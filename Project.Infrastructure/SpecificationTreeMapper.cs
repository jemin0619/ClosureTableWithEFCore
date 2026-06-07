using System.Globalization;
using System.Reflection;

namespace Project.Infrastructure;

public sealed class SpecificationTreeMapper
{
    public SpecTreeNode ToTree<TSpecification>(TSpecification specification) where TSpecification : class
    {
        return BuildObjectNode(typeof(TSpecification).Name, specification);
    }

    public TSpecification FromTree<TSpecification>(SpecTreeNode root) where TSpecification : class, new()
    {
        var instance = new TSpecification();
        PopulateObject(instance, root);
        return instance;
    }

    public string? GetValueByPath(SpecTreeNode root, string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = root;
        foreach (var part in parts)
        {
            var next = current.Children.FirstOrDefault(x => string.Equals(x.Key, part, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                return null;
            }

            current = next;
        }

        return current.Value;
    }

    private static SpecTreeNode BuildObjectNode(string key, object value)
    {
        var node = new SpecTreeNode(key, null);
        var type = value.GetType();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead)
            {
                continue;
            }

            var propertyValue = property.GetValue(value);
            if (propertyValue is null)
            {
                continue;
            }

            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (IsLeafType(propertyType))
            {
                node.Children.Add(new SpecTreeNode(property.Name, ConvertToString(propertyValue, propertyType)));
                continue;
            }

            node.Children.Add(BuildObjectNode(property.Name, propertyValue));
        }

        return node;
    }

    private static void PopulateObject(object target, SpecTreeNode node)
    {
        var propertiesByName = target.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(x => x.CanWrite)
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var child in node.Children)
        {
            if (!propertiesByName.TryGetValue(child.Key, out var property))
            {
                continue;
            }

            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (IsLeafType(propertyType))
            {
                if (child.Value is null)
                {
                    continue;
                }

                var parsed = ParseValue(child.Value, propertyType);
                property.SetValue(target, parsed);
                continue;
            }

            var nested = Activator.CreateInstance(propertyType);
            if (nested is null)
            {
                continue;
            }

            PopulateObject(nested, child);
            property.SetValue(target, nested);
        }
    }

    private static bool IsLeafType(Type type)
    {
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan)
            || type == typeof(Guid);
    }

    private static string ConvertToString(object value, Type type)
    {
        if (type == typeof(bool))
        {
            return ((bool)value).ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static object ParseValue(string value, Type type)
    {
        if (type.IsEnum)
        {
            return Enum.Parse(type, value, true);
        }

        if (type == typeof(string))
        {
            return value;
        }

        if (type == typeof(Guid))
        {
            return Guid.Parse(value);
        }

        if (type == typeof(DateTimeOffset))
        {
            return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
        }

        if (type == typeof(TimeSpan))
        {
            return TimeSpan.Parse(value, CultureInfo.InvariantCulture);
        }

        return Convert.ChangeType(value, type, CultureInfo.InvariantCulture)
               ?? throw new InvalidOperationException($"Unable to parse value '{value}' as {type.Name}.");
    }
}

public sealed class SpecTreeNode(string key, string? value)
{
    public string Key { get; } = key;
    public string? Value { get; } = value;
    public List<SpecTreeNode> Children { get; } = [];
}
