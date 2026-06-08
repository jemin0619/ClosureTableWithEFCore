using Microsoft.EntityFrameworkCore;
using Project.Domain;
using Project.Domain.Ports;
using Project.Infrastructure.Entities;
using System.Globalization;

namespace Project.Infrastructure;

public sealed class EfSpecificationRepository(SpecDbContext dbContext, SpecificationTreeMapper treeMapper) : ISpecificationRepository
{
    private readonly SpecDbContext _dbContext = dbContext;
    private readonly SpecificationTreeMapper _treeMapper = treeMapper;

    public async Task CreateAsync<TSpecification>(string serialCode, TSpecification specification, CancellationToken cancellationToken = default)
        where TSpecification : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialCode);
        ArgumentNullException.ThrowIfNull(specification);

        var exists = await _dbContext.SpecSerialRoots
            .AsNoTracking()
            .AnyAsync(x => x.SerialCode == serialCode, cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException($"{serialCode} 사양은 이미 존재해.");
        }

        await InsertNewTreeForSerialAsync(serialCode, specification, cancellationToken);
    }

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
            var oldRootNodeId = existingRoot.RootNodeId;
            _dbContext.SpecSerialRoots.Remove(existingRoot);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await DeleteSubtreeAsync(oldRootNodeId, cancellationToken);
        }

        await InsertNewTreeForSerialAsync(serialCode, specification, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateAsync<TSpecification>(string serialCode, TSpecification specification, CancellationToken cancellationToken = default)
        where TSpecification : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialCode);
        ArgumentNullException.ThrowIfNull(specification);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingRoot = await _dbContext.SpecSerialRoots
            .AsTracking()
            .SingleOrDefaultAsync(x => x.SerialCode == serialCode, cancellationToken);

        if (existingRoot is null)
        {
            throw new InvalidOperationException($"{serialCode} 사양이 없어서 수정할 수 없어.");
        }

        var oldRootNodeId = existingRoot.RootNodeId;
        _dbContext.SpecSerialRoots.Remove(existingRoot);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await DeleteSubtreeAsync(oldRootNodeId, cancellationToken);
        await InsertNewTreeForSerialAsync(serialCode, specification, cancellationToken);
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

    public async Task<IReadOnlyList<string>> QuerySerialCodesAsync(SpecQueryCondition condition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var codes = await ResolveConditionAsync(condition, cancellationToken);
        return codes.OrderBy(x => x).ToList();
    }

    private async Task<HashSet<string>> ResolveConditionAsync(SpecQueryCondition condition, CancellationToken ct)
    {
        return condition switch
        {
            LeafSpecQueryCondition leaf => await ResolveLeafAsync(leaf, ct),
            AndSpecQueryCondition and => await ResolveAndAsync(and, ct),
            OrSpecQueryCondition or => await ResolveOrAsync(or, ct),
            NotSpecQueryCondition not => await ResolveNotAsync(not, ct),
            _ => throw new InvalidOperationException("지원하지 않는 조건 타입이야.")
        };
    }

    private async Task<HashSet<string>> ResolveLeafAsync(LeafSpecQueryCondition leaf, CancellationToken ct)
    {
        var leafKey = leaf.PathSegments[^1];
        var ancestorKeys = leaf.PathSegments.Length > 1 ? leaf.PathSegments[..^1] : [];

        var matchingIds = await FetchMatchingNodeIdsAsync(leafKey, leaf.Operator, leaf.Value, ct);
        if (matchingIds.Count == 0)
        {
            return [];
        }

        matchingIds = await FilterByAncestorChainAsync(matchingIds, ancestorKeys, ct);
        if (matchingIds.Count == 0)
        {
            return [];
        }

        var serialCodes = await _dbContext.SpecSerialRoots
            .AsNoTracking()
            .Where(sr => _dbContext.SpecClosures.Any(sc =>
                sc.AncestorId == sr.RootNodeId && matchingIds.Contains(sc.DescendantId)))
            .Select(sr => sr.SerialCode)
            .ToListAsync(ct);

        return [.. serialCodes];
    }

    private async Task<List<int>> FetchMatchingNodeIdsAsync(string key, SpecQueryOperator op, string value, CancellationToken ct)
    {
        if (op == SpecQueryOperator.Equal)
        {
            return await _dbContext.SpecNodes
                .AsNoTracking()
                .Where(n => n.Key == key && n.Value == value)
                .Select(n => n.Id)
                .ToListAsync(ct);
        }

        if (op == SpecQueryOperator.Like)
        {
            return await _dbContext.SpecNodes
                .AsNoTracking()
                .Where(n => n.Key == key && n.Value != null && n.Value.Contains(value))
                .Select(n => n.Id)
                .ToListAsync(ct);
        }

        // >, <, >=, <= : key로 후보를 가져온 뒤 메모리에서 값 비교 (숫자 정확도 보장)
        var candidates = await _dbContext.SpecNodes
            .AsNoTracking()
            .Where(n => n.Key == key && n.Value != null)
            .Select(n => new { n.Id, n.Value })
            .ToListAsync(ct);

        return candidates
            .Where(x => CompareNodeValues(x.Value!, value, op))
            .Select(x => x.Id)
            .ToList();
    }

    private static bool CompareNodeValues(string nodeValue, string queryValue, SpecQueryOperator op)
    {
        if (decimal.TryParse(nodeValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var nodeDecimal)
            && decimal.TryParse(queryValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var queryDecimal))
        {
            return op switch
            {
                SpecQueryOperator.GreaterThan => nodeDecimal > queryDecimal,
                SpecQueryOperator.GreaterThanOrEqual => nodeDecimal >= queryDecimal,
                SpecQueryOperator.LessThan => nodeDecimal < queryDecimal,
                SpecQueryOperator.LessThanOrEqual => nodeDecimal <= queryDecimal,
                _ => false
            };
        }

        var cmp = string.Compare(nodeValue, queryValue, StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            SpecQueryOperator.GreaterThan => cmp > 0,
            SpecQueryOperator.GreaterThanOrEqual => cmp >= 0,
            SpecQueryOperator.LessThan => cmp < 0,
            SpecQueryOperator.LessThanOrEqual => cmp <= 0,
            _ => false
        };
    }

    private async Task<List<int>> FilterByAncestorChainAsync(List<int> nodeIds, string[] ancestorKeys, CancellationToken ct)
    {
        // ancestorKeys[0] 이 루트에 가장 가깝고, ancestorKeys[^1] 이 리프에 가장 가까운 부모
        // ancestorKeys[i] 에서 리프까지의 depth = ancestorKeys.Length - i
        for (int i = 0; i < ancestorKeys.Length; i++)
        {
            if (nodeIds.Count == 0)
            {
                break;
            }

            int depth = ancestorKeys.Length - i;
            string ancestorKey = ancestorKeys[i];
            var currentIds = nodeIds;

            nodeIds = await _dbContext.SpecClosures
                .AsNoTracking()
                .Where(sc => currentIds.Contains(sc.DescendantId)
                             && sc.Depth == depth
                             && _dbContext.SpecNodes.Any(an => an.Id == sc.AncestorId && an.Key == ancestorKey))
                .Select(sc => sc.DescendantId)
                .Distinct()
                .ToListAsync(ct);
        }

        return nodeIds;
    }

    private async Task<HashSet<string>> ResolveAndAsync(AndSpecQueryCondition and, CancellationToken ct)
    {
        var left = await ResolveConditionAsync(and.Left, ct);
        if (left.Count == 0)
        {
            return left;
        }

        var right = await ResolveConditionAsync(and.Right, ct);
        left.IntersectWith(right);
        return left;
    }

    private async Task<HashSet<string>> ResolveOrAsync(OrSpecQueryCondition or, CancellationToken ct)
    {
        var left = await ResolveConditionAsync(or.Left, ct);
        var right = await ResolveConditionAsync(or.Right, ct);
        left.UnionWith(right);
        return left;
    }

    private async Task<HashSet<string>> ResolveNotAsync(NotSpecQueryCondition not, CancellationToken ct)
    {
        var inner = await ResolveConditionAsync(not.Inner, ct);
        var all = await _dbContext.SpecSerialRoots
            .AsNoTracking()
            .Select(sr => sr.SerialCode)
            .ToListAsync(ct);
        var result = new HashSet<string>(all, StringComparer.Ordinal);
        result.ExceptWith(inner);
        return result;
    }

    public async Task<bool> DeleteAsync(string serialCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialCode);

        var existingRoot = await _dbContext.SpecSerialRoots
            .AsTracking()
            .SingleOrDefaultAsync(x => x.SerialCode == serialCode, cancellationToken);

        if (existingRoot is null)
        {
            return false;
        }

        var oldRootNodeId = existingRoot.RootNodeId;
        _dbContext.SpecSerialRoots.Remove(existingRoot);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await DeleteSubtreeAsync(oldRootNodeId, cancellationToken);
        return true;
    }

    private async Task InsertNewTreeForSerialAsync<TSpecification>(string serialCode, TSpecification specification, CancellationToken cancellationToken)
        where TSpecification : class
    {
        var tree = _treeMapper.ToTree(specification);
        var rootNodeId = await InsertNodeRecursiveAsync(tree, null, cancellationToken);

        _dbContext.SpecSerialRoots.Add(new SpecSerialRootEntity
        {
            SerialCode = serialCode,
            RootNodeId = rootNodeId
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
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
