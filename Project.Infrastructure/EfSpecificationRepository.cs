using Microsoft.EntityFrameworkCore;
using Project.Domain.Ports;
using Project.Infrastructure.Entities;

namespace Project.Infrastructure;

public sealed class EfSpecificationRepository(SpecDbContext dbContext, SpecificationTreeMapper treeMapper) : ISpecificationRepository
{
    private readonly SpecDbContext _dbContext = dbContext;
    private readonly SpecificationTreeMapper _treeMapper = treeMapper;

    public async Task SaveAsync<TSpecification>(string serialCode, TSpecification specification, CancellationToken cancellationToken = default)
        where TSpecification : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialCode);
        ArgumentNullException.ThrowIfNull(specification);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingRoot = await _dbContext.SpecSerialRoots
            .AsTracking()
            .SingleOrDefaultAsync(x => x.SerialCode == serialCode, cancellationToken);

        if (existingRoot is not null)
        {
            await DeleteSubtreeAsync(existingRoot.RootNodeId, cancellationToken);
            _dbContext.SpecSerialRoots.Remove(existingRoot);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var tree = _treeMapper.ToTree(specification);
        var rootNodeId = await InsertNodeRecursiveAsync(tree, null, cancellationToken);

        _dbContext.SpecSerialRoots.Add(new SpecSerialRootEntity
        {
            SerialCode = serialCode,
            RootNodeId = rootNodeId
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<TSpecification?> LoadAsync<TSpecification>(string serialCode, CancellationToken cancellationToken = default)
        where TSpecification : class, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialCode);

        var rootNodeId = await _dbContext.SpecSerialRoots
            .AsNoTracking()
            .Where(x => x.SerialCode == serialCode)
            .Select(x => (int?)x.RootNodeId)
            .SingleOrDefaultAsync(cancellationToken);

        if (rootNodeId is null)
        {
            return null;
        }

        var root = await BuildTreeAsync(rootNodeId.Value, cancellationToken);
        return _treeMapper.FromTree<TSpecification>(root);
    }

    public async Task<string?> GetValueByPathAsync(string serialCode, string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var rootNodeId = await _dbContext.SpecSerialRoots
            .AsNoTracking()
            .Where(x => x.SerialCode == serialCode)
            .Select(x => (int?)x.RootNodeId)
            .SingleOrDefaultAsync(cancellationToken);

        if (rootNodeId is null)
        {
            return null;
        }

        var root = await BuildTreeAsync(rootNodeId.Value, cancellationToken);
        return _treeMapper.GetValueByPath(root, path);
    }

    public async Task<IReadOnlyList<string>> ListSerialCodesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.SpecSerialRoots
            .AsNoTracking()
            .OrderBy(x => x.SerialCode)
            .Select(x => x.SerialCode)
            .ToListAsync(cancellationToken);
    }

    private async Task DeleteSubtreeAsync(int rootNodeId, CancellationToken cancellationToken)
    {
        var nodeIds = await _dbContext.SpecClosures
            .AsNoTracking()
            .Where(x => x.AncestorId == rootNodeId)
            .Select(x => x.DescendantId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (nodeIds.Count == 0)
        {
            return;
        }

        var closureRows = await _dbContext.SpecClosures
            .Where(x => nodeIds.Contains(x.AncestorId) || nodeIds.Contains(x.DescendantId))
            .ToListAsync(cancellationToken);

        _dbContext.SpecClosures.RemoveRange(closureRows);

        var nodes = await _dbContext.SpecNodes
            .Where(x => nodeIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        _dbContext.SpecNodes.RemoveRange(nodes);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> InsertNodeRecursiveAsync(SpecTreeNode node, int? parentId, CancellationToken cancellationToken)
    {
        var entity = new SpecNodeEntity
        {
            Key = node.Key,
            Value = node.Value,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.SpecNodes.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _dbContext.SpecClosures.Add(new SpecClosureEntity
        {
            AncestorId = entity.Id,
            DescendantId = entity.Id,
            Depth = 0
        });

        if (parentId.HasValue)
        {
            var ancestorRows = await _dbContext.SpecClosures
                .AsNoTracking()
                .Where(x => x.DescendantId == parentId.Value)
                .ToListAsync(cancellationToken);

            foreach (var ancestorRow in ancestorRows)
            {
                _dbContext.SpecClosures.Add(new SpecClosureEntity
                {
                    AncestorId = ancestorRow.AncestorId,
                    DescendantId = entity.Id,
                    Depth = ancestorRow.Depth + 1
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var child in node.Children)
        {
            await InsertNodeRecursiveAsync(child, entity.Id, cancellationToken);
        }

        return entity.Id;
    }

    private async Task<SpecTreeNode> BuildTreeAsync(int rootNodeId, CancellationToken cancellationToken)
    {
        var allNodeIds = await _dbContext.SpecClosures
            .AsNoTracking()
            .Where(x => x.AncestorId == rootNodeId)
            .Select(x => x.DescendantId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var nodeEntities = await _dbContext.SpecNodes
            .AsNoTracking()
            .Where(x => allNodeIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        var nodesById = nodeEntities.ToDictionary(
            x => x.Id,
            x => new SpecTreeNode(x.Key, x.Value));

        var directEdges = await _dbContext.SpecClosures
            .AsNoTracking()
            .Where(x => x.Depth == 1 && allNodeIds.Contains(x.AncestorId) && allNodeIds.Contains(x.DescendantId))
            .ToListAsync(cancellationToken);

        foreach (var edge in directEdges)
        {
            nodesById[edge.AncestorId].Children.Add(nodesById[edge.DescendantId]);
        }

        return nodesById[rootNodeId];
    }
}
