using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Project.Presentation.ViewModels;

/// <summary>
/// Represents a single node in the Specification property tree.
/// Built via reflection — no view changes are needed when Specification gains new properties.
/// </summary>
public sealed class SpecNodeViewModel : INotifyPropertyChanged
{
    private bool _isExpanded = true;
    private string _stringValue = string.Empty;
    private object? _selectedEnumValue;
    private bool? _selectedBooleanValue;

    public string DisplayName { get; }
    public bool IsLeaf { get; }
    public bool IsEnum { get; }
    public bool IsBoolean { get; }
    public IReadOnlyList<object>? EnumValues { get; }
    public IReadOnlyList<bool>? BooleanValues { get; }
    public ObservableCollection<SpecNodeViewModel> Children { get; } = [];

    public bool IsStringLeaf => IsLeaf && !IsEnum && !IsBoolean;
    public bool IsEnumLeaf => IsLeaf && IsEnum;
    public bool IsBooleanLeaf => IsLeaf && IsBoolean;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public string StringValue
    {
        get => _stringValue;
        set => SetField(ref _stringValue, value);
    }

    public object? SelectedEnumValue
    {
        get => _selectedEnumValue;
        set => SetField(ref _selectedEnumValue, value);
    }

    public bool? SelectedBooleanValue
    {
        get => _selectedBooleanValue;
        set => SetField(ref _selectedBooleanValue, value);
    }

    private SpecNodeViewModel(string displayName, Type propertyType)
    {
        DisplayName = displayName;
        var normalizedType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        IsLeaf = IsLeafType(normalizedType);
        IsEnum = normalizedType.IsEnum;
        IsBoolean = normalizedType == typeof(bool);
        if (IsEnum)
            EnumValues = Enum.GetValues(normalizedType).Cast<object>().ToList();
        if (IsBoolean)
            BooleanValues = [true, false];
    }

    private static bool IsLeafType(Type type) =>
        type.IsPrimitive || type == typeof(string) || type.IsEnum || type == typeof(decimal);

    /// <summary>Builds a list of nodes from every public property of <paramref name="obj"/>.</summary>
    public static IReadOnlyList<SpecNodeViewModel> BuildFrom(object obj)
    {
        var nodes = new List<SpecNodeViewModel>();
        foreach (var prop in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var node = new SpecNodeViewModel(prop.Name, prop.PropertyType);
            var value = prop.GetValue(obj);

            if (node.IsLeaf)
            {
                if (node.IsEnum)
                    node.SelectedEnumValue = value;
                else if (node.IsBoolean && value is bool boolValue)
                    node.SelectedBooleanValue = boolValue;
                else
                    node.StringValue = value?.ToString() ?? string.Empty;
            }
            else if (value != null)
            {
                foreach (var child in BuildFrom(value))
                    node.Children.Add(child);
            }

            nodes.Add(node);
        }
        return nodes;
    }

    /// <summary>
    /// Writes the values stored in <paramref name="nodes"/> back onto the matching properties of
    /// <paramref name="target"/> (matched by name, recursively).
    /// </summary>
    public static void ApplyTo(IEnumerable<SpecNodeViewModel> nodes, object target)
    {
        foreach (var node in nodes)
        {
            var prop = target.GetType().GetProperty(node.DisplayName);
            if (prop == null) continue;

            if (node.IsLeaf)
            {
                if (node.IsEnum)
                {
                    prop.SetValue(target, node.SelectedEnumValue);
                }
                else if (node.IsBoolean)
                {
                    if (node.SelectedBooleanValue is null)
                    {
                        throw new InvalidOperationException($"'{node.DisplayName}' 값이 비어있어.");
                    }

                    prop.SetValue(target, node.SelectedBooleanValue.Value);
                }
                else
                {
                    try
                    {
                        var converted = Convert.ChangeType(node.StringValue, prop.PropertyType);
                        prop.SetValue(target, converted);
                    }
                    catch
                    {
                        throw new InvalidOperationException(
                            $"'{node.DisplayName}' 값 '{node.StringValue}'을(를) {prop.PropertyType.Name}으로 변환할 수 없어.");
                    }
                }
            }
            else
            {
                var childObj = Activator.CreateInstance(prop.PropertyType)
                    ?? throw new InvalidOperationException(
                        $"{prop.PropertyType.Name} 인스턴스를 만들 수 없어.");
                ApplyTo(node.Children, childObj);
                prop.SetValue(target, childObj);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
