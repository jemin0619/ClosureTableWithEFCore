namespace Project.Domain.Ports;

public interface ISpecificationRepository
{
    Task SaveAsync<TSpecification>(string serialCode, TSpecification specification, CancellationToken cancellationToken = default)
        where TSpecification : class;

    Task<TSpecification?> LoadAsync<TSpecification>(string serialCode, CancellationToken cancellationToken = default)
        where TSpecification : class, new();

    Task<string?> GetValueByPathAsync(string serialCode, string path, CancellationToken cancellationToken = default);
}
