namespace Project.Domain;

public abstract record SpecQueryCondition;

public sealed record LeafSpecQueryCondition(
    string[] PathSegments,
    SpecQueryOperator Operator,
    string Value) : SpecQueryCondition;

public sealed record AndSpecQueryCondition(
    SpecQueryCondition Left,
    SpecQueryCondition Right) : SpecQueryCondition;

public sealed record OrSpecQueryCondition(
    SpecQueryCondition Left,
    SpecQueryCondition Right) : SpecQueryCondition;

public sealed record NotSpecQueryCondition(
    SpecQueryCondition Inner) : SpecQueryCondition;

// Arithmetic condition: supports expressions like A + B == 5
public sealed record ArithmeticSpecQueryCondition(
    SpecQueryOperand Left,
    SpecQueryOperator Operator,
    SpecQueryOperand Right) : SpecQueryCondition;

public enum SpecQueryOperator
{
    Equal,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Like
}

// Operand expression nodes used in ArithmeticSpecQueryCondition
public abstract record SpecQueryOperand;

public sealed record PathSpecQueryOperand(string[] PathSegments) : SpecQueryOperand;

public sealed record LiteralSpecQueryOperand(string Value) : SpecQueryOperand;

public sealed record BinaryArithmeticSpecQueryOperand(
    SpecArithmeticOperator Operator,
    SpecQueryOperand Left,
    SpecQueryOperand Right) : SpecQueryOperand;

public enum SpecArithmeticOperator
{
    Add,
    Subtract,
    Multiply,
    Divide
}
