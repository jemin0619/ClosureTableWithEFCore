namespace Project.Infrastructure.Entities;

public class SpecClosureEntity
{
    public int AncestorId { get; set; }
    public int DescendantId { get; set; }
    public int Depth { get; set; }

    public SpecNodeEntity? Ancestor { get; set; }
    public SpecNodeEntity? Descendant { get; set; }
}
