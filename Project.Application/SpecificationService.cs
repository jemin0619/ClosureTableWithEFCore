using Project.Domain.Ports;

namespace Project.Application;

public interface ISpecificationService
{
    Task SaveSpecificationAsync<TSpecification>(string serialCode, TSpecification specification, CancellationToken cancellationToken = default)
        where TSpecification : class;

    Task<TSpecification?> LoadSpecificationAsync<TSpecification>(string serialCode, CancellationToken cancellationToken = default)
        where TSpecification : class, new();

    Task<string?> GetSpecValueAsync(string serialCode, string path, CancellationToken cancellationToken = default);
}

public sealed class SpecificationService(ISpecificationRepository specificationRepository) : ISpecificationService, ISpecValueReader
{
    private readonly ISpecificationRepository _specificationRepository = specificationRepository;

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

    public Task<string?> GetSpecValueAsync(string serialCode, string path, CancellationToken cancellationToken = default)
    {
        return _specificationRepository.GetValueByPathAsync(serialCode, path, cancellationToken);
    }

    public Task<string?> GetValueAsync(string serialCode, string path, CancellationToken cancellationToken = default)
    {
        return _specificationRepository.GetValueByPathAsync(serialCode, path, cancellationToken);
    }
}
