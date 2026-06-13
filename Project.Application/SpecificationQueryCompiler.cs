using System.Globalization;
using System.Reflection;
using System.Collections.Concurrent;
using Project.Domain;

namespace Project.Application;

internal static partial class SpecificationQueryCompiler
{
    private static readonly ConcurrentDictionary<string, string[]> PathSegmentCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ReadablePropertiesCache = new();
    private static readonly ConcurrentDictionary<(Type Type, string Segment), PropertyInfo[]> SegmentCandidateCache = new();

    public static Func<TSpecification, bool> Compile<TSpecification>(string query)
        where TSpecification : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var parser = new Parser(query);
        var expression = parser.Parse();
        return specification => EvaluateBooleanExpression(expression, specification);
    }

    public static SpecQueryCondition BuildDbCondition(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var parser = new Parser(query);
        var expression = parser.Parse();
        return ConvertToDbCondition(expression);
    }

    private static SpecQueryCondition ConvertToDbCondition(AstNode node)
    {
        return node switch
        {
            BinaryLogicalNode { Operator: LogicalOperator.And } and =>
                new AndSpecQueryCondition(ConvertToDbCondition(and.Left), ConvertToDbCondition(and.Right)),
            BinaryLogicalNode { Operator: LogicalOperator.Or } or =>
                new OrSpecQueryCondition(ConvertToDbCondition(or.Left), ConvertToDbCondition(or.Right)),
            UnaryLogicalNode notNode =>
                new NotSpecQueryCondition(ConvertToDbCondition(notNode.Operand)),
            ComparisonNode cmp => ConvertComparisonToDbCondition(cmp),
            _ => throw new InvalidOperationException("DB 조건으로 변환할 수 없는 쿼리야.")
        };
    }

    private static LeafSpecQueryCondition ConvertComparisonToDbCondition(ComparisonNode node)
    {
        if (node.Left is not PathOperandNode leftPath)
        {
            throw new InvalidOperationException("비교식의 왼쪽은 경로여야 해.");
        }

        var valueText = ResolveDbRightHandValue(node.Right);

        var pathSegments = leftPath.Path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var op = node.Operator switch
        {
            ComparisonOperator.Equal => SpecQueryOperator.Equal,
            ComparisonOperator.GreaterThan => SpecQueryOperator.GreaterThan,
            ComparisonOperator.GreaterThanOrEqual => SpecQueryOperator.GreaterThanOrEqual,
            ComparisonOperator.LessThan => SpecQueryOperator.LessThan,
            ComparisonOperator.LessThanOrEqual => SpecQueryOperator.LessThanOrEqual,
            ComparisonOperator.Like => SpecQueryOperator.Like,
            _ => throw new InvalidOperationException("알 수 없는 비교 연산자야.")
        };

        return new LeafSpecQueryCondition(pathSegments, op, valueText);
    }

    private static string ResolveDbRightHandValue(AstNode node)
    {
        if (node is LiteralOperandNode literal)
        {
            return literal.Value is null
                ? string.Empty
                : Convert.ToString(literal.Value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        if (node is PathOperandNode rightPath && !rightPath.Path.Contains('.', StringComparison.Ordinal))
        {
            // 점(.) 없는 단순 식별자 = enum 리터럴 등 (예: Type == TypeA)
            return rightPath.Path;
        }

        if (node is ArithmeticOperandNode arith)
        {
            // 상수 산술식만 허용: 경로 없이 리터럴만으로 이루어진 식을 상수 폴딩
            try
            {
                var folded = FoldArithmeticConstant(arith);
                return Convert.ToString(folded, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            catch (InvalidOperationException inner)
            {
                throw new InvalidOperationException(
                    "비교식의 오른쪽 산술식에는 상수만 사용할 수 있어 (경로 포함 불가).", inner);
            }
        }

        throw new InvalidOperationException("비교식의 오른쪽은 리터럴이거나 단순 식별자여야 해.");
    }

    private static decimal FoldArithmeticConstant(ArithmeticOperandNode node)
    {
        var left = FoldAstNodeConstant(node.Left);
        var right = FoldAstNodeConstant(node.Right);
        return node.Operator switch
        {
            ArithmeticOperator.Add => left + right,
            ArithmeticOperator.Subtract => left - right,
            ArithmeticOperator.Multiply => left * right,
            ArithmeticOperator.Divide => right == 0
                ? throw new InvalidOperationException("0으로 나눌 수 없어.")
                : left / right,
            _ => throw new InvalidOperationException("알 수 없는 산술 연산자야.")
        };
    }

    private static decimal FoldAstNodeConstant(AstNode node)
    {
        if (node is LiteralOperandNode { Value: not null } literal
            && TryConvertToDecimal(literal.Value, out var val))
        {
            return val;
        }

        if (node is ArithmeticOperandNode arith)
        {
            return FoldArithmeticConstant(arith);
        }

        throw new InvalidOperationException("DB 쿼리에서 경로를 포함한 산술식은 지원하지 않아. 오른쪽에는 상수 산술식만 사용할 수 있어.");
    }

    private static bool EvaluateBooleanExpression(AstNode node, object? specification)
    {
        if (specification is null)
        {
            return false;
        }

        return node switch
        {
            BinaryLogicalNode logicalNode => logicalNode.Operator switch
            {
                LogicalOperator.And => EvaluateBooleanExpression(logicalNode.Left, specification)
                                       && EvaluateBooleanExpression(logicalNode.Right, specification),
                LogicalOperator.Or => EvaluateBooleanExpression(logicalNode.Left, specification)
                                      || EvaluateBooleanExpression(logicalNode.Right, specification),
                _ => throw new InvalidOperationException("Unknown logical operator.")
            },
            UnaryLogicalNode unaryNode => !EvaluateBooleanExpression(unaryNode.Operand, specification),
            ComparisonNode comparisonNode => EvaluateComparison(comparisonNode, specification),
            _ => throw new InvalidOperationException("Unsupported AST node.")
        };
    }

    private static bool EvaluateComparison(ComparisonNode node, object specification)
    {
        var left = EvaluateValue(node.Left, specification);

        if (node.Operator == ComparisonOperator.Like)
        {
            var rightLike = EvaluateValue(node.Right, specification);
            return EvaluateLike(left, rightLike);
        }

        var right = EvaluateValue(node.Right, specification, left?.GetType().IsEnum == true ? left.GetType() : null);
        var comparisonResult = CompareValues(left, right);

        return node.Operator switch
        {
            ComparisonOperator.Equal => comparisonResult == 0,
            ComparisonOperator.GreaterThanOrEqual => comparisonResult >= 0,
            ComparisonOperator.GreaterThan => comparisonResult > 0,
            ComparisonOperator.LessThanOrEqual => comparisonResult <= 0,
            ComparisonOperator.LessThan => comparisonResult < 0,
            _ => throw new InvalidOperationException("Unknown comparison operator.")
        };
    }

    private static bool EvaluateLike(object? left, object? right)
    {
        if (left is null || right is null)
        {
            throw new InvalidOperationException("비교할 값이 비어있어.");
        }

        if (left is string leftStr && right is string rightStr)
        {
            return leftStr.Contains(rightStr, StringComparison.OrdinalIgnoreCase);
        }

        throw new InvalidOperationException("like 연산자는 문자열에만 사용할 수 있어.");
    }

    private static object? EvaluateValue(AstNode node, object specification, Type? expectedEnumType = null)
    {
        return node switch
        {
            LiteralOperandNode literalNode => literalNode.Value,
            PathOperandNode pathNode => ResolveComparisonPathOperand(specification, pathNode.Path, expectedEnumType),
            ArithmeticOperandNode arithNode => EvaluateArithmetic(arithNode, specification),
            _ => throw new InvalidOperationException("값으로 평가할 수 없는 노드야.")
        };
    }

    private static decimal EvaluateArithmetic(ArithmeticOperandNode node, object specification)
    {
        var left = EvaluateValue(node.Left, specification);
        var right = EvaluateValue(node.Right, specification);

        if (left is null || right is null)
        {
            throw new InvalidOperationException("산술 연산의 피연산자가 null이야.");
        }

        if (!TryConvertToDecimal(left, out var leftDecimal))
        {
            throw new InvalidOperationException($"산술 연산의 왼쪽 값 '{left}'을(를) 숫자로 변환할 수 없어.");
        }

        if (!TryConvertToDecimal(right, out var rightDecimal))
        {
            throw new InvalidOperationException($"산술 연산의 오른쪽 값 '{right}'을(를) 숫자로 변환할 수 없어.");
        }

        return node.Operator switch
        {
            ArithmeticOperator.Add => leftDecimal + rightDecimal,
            ArithmeticOperator.Subtract => leftDecimal - rightDecimal,
            ArithmeticOperator.Multiply => leftDecimal * rightDecimal,
            ArithmeticOperator.Divide => rightDecimal == 0
                ? throw new InvalidOperationException("0으로 나눌 수 없어.")
                : leftDecimal / rightDecimal,
            _ => throw new InvalidOperationException("알 수 없는 산술 연산자야.")
        };
    }

    private static object? ResolveComparisonPathOperand(object root, string path, Type? expectedEnumType)
    {
        var resolution = ResolvePath(root, path);
        if (resolution.Status == PathResolutionStatus.Success)
        {
            return resolution.Value;
        }

        if (expectedEnumType?.IsEnum == true && !path.Contains('.', StringComparison.Ordinal))
        {
            return path;
        }

        if (resolution.Status == PathResolutionStatus.Ambiguous)
        {
            throw new InvalidOperationException($"'{path}' 경로가 모호해.");
        }

        throw new InvalidOperationException($"'{path}' 경로를 찾을 수 없어.");
    }

    private static PathResolutionResult ResolvePath(object root, string path)
    {
        var segments = PathSegmentCache.GetOrAdd(path, static key =>
            key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (segments.Length == 0)
        {
            throw new InvalidOperationException("경로가 비어있어.");
        }

        return ResolvePathRecursive(root, segments, 0);
    }

    private static PathResolutionResult ResolvePathRecursive(object? current, IReadOnlyList<string> segments, int segmentIndex)
    {
        if (current is null)
        {
            return PathResolutionResult.NotFound();
        }

        if (segmentIndex >= segments.Count)
        {
            return PathResolutionResult.Success(current);
        }

        var segment = segments[segmentIndex];
        var candidates = GetSegmentCandidates(current.GetType(), segment);

        if (candidates.Length == 0)
        {
            return PathResolutionResult.NotFound();
        }

        if (candidates.Length == 1)
        {
            var value = candidates[0].GetValue(current);
            return ResolvePathRecursive(value, segments, segmentIndex + 1);
        }

        PathResolutionResult? resolved = null;
        foreach (var candidate in candidates)
        {
            var branch = ResolvePathRecursive(candidate.GetValue(current), segments, segmentIndex + 1);
            if (branch.Status != PathResolutionStatus.Success)
            {
                continue;
            }

            if (resolved is not null)
            {
                return PathResolutionResult.Ambiguous();
            }

            resolved = branch;
        }

        return resolved ?? PathResolutionResult.NotFound();
    }

    private static PropertyInfo[] GetSegmentCandidates(Type type, string segment)
    {
        return SegmentCandidateCache.GetOrAdd((type, segment), static key =>
        {
            var properties = GetReadableProperties(key.Type);
            var exact = properties
                .Where(x => string.Equals(x.Name, key.Segment, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (exact.Length > 0)
            {
                return exact;
            }

            return properties
                .Where(x => x.Name.StartsWith(key.Segment, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        });
    }

    private static PropertyInfo[] GetReadableProperties(Type type)
    {
        return ReadablePropertiesCache.GetOrAdd(type, static currentType =>
            currentType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => x.CanRead)
                .ToArray());
    }

    private static int CompareValues(object? left, object? right)
    {
        if (left is null || right is null)
        {
            throw new InvalidOperationException("비교할 값이 비어있어.");
        }

        if (TryConvertToDecimal(left, out var leftDecimal) && TryConvertToDecimal(right, out var rightDecimal))
        {
            return leftDecimal.CompareTo(rightDecimal);
        }

        if (left is bool leftBool && right is bool rightBool)
        {
            return leftBool.CompareTo(rightBool);
        }

        if (left.GetType().IsEnum && right.GetType().IsEnum)
        {
            var leftEnum = Convert.ToInt64(left, CultureInfo.InvariantCulture);
            var rightEnum = Convert.ToInt64(right, CultureInfo.InvariantCulture);
            return leftEnum.CompareTo(rightEnum);
        }

        if (left.GetType().IsEnum && right is string rightEnumText)
        {
            var parsed = ParseEnumValue(left.GetType(), rightEnumText);
            var leftEnum = Convert.ToInt64(left, CultureInfo.InvariantCulture);
            var parsedEnum = Convert.ToInt64(parsed, CultureInfo.InvariantCulture);
            return leftEnum.CompareTo(parsedEnum);
        }

        if (right.GetType().IsEnum && left is string leftEnumText)
        {
            var parsed = ParseEnumValue(right.GetType(), leftEnumText);
            var parsedEnum = Convert.ToInt64(parsed, CultureInfo.InvariantCulture);
            var rightEnum = Convert.ToInt64(right, CultureInfo.InvariantCulture);
            return parsedEnum.CompareTo(rightEnum);
        }

        if (left is string leftText && right is string rightText)
        {
            return string.Compare(leftText, rightText, StringComparison.OrdinalIgnoreCase);
        }

        if (left is IComparable comparable && right.GetType() == left.GetType())
        {
            return comparable.CompareTo(right);
        }

        throw new InvalidOperationException($"'{left.GetType().Name}'과 '{right.GetType().Name}'은(는) 비교할 수 없어.");
    }

    private static bool TryConvertToDecimal(object value, out decimal converted)
    {
        try
        {
            converted = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            converted = 0;
            return false;
        }
    }

    private static object ParseEnumValue(Type enumType, string enumText)
    {
        if (Enum.TryParse(enumType, enumText, true, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"'{enumText}'은(는) {enumType.Name} enum 값이 아니야.");
    }

    private readonly record struct PathResolutionResult(PathResolutionStatus Status, object? Value)
    {
        public static PathResolutionResult Success(object? value) => new(PathResolutionStatus.Success, value);
        public static PathResolutionResult NotFound() => new(PathResolutionStatus.NotFound, null);
        public static PathResolutionResult Ambiguous() => new(PathResolutionStatus.Ambiguous, null);
    }

    private enum PathResolutionStatus
    {
        Success,
        NotFound,
        Ambiguous
    }

    private sealed class Parser
    {
        private readonly IReadOnlyList<Token> _tokens;
        private int _position;

        public Parser(string text)
        {
            _tokens = Tokenize(text);
        }

        public AstNode Parse()
        {
            var expression = ParseOr();
            Expect(TokenType.End);
            return expression;
        }

        private AstNode ParseOr()
        {
            var left = ParseAnd();
            while (Match(TokenType.Or))
            {
                var right = ParseAnd();
                left = new BinaryLogicalNode(LogicalOperator.Or, left, right);
            }

            return left;
        }

        private AstNode ParseAnd()
        {
            var left = ParseUnary();
            while (Match(TokenType.And))
            {
                var right = ParseUnary();
                left = new BinaryLogicalNode(LogicalOperator.And, left, right);
            }

            return left;
        }

        private AstNode ParseUnary()
        {
            if (Match(TokenType.Not))
            {
                return new UnaryLogicalNode(ParseUnary());
            }

            return ParseComparison();
        }

        // Handles optional comparison operator between two additive expressions.
        private AstNode ParseComparison()
        {
            var left = ParseAdditive();
            if (!CurrentIsComparison())
            {
                return left;
            }

            var comparisonOperatorToken = Next();
            var right = ParseAdditive();
            return new ComparisonNode(left, right, comparisonOperatorToken.Type switch
            {
                TokenType.Equal => ComparisonOperator.Equal,
                TokenType.GreaterThanOrEqual => ComparisonOperator.GreaterThanOrEqual,
                TokenType.GreaterThan => ComparisonOperator.GreaterThan,
                TokenType.LessThanOrEqual => ComparisonOperator.LessThanOrEqual,
                TokenType.LessThan => ComparisonOperator.LessThan,
                TokenType.Like => ComparisonOperator.Like,
                _ => throw new InvalidOperationException("Unknown comparison operator.")
            });
        }

        // Handles + and - (lowest arithmetic precedence).
        private AstNode ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (Peek().Type is TokenType.Plus or TokenType.Minus)
            {
                var opToken = Next();
                var right = ParseMultiplicative();
                var op = opToken.Type == TokenType.Plus ? ArithmeticOperator.Add : ArithmeticOperator.Subtract;
                left = new ArithmeticOperandNode(left, right, op);
            }

            return left;
        }

        // Handles * and / (higher arithmetic precedence).
        private AstNode ParseMultiplicative()
        {
            var left = ParseAtom();
            while (Peek().Type is TokenType.Multiply or TokenType.Divide)
            {
                var opToken = Next();
                var right = ParseAtom();
                var op = opToken.Type == TokenType.Multiply ? ArithmeticOperator.Multiply : ArithmeticOperator.Divide;
                left = new ArithmeticOperandNode(left, right, op);
            }

            return left;
        }

        // Handles base operands and parenthesised sub-expressions.
        private AstNode ParseAtom()
        {
            if (Match(TokenType.OpenParenthesis))
            {
                var nested = ParseOr();
                Expect(TokenType.CloseParenthesis);
                return nested;
            }

            var token = Next();
            return token.Type switch
            {
                TokenType.Identifier => new PathOperandNode(token.Text),
                TokenType.String => new LiteralOperandNode(token.Text),
                TokenType.Number => new LiteralOperandNode(ParseNumber(token.Text)),
                TokenType.Boolean => new LiteralOperandNode(bool.Parse(token.Text)),
                _ => throw new InvalidOperationException($"예상하지 못한 토큰: '{token.Text}'.")
            };
        }

        private bool CurrentIsComparison()
        {
            return Peek().Type is TokenType.Equal
                or TokenType.GreaterThan
                or TokenType.GreaterThanOrEqual
                or TokenType.LessThan
                or TokenType.LessThanOrEqual
                or TokenType.Like;
        }

        private bool Match(TokenType tokenType)
        {
            if (Peek().Type != tokenType)
            {
                return false;
            }

            _position++;
            return true;
        }

        private void Expect(TokenType tokenType)
        {
            var token = Next();
            if (token.Type != tokenType)
            {
                throw new InvalidOperationException($"'{token.Text}' 위치의 쿼리 문법이 올바르지 않아.");
            }
        }

        private Token Peek()
        {
            if (_position >= _tokens.Count)
            {
                return new Token(TokenType.End, string.Empty);
            }

            return _tokens[_position];
        }

        private Token Next()
        {
            var token = Peek();
            _position++;
            return token;
        }

        private static decimal ParseNumber(string text)
        {
            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            throw new InvalidOperationException($"'{text}'은(는) 숫자가 아니야.");
        }

        private static IReadOnlyList<Token> Tokenize(string query)
        {
            var tokens = new List<Token>();
            var index = 0;
            // Tracks the last emitted token type to distinguish binary '-' (subtraction) from a negative number literal.
            TokenType? lastEmittedTokenType = null;

            while (index < query.Length)
            {
                var current = query[index];
                if (char.IsWhiteSpace(current))
                {
                    index++;
                    continue;
                }

                switch (current)
                {
                    case '(':
                        tokens.Add(new Token(TokenType.OpenParenthesis, "("));
                        lastEmittedTokenType = TokenType.OpenParenthesis;
                        index++;
                        continue;
                    case ')':
                        tokens.Add(new Token(TokenType.CloseParenthesis, ")"));
                        lastEmittedTokenType = TokenType.CloseParenthesis;
                        index++;
                        continue;
                    case '+':
                        tokens.Add(new Token(TokenType.Plus, "+"));
                        lastEmittedTokenType = TokenType.Plus;
                        index++;
                        continue;
                    case '*':
                        tokens.Add(new Token(TokenType.Multiply, "*"));
                        lastEmittedTokenType = TokenType.Multiply;
                        index++;
                        continue;
                    case '/':
                        tokens.Add(new Token(TokenType.Divide, "/"));
                        lastEmittedTokenType = TokenType.Divide;
                        index++;
                        continue;
                    case '>':
                        if (index + 1 < query.Length && query[index + 1] == '=')
                        {
                            tokens.Add(new Token(TokenType.GreaterThanOrEqual, ">="));
                            lastEmittedTokenType = TokenType.GreaterThanOrEqual;
                            index += 2;
                        }
                        else
                        {
                            tokens.Add(new Token(TokenType.GreaterThan, ">"));
                            lastEmittedTokenType = TokenType.GreaterThan;
                            index++;
                        }

                        continue;
                    case '<':
                        if (index + 1 < query.Length && query[index + 1] == '=')
                        {
                            tokens.Add(new Token(TokenType.LessThanOrEqual, "<="));
                            lastEmittedTokenType = TokenType.LessThanOrEqual;
                            index += 2;
                        }
                        else
                        {
                            tokens.Add(new Token(TokenType.LessThan, "<"));
                            lastEmittedTokenType = TokenType.LessThan;
                            index++;
                        }

                        continue;
                    case '=':
                        if (index + 1 < query.Length && query[index + 1] == '=')
                        {
                            tokens.Add(new Token(TokenType.Equal, "=="));
                            lastEmittedTokenType = TokenType.Equal;
                            index += 2;
                        }
                        else
                        {
                            tokens.Add(new Token(TokenType.Equal, "="));
                            lastEmittedTokenType = TokenType.Equal;
                            index++;
                        }

                        continue;
                    case '&':
                        if (index + 1 < query.Length && query[index + 1] == '&')
                        {
                            tokens.Add(new Token(TokenType.And, "&&"));
                            lastEmittedTokenType = TokenType.And;
                            index += 2;
                            continue;
                        }

                        break;
                    case '|':
                        if (index + 1 < query.Length && query[index + 1] == '|')
                        {
                            tokens.Add(new Token(TokenType.Or, "||"));
                            lastEmittedTokenType = TokenType.Or;
                            index += 2;
                            continue;
                        }

                        break;
                    case '!':
                        tokens.Add(new Token(TokenType.Not, "!"));
                        lastEmittedTokenType = TokenType.Not;
                        index++;
                        continue;
                    case '"':
                    case '\'':
                        {
                            var quote = current;
                            index++;
                            var start = index;
                            while (index < query.Length && query[index] != quote)
                            {
                                index++;
                            }

                            if (index >= query.Length)
                            {
                                throw new InvalidOperationException("문자열 리터럴이 닫히지 않았어.");
                            }

                            var text = query[start..index];
                            tokens.Add(new Token(TokenType.String, text));
                            lastEmittedTokenType = TokenType.String;
                            index++;
                            continue;
                        }
                }

                // '-': binary subtraction when preceded by an operand-ending token; negative number literal otherwise.
                if (current == '-')
                {
                    bool isAfterOperand = lastEmittedTokenType is TokenType.Identifier
                        or TokenType.Number
                        or TokenType.String
                        or TokenType.Boolean
                        or TokenType.CloseParenthesis;

                    if (isAfterOperand)
                    {
                        tokens.Add(new Token(TokenType.Minus, "-"));
                        lastEmittedTokenType = TokenType.Minus;
                        index++;
                        continue;
                    }

                    // negative number literal: '-' must be followed by a digit
                    if (index + 1 < query.Length && char.IsDigit(query[index + 1]))
                    {
                        var start = index;
                        index++;
                        while (index < query.Length && (char.IsDigit(query[index]) || query[index] == '.'))
                        {
                            index++;
                        }

                        tokens.Add(new Token(TokenType.Number, query[start..index]));
                        lastEmittedTokenType = TokenType.Number;
                        continue;
                    }

                    throw new InvalidOperationException("'-' 다음에 숫자가 와야 해.");
                }

                if (char.IsDigit(current))
                {
                    var start = index;
                    index++;
                    while (index < query.Length && (char.IsDigit(query[index]) || query[index] == '.'))
                    {
                        index++;
                    }

                    tokens.Add(new Token(TokenType.Number, query[start..index]));
                    lastEmittedTokenType = TokenType.Number;
                    continue;
                }

                if (IsIdentifierStart(current))
                {
                    var start = index;
                    index++;
                    while (index < query.Length && IsIdentifierPart(query[index]))
                    {
                        index++;
                    }

                    var tokenText = query[start..index];
                    if (tokenText.Equals("and", StringComparison.OrdinalIgnoreCase))
                    {
                        tokens.Add(new Token(TokenType.And, tokenText));
                        lastEmittedTokenType = TokenType.And;
                        continue;
                    }

                    if (tokenText.Equals("or", StringComparison.OrdinalIgnoreCase))
                    {
                        tokens.Add(new Token(TokenType.Or, tokenText));
                        lastEmittedTokenType = TokenType.Or;
                        continue;
                    }

                    if (tokenText.Equals("not", StringComparison.OrdinalIgnoreCase))
                    {
                        tokens.Add(new Token(TokenType.Not, tokenText));
                        lastEmittedTokenType = TokenType.Not;
                        continue;
                    }

                    if (tokenText.Equals("like", StringComparison.OrdinalIgnoreCase))
                    {
                        tokens.Add(new Token(TokenType.Like, tokenText));
                        lastEmittedTokenType = TokenType.Like;
                        continue;
                    }

                    if (tokenText.Equals("true", StringComparison.OrdinalIgnoreCase)
                        || tokenText.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        tokens.Add(new Token(TokenType.Boolean, tokenText.ToLowerInvariant()));
                        lastEmittedTokenType = TokenType.Boolean;
                        continue;
                    }

                    tokens.Add(new Token(TokenType.Identifier, tokenText));
                    lastEmittedTokenType = TokenType.Identifier;
                    continue;
                }

                throw new InvalidOperationException($"'{current}' 문자를 해석할 수 없어.");
            }

            tokens.Add(new Token(TokenType.End, string.Empty));
            return tokens;
        }

        private static bool IsIdentifierStart(char c)
        {
            return char.IsLetter(c) || c == '_';
        }

        private static bool IsIdentifierPart(char c)
        {
            return char.IsLetterOrDigit(c) || c is '_' or '.';
        }
    }

    private readonly record struct Token(TokenType Type, string Text);

    private enum TokenType
    {
        Identifier,
        Number,
        String,
        Boolean,
        Equal,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
        Like,
        And,
        Or,
        Not,
        OpenParenthesis,
        CloseParenthesis,
        Plus,
        Minus,
        Multiply,
        Divide,
        End
    }

    private abstract record AstNode;
    private abstract record OperandNode : AstNode;
    private sealed record PathOperandNode(string Path) : OperandNode;
    private sealed record LiteralOperandNode(object? Value) : OperandNode;
    private sealed record ArithmeticOperandNode(AstNode Left, AstNode Right, ArithmeticOperator Operator) : OperandNode;
    private sealed record ComparisonNode(AstNode Left, AstNode Right, ComparisonOperator Operator) : AstNode;
    private sealed record BinaryLogicalNode(LogicalOperator Operator, AstNode Left, AstNode Right) : AstNode;
    private sealed record UnaryLogicalNode(AstNode Operand) : AstNode;

    private enum ComparisonOperator
    {
        Equal,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
        Like
    }

    private enum LogicalOperator
    {
        And,
        Or
    }

    private enum ArithmeticOperator
    {
        Add,
        Subtract,
        Multiply,
        Divide
    }
}
