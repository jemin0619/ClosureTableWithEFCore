using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
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
    private string _statusMessage = string.Empty;
    private ObservableCollection<string> _serialCodes = [];
    private ObservableCollection<SpecNodeViewModel> _specNodes = [];

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

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public ObservableCollection<string> SerialCodes
    {
        get => _serialCodes;
        set => SetField(ref _serialCodes, value);
    }

    public ObservableCollection<SpecNodeViewModel> SpecNodes
    {
        get => _specNodes;
        set => SetField(ref _specNodes, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand LoadCommand { get; }
    public ICommand RefreshCommand { get; }

    public MainViewModel(ISpecificationService specificationService)
    {
        _specificationService = specificationService;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
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
            var serialCode = SerialCode.Trim();
            if (string.IsNullOrWhiteSpace(serialCode))
            {
                StatusMessage = "Serial Code를 입력해줘.";
                return;
            }

            var spec = new Specification();
            SpecNodeViewModel.ApplyTo(SpecNodes, spec);

            await _specificationService.SaveSpecificationAsync(serialCode, spec);
            await RefreshSerialsAsync(serialCode);
            StatusMessage = $"{serialCode} 사양을 저장했어.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"저장 실패: {ex.Message}";
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            var serialCode = SelectedSerialForLoad;
            if (string.IsNullOrWhiteSpace(serialCode))
            {
                StatusMessage = "불러올 Serial을 선택해줘.";
                return;
            }

            var spec = await _specificationService.LoadSpecificationAsync<Specification>(serialCode);
            if (spec is null)
            {
                StatusMessage = $"{serialCode} 사양을 찾지 못했어.";
                return;
            }

            SerialCode = serialCode;
            ResetSpecNodes(spec);
            StatusMessage = $"{serialCode} 사양을 불러왔어.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"불러오기 실패: {ex.Message}";
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
            SelectedSerialForLoad = target;

            if (codeList.Count == 0)
                StatusMessage = "저장된 Serial이 아직 없어.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"목록 조회 실패: {ex.Message}";
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
