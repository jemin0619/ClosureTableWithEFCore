namespace Project.Domain;

public class Specification
{
    public string Name { get; set; }
    public InnerSpecA InnerSpecA { get; set; }
    public InnerSpecB InnerSpecB { get; set; }
}

public class InnerSpecA
{
    public int A { get; set; }
    public int B { get; set; }
}

public class InnerSpecB
{
    public string C { get; set; }
    public string D { get; set; }
    public InnerSpecC InnerSpecC { get; set; }
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