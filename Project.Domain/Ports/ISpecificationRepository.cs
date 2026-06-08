namespace Project.Domain.Ports;

public interface ISpecificationRepository
{
    Task CreateAsync<TSpecification>(string serialCode, TSpecification specification, CancellationToken cancellationToken = default)
        where TSpecification : class;

    Task SaveAsync<TSpecification>(string serialCode, TSpecification specification, CancellationToken cancellationToken = default)
        where TSpecification : class;

    Task<TSpecification?> LoadAsync<TSpecification>(string serialCode, CancellationToken cancellationToken = default)
        where TSpecification : class, new();

    Task UpdateAsync<TSpecification>(string serialCode, TSpecification specification, CancellationToken cancellationToken = default)
        where TSpecification : class;

    Task<bool> DeleteAsync(string serialCode, CancellationToken cancellationToken = default);

    Task<string?> GetValueByPathAsync(string serialCode, string path, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListSerialCodesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> QuerySerialCodesAsync(SpecQueryCondition condition, CancellationToken cancellationToken = default);
}
