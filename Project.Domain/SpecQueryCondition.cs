namespace Project.Domain;

public abstract record SpecQueryCondition;

public sealed record ComparisonSpecQueryCondition(
    SpecQueryOperand Left,
    SpecQueryOperator Operator,
    SpecQueryOperand Right) : SpecQueryCondition;

public sealed record AndSpecQueryCondition(
    SpecQueryCondition Left,
    SpecQueryCondition Right) : SpecQueryCondition;

public sealed record OrSpecQueryCondition(
    SpecQueryCondition Left,
    SpecQueryCondition Right) : SpecQueryCondition;

public sealed record NotSpecQueryCondition(
    SpecQueryCondition Inner) : SpecQueryCondition;

public abstract record SpecQueryOperand;

public sealed record PathSpecQueryOperand(
    string[] PathSegments) : SpecQueryOperand;

public sealed record LiteralSpecQueryOperand(
    object? Value) : SpecQueryOperand;

public sealed record BinaryArithmeticSpecQueryOperand(
    SpecQueryArithmeticOperator Operator,
    SpecQueryOperand Left,
    SpecQueryOperand Right) : SpecQueryOperand;

public sealed record UnaryArithmeticSpecQueryOperand(
    SpecQueryOperand Operand) : SpecQueryOperand;

public enum SpecQueryOperator
{
    Equal,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Like
}

public enum SpecQueryArithmeticOperator
{
    Add,
    Subtract,
    Multiply,
    Divide
}
