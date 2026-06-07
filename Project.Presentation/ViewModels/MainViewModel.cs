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
    private Brush _toastBackground = Brushes.SlateBlue;
    private Brush _toastForeground = Brushes.White;
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
            ShowToast($"{serialCode} 사양을 저장했어.", ToastType.Success);
        }
        catch (Exception ex)
        {
            ShowToast($"저장 실패: {ex.Message}", ToastType.Error);
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
            ShowToast($"{serialCode} 사양을 신규 생성했어.", ToastType.Success);
        }
        catch (Exception ex)
        {
            ShowToast($"생성 실패: {ex.Message}", ToastType.Error);
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
            ShowToast($"{serialCode} 사양을 수정했어.", ToastType.Success);
        }
        catch (Exception ex)
        {
            ShowToast($"수정 실패: {ex.Message}", ToastType.Error);
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
                ShowToast($"{serialCode} 사양을 찾지 못했어.", ToastType.Warning);
                return;
            }

            if (SelectedSerialForSave == serialCode)
            {
                SerialCode = string.Empty;
                ResetSpecNodes(new Specification());
            }

            await RefreshSerialsAsync();
            ShowToast($"{serialCode} 사양을 삭제했어.", ToastType.Success);
        }
        catch (Exception ex)
        {
            ShowToast($"삭제 실패: {ex.Message}", ToastType.Error);
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            var serialCode = SelectedSerialForLoad;
            if (string.IsNullOrWhiteSpace(serialCode) || !SerialCodes.Contains(serialCode))
            {
                ShowToast("목록에서 불러올 Serial을 선택해줘.", ToastType.Warning);
                return;
            }

            var spec = await _specificationService.LoadSpecificationAsync<Specification>(serialCode);
            if (spec is null)
            {
                ShowToast($"{serialCode} 사양을 찾지 못했어.", ToastType.Warning);
                return;
            }

            SerialCode = serialCode;
            ResetSpecNodes(spec);
            ShowToast($"{serialCode} 사양을 불러왔어.", ToastType.Info);
        }
        catch (Exception ex)
        {
            ShowToast($"불러오기 실패: {ex.Message}", ToastType.Error);
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
                ShowToast("저장된 Serial이 아직 없어.", ToastType.Info);
        }
        catch (Exception ex)
        {
            ShowToast($"목록 조회 실패: {ex.Message}", ToastType.Error);
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

    private void ShowToast(string message, ToastType toastType)
    {
        ToastMessage = message;

        switch (toastType)
        {
            case ToastType.Success:
                ToastBackground = new SolidColorBrush(Color.FromRgb(34, 139, 34));
                ToastForeground = Brushes.White;
                break;
            case ToastType.Warning:
                ToastBackground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                ToastForeground = Brushes.Black;
                break;
            case ToastType.Error:
                ToastBackground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                ToastForeground = Brushes.White;
                break;
            default:
                ToastBackground = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                ToastForeground = Brushes.White;
                break;
        }

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

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}
