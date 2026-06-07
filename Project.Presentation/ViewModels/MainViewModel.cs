using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using Project.Application;
using Project.Domain;
using Project.Presentation.Commands;

namespace Project.Presentation.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ISpecificationService _specificationService;

    private string _serialCode = string.Empty;
    private string? _selectedSerialForSave;
    private string? _selectedSerialForLoad;
    private string _loadSearchText = string.Empty;
    private string _toastMessage = string.Empty;
    private bool _isToastVisible;
    private Brush _toastBackground = new SolidColorBrush(Color.FromRgb(207, 244, 252));
    private Brush _toastForeground = new SolidColorBrush(Color.FromRgb(5, 81, 96));
    private Brush _toastBorderBrush = new SolidColorBrush(Color.FromRgb(158, 234, 249));
    private ObservableCollection<string> _serialCodes = [];
    private ObservableCollection<string> _filteredSerialCodes = [];
    private ObservableCollection<SpecNodeViewModel> _specNodes = [];
    private CancellationTokenSource? _toastCancellationTokenSource;

    public string SerialCode
    {
        get => _serialCode;
        set => SetField(ref _serialCode, value);
    }

    public string? SelectedSerialForSave
    {
        get => _selectedSerialForSave;
        set => SetField(ref _selectedSerialForSave, value);
    }

    public string? SelectedSerialForLoad
    {
        get => _selectedSerialForLoad;
        set => SetField(ref _selectedSerialForLoad, value);
    }

    public string LoadSearchText
    {
        get => _loadSearchText;
        set
        {
            if (!SetField(ref _loadSearchText, value))
            {
                return;
            }

            ApplyLoadFilter();
            SelectedSerialForLoad = null;
        }
    }

    public string ToastMessage
    {
        get => _toastMessage;
        set => SetField(ref _toastMessage, value);
    }

    public bool IsToastVisible
    {
        get => _isToastVisible;
        set => SetField(ref _isToastVisible, value);
    }

    public Brush ToastBackground
    {
        get => _toastBackground;
        set => SetField(ref _toastBackground, value);
    }

    public Brush ToastForeground
    {
        get => _toastForeground;
        set => SetField(ref _toastForeground, value);
    }

    public Brush ToastBorderBrush
    {
        get => _toastBorderBrush;
        set => SetField(ref _toastBorderBrush, value);
    }

    public ObservableCollection<string> SerialCodes
    {
        get => _serialCodes;
        set => SetField(ref _serialCodes, value);
    }

    public ObservableCollection<string> FilteredSerialCodes
    {
        get => _filteredSerialCodes;
        set => SetField(ref _filteredSerialCodes, value);
    }

    public ObservableCollection<SpecNodeViewModel> SpecNodes
    {
        get => _specNodes;
        set => SetField(ref _specNodes, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand CreateCommand { get; }
    public ICommand UpdateCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand LoadCommand { get; }
    public ICommand RefreshCommand { get; }

    public MainViewModel(ISpecificationService specificationService)
    {
        _specificationService = specificationService;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CreateCommand = new AsyncRelayCommand(CreateAsync);
        UpdateCommand = new AsyncRelayCommand(UpdateAsync);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync);
        LoadCommand = new AsyncRelayCommand(LoadAsync);
        RefreshCommand = new AsyncRelayCommand(() => RefreshSerialsAsync());
        ResetSpecNodes(new Specification());
    }

    public Task InitializeAsync() => RefreshSerialsAsync();

    private void ResetSpecNodes(Specification spec)
    {
        SpecNodes = new ObservableCollection<SpecNodeViewModel>(SpecNodeViewModel.BuildFrom(spec));
    }

    private async Task SaveAsync()
    {
        try
        {
            var serialCode = ValidateSerialCode();
            var spec = ReadSpecificationFromNodes();
            await _specificationService.SaveSpecificationAsync(serialCode, spec);
            await RefreshSerialsAsync(serialCode);
            ShowToast($"{serialCode} 사양을 저장했어.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowToast($"저장 실패: {ex.Message}", ToastKind.Error);
        }
    }

    private async Task CreateAsync()
    {
        try
        {
            var serialCode = ValidateSerialCode();
            var spec = ReadSpecificationFromNodes();
            await _specificationService.CreateSpecificationAsync(serialCode, spec);
            await RefreshSerialsAsync(serialCode);
            ShowToast($"{serialCode} 사양을 신규 생성했어.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowToast($"생성 실패: {ex.Message}", ToastKind.Error);
        }
    }

    private async Task UpdateAsync()
    {
        try
        {
            var serialCode = ValidateSerialCode();
            var spec = ReadSpecificationFromNodes();
            await _specificationService.UpdateSpecificationAsync(serialCode, spec);
            await RefreshSerialsAsync(serialCode);
            ShowToast($"{serialCode} 사양을 수정했어.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowToast($"수정 실패: {ex.Message}", ToastKind.Error);
        }
    }

    private async Task DeleteAsync()
    {
        try
        {
            var serialCode = ValidateSerialCode();
            var deleted = await _specificationService.DeleteSpecificationAsync(serialCode);
            if (!deleted)
            {
                ShowToast($"{serialCode} 사양을 찾지 못했어.", ToastKind.Warning);
                return;
            }

            if (SelectedSerialForSave == serialCode)
            {
                SerialCode = string.Empty;
                ResetSpecNodes(new Specification());
            }

            await RefreshSerialsAsync();
            ShowToast($"{serialCode} 사양을 삭제했어.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowToast($"삭제 실패: {ex.Message}", ToastKind.Error);
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            var serialCode = SelectedSerialForLoad;
            if (string.IsNullOrWhiteSpace(serialCode) || !SerialCodes.Contains(serialCode))
            {
                ShowToast("목록에서 불러올 Serial을 선택해줘.", ToastKind.Warning);
                return;
            }

            var spec = await _specificationService.LoadSpecificationAsync<Specification>(serialCode);
            if (spec is null)
            {
                ShowToast($"{serialCode} 사양을 찾지 못했어.", ToastKind.Warning);
                return;
            }

            SerialCode = serialCode;
            ResetSpecNodes(spec);
            ShowToast($"{serialCode} 사양을 불러왔어.", ToastKind.Info);
        }
        catch (Exception ex)
        {
            ShowToast($"불러오기 실패: {ex.Message}", ToastKind.Error);
        }
    }

    private async Task RefreshSerialsAsync(string? selectedSerialCode = null)
    {
        try
        {
            var codes = await _specificationService.ListSerialCodesAsync();
            var codeList = codes.ToList();
            SerialCodes = new ObservableCollection<string>(codeList);

            var target = !string.IsNullOrWhiteSpace(selectedSerialCode) && codeList.Contains(selectedSerialCode)
                ? selectedSerialCode
                : codeList.Count > 0 ? codeList[0] : null;

            SelectedSerialForSave = target;
            LoadSearchText = string.Empty;
            ApplyLoadFilter();
            SelectedSerialForLoad = target;

            if (codeList.Count == 0)
                ShowToast("저장된 Serial이 아직 없어.", ToastKind.Info);
        }
        catch (Exception ex)
        {
            ShowToast($"목록 조회 실패: {ex.Message}", ToastKind.Error);
        }
    }

    private string ValidateSerialCode()
    {
        var serialCode = SerialCode.Trim();
        if (string.IsNullOrWhiteSpace(serialCode))
        {
            throw new InvalidOperationException("Serial Code를 입력해줘.");
        }

        return serialCode;
    }

    private Specification ReadSpecificationFromNodes()
    {
        var spec = new Specification();
        SpecNodeViewModel.ApplyTo(SpecNodes, spec);
        return spec;
    }

    private void ApplyLoadFilter()
    {
        var keyword = LoadSearchText.Trim();
        var filtered = string.IsNullOrWhiteSpace(keyword)
            ? SerialCodes.ToList()
            : SerialCodes
                .Where(x => x.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();

        FilteredSerialCodes = new ObservableCollection<string>(filtered);
    }

    private void ShowToast(string message, ToastKind toastType)
    {
        ToastMessage = message;
        ApplyToastStyle(toastType);

        IsToastVisible = true;
        _toastCancellationTokenSource?.Cancel();
        _toastCancellationTokenSource?.Dispose();
        _toastCancellationTokenSource = new CancellationTokenSource();
        _ = HideToastAsync(_toastCancellationTokenSource.Token);
    }

    private async Task HideToastAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            IsToastVisible = false;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyToastStyle(ToastKind toastType)
    {
        var style = toastType switch
        {
            ToastKind.Success => new ToastStyle(
                new SolidColorBrush(Color.FromRgb(209, 231, 221)),   // #d1e7dd
                new SolidColorBrush(Color.FromRgb(15, 81, 50)),      // #0f5132
                new SolidColorBrush(Color.FromRgb(163, 207, 187))),  // #a3cfbb
            ToastKind.Warning => new ToastStyle(
                new SolidColorBrush(Color.FromRgb(255, 243, 205)),   // #fff3cd
                new SolidColorBrush(Color.FromRgb(102, 77, 3)),      // #664d03
                new SolidColorBrush(Color.FromRgb(255, 230, 156))),  // #ffe69c
            ToastKind.Error => new ToastStyle(
                new SolidColorBrush(Color.FromRgb(248, 215, 218)),   // #f8d7da
                new SolidColorBrush(Color.FromRgb(132, 32, 41)),     // #842029
                new SolidColorBrush(Color.FromRgb(241, 174, 181))),  // #f1aeb5
            _ => new ToastStyle(
                new SolidColorBrush(Color.FromRgb(207, 244, 252)),   // #cff4fc
                new SolidColorBrush(Color.FromRgb(5, 81, 96)),       // #055160
                new SolidColorBrush(Color.FromRgb(158, 234, 249)))   // #9eeaf9
        };

        ToastBackground = style.Background;
        ToastForeground = style.Foreground;
        ToastBorderBrush = style.BorderBrush;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    private sealed record ToastStyle(Brush Background, Brush Foreground, Brush BorderBrush);

    private enum ToastKind
    {
        Info,
        Success,
        Warning,
        Error
    }
}
