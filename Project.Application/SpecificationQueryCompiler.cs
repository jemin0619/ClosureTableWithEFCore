using System.Globalization;
using System.Reflection;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Project.Application;

internal static partial class SpecificationQueryCompiler
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<string>> QueryablePathCache = new();
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, IReadOnlyList<string>>> EnumPathValueCache = new();
    private static readonly ConcurrentDictionary<string, string[]> PathSegmentCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ReadablePropertiesCache = new();
    private static readonly ConcurrentDictionary<(Type Type, string Segment), PropertyInfo[]> SegmentCandidateCache = new();
    private static readonly string[] ComparisonOperatorSuggestions = [" == ", " > ", " < "];
    private static readonly string[] LogicalOperatorSuggestions = [" && ", " || "];

    private static readonly IReadOnlySet<Type> LeafTypes = new HashSet<Type>
    {
        typeof(string),
        typeof(bool),
        typeof(byte),
        typeof(sbyte),
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong),
        typeof(float),
        typeof(double),
        typeof(decimal)
    };

    public static Func<TSpecification, bool> Compile<TSpecification>(string query)
        where TSpecification : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var parser = new Parser(query);
        var expression = parser.Parse();
        return specification => EvaluateBooleanExpression(expression, specification);
    }

    public static IReadOnlyList<string> GetQueryablePaths<TSpecification>()
        where TSpecification : class
    {
        return QueryablePathCache.GetOrAdd(typeof(TSpecification), static type =>
        {
            var paths = new List<string>();
            CollectPaths(type, null, paths);
            return paths;
        });
    }

    public static IReadOnlyList<string> GetQuerySuggestions<TSpecification>(string queryText, int maxSuggestionCount = 12)
        where TSpecification : class
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSuggestionCount);

        if (TryGetComparisonOperatorSuggestions<TSpecification>(queryText, out var comparisonOperatorSuggestions))
        {
            return comparisonOperatorSuggestions;
        }

        if (TryGetEnumSuggestions(typeof(TSpecification), queryText, maxSuggestionCount, out var enumSuggestions))
        {
            return enumSuggestions;
        }

        if (TryGetLogicalOperatorSuggestions(queryText, out var logicalOperatorSuggestions))
        {
            return logicalOperatorSuggestions;
        }

        var fragment = GetCurrentIdentifierFragment(queryText);
        if (string.IsNullOrWhiteSpace(fragment) && !IsExpectingPathSuggestion(queryText))
        {
            return [];
        }

        return GetQueryablePaths<TSpecification>()
            .Where(x => string.IsNullOrWhiteSpace(fragment)
                        || x.StartsWith(fragment, StringComparison.OrdinalIgnoreCase)
                        || x.Contains($".{fragment}", StringComparison.OrdinalIgnoreCase))
            .Take(maxSuggestionCount)
            .ToList();
    }

    private static void CollectPaths(Type type, string? prefix, List<string> destination)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead)
            {
                continue;
            }

            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var path = string.IsNullOrWhiteSpace(prefix) ? property.Name : $"{prefix}.{property.Name}";
            destination.Add(path);

            if (IsLeafType(propertyType))
            {
                continue;
            }

            CollectPaths(propertyType, path, destination);
        }
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
            OperandNode operandNode => ConvertToBoolean(EvaluateOperand(operandNode, specification)),
            _ => throw new InvalidOperationException("Unsupported AST node.")
        };
    }

    private static bool EvaluateComparison(ComparisonNode node, object specification)
    {
        var left = EvaluateOperand(node.Left, specification);
        var right = EvaluateOperand(node.Right, specification, left?.GetType().IsEnum == true ? left.GetType() : null);
        var comparisonResult = CompareValues(left, right);

        return node.Operator switch
        {
            ComparisonOperator.Equal => comparisonResult == 0,
            ComparisonOperator.GreaterThan => comparisonResult > 0,
            ComparisonOperator.LessThan => comparisonResult < 0,
            _ => throw new InvalidOperationException("Unknown comparison operator.")
        };
    }

    private static object? EvaluateOperand(OperandNode node, object specification, Type? expectedEnumType = null)
    {
        return node switch
        {
            LiteralOperandNode literalNode => literalNode.Value,
            PathOperandNode pathNode => ResolveComparisonPathOperand(specification, pathNode.Path, expectedEnumType),
            _ => throw new InvalidOperationException("Unsupported operand node.")
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

    private static object? ResolvePathValue(object root, string path)
    {
        var segments = PathSegmentCache.GetOrAdd(path, static key =>
            key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (segments.Length == 0)
        {
            throw new InvalidOperationException("경로가 비어있어.");
        }

        var resolution = ResolvePathRecursive(root, segments, 0);
        if (resolution.Status == PathResolutionStatus.Success)
        {
            return resolution.Value;
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

    private static bool TryGetEnumSuggestions(Type rootType, string queryText, int maxSuggestionCount, out IReadOnlyList<string> suggestions)
    {
        suggestions = [];
        var match = GetEnumSuggestionContextMatch(queryText);
        if (!match.Success)
        {
            return false;
        }

        var path = match.Groups["path"].Value;
        var fragment = match.Groups["fragment"].Success ? match.Groups["fragment"].Value : string.Empty;
        var enumValuesByPath = EnumPathValueCache.GetOrAdd(rootType, BuildEnumPathValueMap);
        if (!enumValuesByPath.TryGetValue(path, out var enumValues))
        {
            return false;
        }

        suggestions = enumValues
            .Where(x => string.IsNullOrWhiteSpace(fragment) || x.StartsWith(fragment, StringComparison.OrdinalIgnoreCase))
            .Take(maxSuggestionCount)
            .ToList();

        return suggestions.Count > 0;
    }

    private static bool TryGetComparisonOperatorSuggestions<TSpecification>(string queryText, out IReadOnlyList<string> suggestions)
        where TSpecification : class
    {
        suggestions = [];
        var fragment = GetCurrentIdentifierFragment(queryText);
        if (string.IsNullOrWhiteSpace(fragment))
        {
            return false;
        }

        if (!GetQueryablePaths<TSpecification>().Contains(fragment, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var trimmed = queryText.TrimEnd();
        if (!trimmed.EndsWith(fragment, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        suggestions = ComparisonOperatorSuggestions;
        return true;
    }

    private static bool TryGetLogicalOperatorSuggestions(string queryText, out IReadOnlyList<string> suggestions)
    {
        suggestions = [];
        if (!CompletedExpressionRegex().IsMatch(queryText))
        {
            return false;
        }

        suggestions = LogicalOperatorSuggestions;
        return true;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildEnumPathValueMap(Type rootType)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        CollectEnumPaths(rootType, null, map);
        return map;
    }

    private static void CollectEnumPaths(Type type, string? prefix, Dictionary<string, IReadOnlyList<string>> destination)
    {
        foreach (var property in GetReadableProperties(type))
        {
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var path = string.IsNullOrWhiteSpace(prefix) ? property.Name : $"{prefix}.{property.Name}";

            if (propertyType.IsEnum)
            {
                destination[path] = Enum.GetNames(propertyType);
                continue;
            }

            if (IsLeafType(propertyType))
            {
                continue;
            }

            CollectEnumPaths(propertyType, path, destination);
        }
    }

    private static Match GetEnumSuggestionContextMatch(string queryText)
    {
        return EnumSuggestionContextRegex().Match(queryText);
    }

    private static string? GetCurrentIdentifierFragment(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = QueryFragmentRegex().Match(text);
        return match.Success ? match.Value : null;
    }

    private static bool IsExpectingPathSuggestion(string queryText)
    {
        var trimmed = queryText.TrimEnd();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return true;
        }

        return trimmed.EndsWith("&&", StringComparison.Ordinal)
               || trimmed.EndsWith("||", StringComparison.Ordinal)
               || trimmed.EndsWith("!", StringComparison.Ordinal)
               || trimmed.EndsWith("(", StringComparison.Ordinal);
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

    private static bool ConvertToBoolean(object? value)
    {
        if (value is null)
        {
            throw new InvalidOperationException("불리언 식으로 평가할 값이 비어있어.");
        }

        if (value is bool boolValue)
        {
            return boolValue;
        }

        throw new InvalidOperationException("조건식 결과가 true/false가 아니야.");
    }

    private static bool IsLeafType(Type type)
    {
        return type.IsEnum || LeafTypes.Contains(type);
    }

    [GeneratedRegex(@"(?<path>[A-Za-z_][A-Za-z0-9_.]*)\s*(==|=|>|<)\s*(?<fragment>[A-Za-z_][A-Za-z0-9_]*)?\s*$", RegexOptions.Compiled)]
    private static partial Regex EnumSuggestionContextRegex();

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.Compiled)]
    private static partial Regex QueryFragmentRegex();

    [GeneratedRegex(@"(?:[A-Za-z_][A-Za-z0-9_]*|-?\d+(?:\.\d+)?|true|false|""[^""]*""|'[^']*'|\))\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex CompletedExpressionRegex();

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

            return ParsePrimary();
        }

        private AstNode ParsePrimary()
        {
            if (Match(TokenType.OpenParenthesis))
            {
                var nested = ParseOr();
                Expect(TokenType.CloseParenthesis);
                return nested;
            }

            return ParseComparisonOrOperand();
        }

        private AstNode ParseComparisonOrOperand()
        {
            var left = ParseOperand();
            if (!CurrentIsComparison())
            {
                return left;
            }

            var comparisonOperatorToken = Next();
            var right = ParseOperand();
            return new ComparisonNode(left, right, comparisonOperatorToken.Type switch
            {
                TokenType.Equal => ComparisonOperator.Equal,
                TokenType.GreaterThan => ComparisonOperator.GreaterThan,
                TokenType.LessThan => ComparisonOperator.LessThan,
                _ => throw new InvalidOperationException("Unknown comparison operator.")
            });
        }

        private OperandNode ParseOperand()
        {
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
            return Peek().Type is TokenType.Equal or TokenType.GreaterThan or TokenType.LessThan;
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
                        index++;
                        continue;
                    case ')':
                        tokens.Add(new Token(TokenType.CloseParenthesis, ")"));
                        index++;
                        continue;
                    case '>':
                        tokens.Add(new Token(TokenType.GreaterThan, ">"));
                        index++;
                        continue;
                    case '<':
                        tokens.Add(new Token(TokenType.LessThan, "<"));
                        index++;
                        continue;
                    case '=':
                        if (index + 1 < query.Length && query[index + 1] == '=')
                        {
                            tokens.Add(new Token(TokenType.Equal, "=="));
                            index += 2;
                        }
                        else
                        {
                            tokens.Add(new Token(TokenType.Equal, "="));
                            index++;
                        }

                        continue;
                    case '&':
                        if (index + 1 < query.Length && query[index + 1] == '&')
                        {
                            tokens.Add(new Token(TokenType.And, "&&"));
                            index += 2;
                            continue;
                        }

                        break;
                    case '|':
                        if (index + 1 < query.Length && query[index + 1] == '|')
                        {
                            tokens.Add(new Token(TokenType.Or, "||"));
                            index += 2;
                            continue;
                        }

                        break;
                    case '!':
                        tokens.Add(new Token(TokenType.Not, "!"));
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
                            index++;
                            continue;
                        }
                }

                if (char.IsDigit(current) || current == '-' && index + 1 < query.Length && char.IsDigit(query[index + 1]))
                {
                    var start = index;
                    index++;
                    while (index < query.Length && (char.IsDigit(query[index]) || query[index] == '.'))
                    {
                        index++;
                    }

                    tokens.Add(new Token(TokenType.Number, query[start..index]));
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
                        continue;
                    }

                    if (tokenText.Equals("or", StringComparison.OrdinalIgnoreCase))
                    {
                        tokens.Add(new Token(TokenType.Or, tokenText));
                        continue;
                    }

                    if (tokenText.Equals("not", StringComparison.OrdinalIgnoreCase))
                    {
                        tokens.Add(new Token(TokenType.Not, tokenText));
                        continue;
                    }

                    if (tokenText.Equals("true", StringComparison.OrdinalIgnoreCase)
                        || tokenText.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        tokens.Add(new Token(TokenType.Boolean, tokenText.ToLowerInvariant()));
                        continue;
                    }

                    tokens.Add(new Token(TokenType.Identifier, tokenText));
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
        LessThan,
        And,
        Or,
        Not,
        OpenParenthesis,
        CloseParenthesis,
        End
    }

    private abstract record AstNode;
    private abstract record OperandNode : AstNode;
    private sealed record PathOperandNode(string Path) : OperandNode;
    private sealed record LiteralOperandNode(object? Value) : OperandNode;
    private sealed record ComparisonNode(OperandNode Left, OperandNode Right, ComparisonOperator Operator) : AstNode;
    private sealed record BinaryLogicalNode(LogicalOperator Operator, AstNode Left, AstNode Right) : AstNode;
    private sealed record UnaryLogicalNode(AstNode Operand) : AstNode;

    private enum ComparisonOperator
    {
        Equal,
        GreaterThan,
        LessThan
    }

    private enum LogicalOperator
    {
        And,
        Or
    }
}
