using Project.Domain.Ports;

namespace Project.Application;

public interface ISpecificationService
{
    Task CreateSpecificationAsync<TSpecification>(string serialCode, TSpecification specification, CancellationToken cancellationToken = default)
        where TSpecification : class;

    Task SaveSpecificationAsync<TSpecification>(string serialCode, TSpecification specification, CancellationToken cancellationToken = default)
        where TSpecification : class;

    Task<TSpecification?> LoadSpecificationAsync<TSpecification>(string serialCode, CancellationToken cancellationToken = default)
        where TSpecification : class, new();

    Task UpdateSpecificationAsync<TSpecification>(string serialCode, TSpecification specification, CancellationToken cancellationToken = default)
        where TSpecification : class;

    Task<bool> DeleteSpecificationAsync(string serialCode, CancellationToken cancellationToken = default);

    Task<string?> GetSpecValueAsync(string serialCode, string path, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListSerialCodesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> QuerySerialCodesAsync<TSpecification>(string query, CancellationToken cancellationToken = default)
        where TSpecification : class, new();
}

public sealed class SpecificationService(ISpecificationRepository specificationRepository) : ISpecificationService, ISpecValueReader
{
    private readonly ISpecificationRepository _specificationRepository = specificationRepository;

    public Task CreateSpecificationAsync<TSpecification>(string serialCode, TSpecification specification, CancellationToken cancellationToken = default)
        where TSpecification : class
    {
        return _specificationRepository.CreateAsync(serialCode, specification, cancellationToken);
    }

    public Task SaveSpecificationAsync<TSpecification>(string serialCode, TSpecification specification, CancellationToken cancellationToken = default)
        where TSpecification : class
    {
        return _specificationRepository.SaveAsync(serialCode, specification, cancellationToken);
    }

    public Task<TSpecification?> LoadSpecificationAsync<TSpecification>(string serialCode, CancellationToken cancellationToken = default)
        where TSpecification : class, new()
    {
        return _specificationRepository.LoadAsync<TSpecification>(serialCode, cancellationToken);
    }

    public Task UpdateSpecificationAsync<TSpecification>(string serialCode, TSpecification specification, CancellationToken cancellationToken = default)
        where TSpecification : class
    {
        return _specificationRepository.UpdateAsync(serialCode, specification, cancellationToken);
    }

    public Task<bool> DeleteSpecificationAsync(string serialCode, CancellationToken cancellationToken = default)
    {
        return _specificationRepository.DeleteAsync(serialCode, cancellationToken);
    }

    public Task<string?> GetSpecValueAsync(string serialCode, string path, CancellationToken cancellationToken = default)
    {
        return _specificationRepository.GetValueByPathAsync(serialCode, path, cancellationToken);
    }

    public Task<IReadOnlyList<string>> ListSerialCodesAsync(CancellationToken cancellationToken = default)
    {
        return _specificationRepository.ListSerialCodesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> QuerySerialCodesAsync<TSpecification>(string query, CancellationToken cancellationToken = default)
        where TSpecification : class, new()
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await ListSerialCodesAsync(cancellationToken);
        }

        try
        {
            var condition = SpecificationQueryCompiler.BuildDbCondition(query);
            return await _specificationRepository.QuerySerialCodesAsync(condition, cancellationToken);
        }
        catch (NotSupportedException)
        {
            var predicate = SpecificationQueryCompiler.Compile<TSpecification>(query);
            var serialCodes = await ListSerialCodesAsync(cancellationToken);
            var evaluations = await Task.WhenAll(serialCodes.Select(async serialCode =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var specification = await LoadSpecificationAsync<TSpecification>(serialCode, cancellationToken);
                return specification is not null && predicate(specification) ? serialCode : null;
            }));
            return evaluations.Where(x => x is not null).Cast<string>().ToList();
        }
    }
    Task<string?> ISpecValueReader.GetValueAsync(string serialCode, string path, CancellationToken cancellationToken)
    {
        return GetSpecValueAsync(serialCode, path, cancellationToken);
    }
}
