namespace Project.Infrastructure.Entities;

public class SpecSerialRootEntity
{
    public string SerialCode { get; set; } = string.Empty;
    public int RootNodeId { get; set; }
    public SpecNodeEntity? RootNode { get; set; }
}
