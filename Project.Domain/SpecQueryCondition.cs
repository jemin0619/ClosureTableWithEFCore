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

public enum SpecQueryOperator
{
    Equal,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Like
}
