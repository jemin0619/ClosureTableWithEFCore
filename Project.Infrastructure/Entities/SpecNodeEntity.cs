namespace Project.Infrastructure.Entities;

public class SpecNodeEntity
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public DateTime UpdatedAt { get; set; }
}
