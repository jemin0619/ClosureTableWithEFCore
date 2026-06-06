namespace Project.Domain;

public class Specification
{
    public string Name { get; set; } = string.Empty;
    public InnerSpecA InnerSpecA { get; set; } = new();
    public InnerSpecB InnerSpecB { get; set; } = new();
}

public class InnerSpecA
{
    public int A { get; set; }
    public int B { get; set; }
}

public class InnerSpecB
{
    public string C { get; set; } = string.Empty;
    public string D { get; set; } = string.Empty;
    public InnerSpecC InnerSpecC { get; set; } = new();
}

public class InnerSpecC
{
    public SpecType Type { get; set; }
}

public enum SpecType
{
    TypeA,
    TypeB,
    TypeC
}
