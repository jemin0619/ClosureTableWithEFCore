namespace Project.Domain.Ports;

public interface ISpecValueReader
{
    Task<string?> GetValueAsync(string serialCode, string path, CancellationToken cancellationToken = default);
}
