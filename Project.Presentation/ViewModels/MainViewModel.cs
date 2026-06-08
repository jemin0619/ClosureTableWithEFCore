using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Media;
using Project.Application;
using Project.Domain;
using Project.Presentation.Commands;

namespace Project.Presentation.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ISpecificationService _specificationService;

    private string _searchText = string.Empty;
    private string _detailQueryText = string.Empty;
    private string? _selectedCandidate;
    private bool _isEditMode;
    private bool _isDetailSearchVisible;
    private bool _isQuerySuggestionOpen;
    private string? _editingSerialCode;
    private string _toastMessage = string.Empty;
    private bool _isToastVisible;
    private Brush _toastBackground = new SolidColorBrush(Color.FromRgb(207, 244, 252));
    private Brush _toastForeground = new SolidColorBrush(Color.FromRgb(5, 81, 96));
    private Brush _toastBorderBrush = new SolidColorBrush(Color.FromRgb(158, 234, 249));
    private ObservableCollection<string> _serialCodes = [];
    private ObservableCollection<string> _candidateSerialCodes = [];
    private ObservableCollection<string> _queryPathSuggestions = [];
    private ObservableCollection<SpecNodeViewModel> _specNodes = [];
    private IReadOnlyList<string>? _queryFilteredSerialCodes;
    private readonly IReadOnlyList<string> _queryablePaths;
    private CancellationTokenSource? _toastCancellationTokenSource;
    private static readonly Regex QueryFragmentRegex = new(@"[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.Compiled);

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value))
            {
                return;
            }

            ApplyCandidateFilter();
            OnPropertyChanged(nameof(TargetSerialLabel));
        }
    }

    public string DetailQueryText
    {
        get => _detailQueryText;
        set
        {
            if (!SetField(ref _detailQueryText, value))
            {
                return;
            }

            UpdateQuerySuggestions();
        }
    }

    public string? SelectedCandidate
    {
        get => _selectedCandidate;
        set => SetField(ref _selectedCandidate, value);
    }

    public bool IsEditMode
    {
        get => _isEditMode;
        private set
        {
            if (!SetField(ref _isEditMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(PrimaryActionButtonText));
            OnPropertyChanged(nameof(TargetSerialLabel));
        }
    }

    public bool IsDetailSearchVisible
    {
        get => _isDetailSearchVisible;
        set => SetField(ref _isDetailSearchVisible, value);
    }

    public bool IsQuerySuggestionOpen
    {
        get => _isQuerySuggestionOpen;
        set => SetField(ref _isQuerySuggestionOpen, value);
    }

    public string PrimaryActionButtonText => IsEditMode ? "Update" : "Create";

    public string TargetSerialLabel => IsEditMode
        ? _editingSerialCode ?? string.Empty
        : SearchText.Trim();

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

    public ObservableCollection<string> CandidateSerialCodes
    {
        get => _candidateSerialCodes;
        set => SetField(ref _candidateSerialCodes, value);
    }

    public ObservableCollection<SpecNodeViewModel> SpecNodes
    {
        get => _specNodes;
        set => SetField(ref _specNodes, value);
    }

    public ObservableCollection<string> QueryPathSuggestions
    {
        get => _queryPathSuggestions;
        set => SetField(ref _queryPathSuggestions, value);
    }

    public ICommand PrimaryActionCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ToggleDetailSearchCommand { get; }
    public ICommand SearchByDetailQueryCommand { get; }
    public ICommand StartEditCommand { get; }
    public ICommand DeleteCandidateCommand { get; }
    public ICommand CopyCandidateCommand { get; }

    public MainViewModel(ISpecificationService specificationService)
    {
        _specificationService = specificationService;
        _queryablePaths = _specificationService.GetQueryablePaths<Specification>();
        PrimaryActionCommand = new AsyncRelayCommand(PrimaryActionAsync);
        ClearCommand = new AsyncRelayCommand(ClearAsync);
        ToggleDetailSearchCommand = new AsyncRelayCommand(ToggleDetailSearchAsync);
        SearchByDetailQueryCommand = new AsyncRelayCommand(SearchByDetailQueryAsync);
        StartEditCommand = new AsyncRelayCommand<string>(StartEditAsync);
        DeleteCandidateCommand = new AsyncRelayCommand<string>(DeleteCandidateAsync);
        CopyCandidateCommand = new AsyncRelayCommand<string>(CopyCandidateAsync);
        ResetSpecNodes(new Specification());
    }

    public Task InitializeAsync() => RefreshSerialsAsync();

    private void ResetSpecNodes(Specification spec)
    {
        SpecNodes = new ObservableCollection<SpecNodeViewModel>(SpecNodeViewModel.BuildFrom(spec));
    }

    private async Task PrimaryActionAsync()
    {
        try
        {
            if (IsEditMode)
            {
                await UpdateAsync();
                return;
            }

            await CreateAsync();
        }
        catch (Exception ex)
        {
            ShowToast($"저장 실패: {ex.Message}", ToastKind.Error);
        }
    }

    private async Task CreateAsync()
    {
        var serialCode = ValidateSerialCode(SearchText);
        var spec = ReadSpecificationFromNodes();
        await _specificationService.CreateSpecificationAsync(serialCode, spec);
        await RefreshSerialsAsync(serialCode);
        SearchText = serialCode;
        ShowToast($"{serialCode} 사양을 신규 생성했어.", ToastKind.Success);
    }

    private async Task UpdateAsync()
    {
        var serialCode = _editingSerialCode;
        if (string.IsNullOrWhiteSpace(serialCode))
        {
            throw new InvalidOperationException("수정할 식별자가 없어.");
        }

        var spec = ReadSpecificationFromNodes();
        await _specificationService.UpdateSpecificationAsync(serialCode, spec);
        await RefreshSerialsAsync(serialCode);
        ShowToast($"{serialCode} 사양을 수정했어.", ToastKind.Success);
    }

    private async Task StartEditAsync(string? serialCode)
    {
        try
        {
            serialCode = string.IsNullOrWhiteSpace(serialCode) ? SelectedCandidate : serialCode;
            if (string.IsNullOrWhiteSpace(serialCode) || !SerialCodes.Contains(serialCode))
            {
                ShowToast("수정할 식별자를 선택해줘.", ToastKind.Warning);
                return;
            }

            var spec = await _specificationService.LoadSpecificationAsync<Specification>(serialCode);
            if (spec is null)
            {
                ShowToast($"{serialCode} 사양을 찾지 못했어.", ToastKind.Warning);
                return;
            }

            _editingSerialCode = serialCode;
            IsEditMode = true;
            SelectedCandidate = serialCode;
            SearchText = serialCode;
            ResetSpecNodes(spec);
            ShowToast($"{serialCode} 사양을 편집 모드로 불러왔어.", ToastKind.Info);
        }
        catch (Exception ex)
        {
            ShowToast($"불러오기 실패: {ex.Message}", ToastKind.Error);
        }
    }

    private async Task DeleteCandidateAsync(string? serialCode)
    {
        try
        {
            serialCode = string.IsNullOrWhiteSpace(serialCode) ? SelectedCandidate : serialCode;
            if (string.IsNullOrWhiteSpace(serialCode))
            {
                ShowToast("삭제할 식별자를 선택해줘.", ToastKind.Warning);
                return;
            }

            var deleted = await _specificationService.DeleteSpecificationAsync(serialCode);
            if (!deleted)
            {
                ShowToast($"{serialCode} 사양을 찾지 못했어.", ToastKind.Warning);
                return;
            }

            if (string.Equals(_editingSerialCode, serialCode, StringComparison.OrdinalIgnoreCase))
            {
                ExitEditMode();
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

    private async Task CopyCandidateAsync(string? serialCode)
    {
        try
        {
            serialCode = string.IsNullOrWhiteSpace(serialCode) ? SelectedCandidate : serialCode;
            if (string.IsNullOrWhiteSpace(serialCode) || !SerialCodes.Contains(serialCode))
            {
                ShowToast("복사할 식별자를 선택해줘.", ToastKind.Warning);
                return;
            }

            var spec = await _specificationService.LoadSpecificationAsync<Specification>(serialCode);
            if (spec is null)
            {
                ShowToast($"{serialCode} 사양을 찾지 못했어.", ToastKind.Warning);
                return;
            }

            ExitEditMode();
            SelectedCandidate = serialCode;
            SearchText = string.Empty;
            ResetSpecNodes(spec);
            ShowToast($"{serialCode} 사양을 복사해서 Create 모드로 가져왔어.", ToastKind.Info);
        }
        catch (Exception ex)
        {
            ShowToast($"복사 실패: {ex.Message}", ToastKind.Error);
        }
    }

    private Task ClearAsync()
    {
        SearchText = string.Empty;
        DetailQueryText = string.Empty;
        SelectedCandidate = null;
        _queryFilteredSerialCodes = null;
        IsQuerySuggestionOpen = false;
        ExitEditMode();
        ApplyCandidateFilter();
        ResetSpecNodes(new Specification());
        ShowToast("입력값을 모두 초기화했어.", ToastKind.Info);
        return Task.CompletedTask;
    }

    private Task ToggleDetailSearchAsync()
    {
        IsDetailSearchVisible = !IsDetailSearchVisible;

        if (!IsDetailSearchVisible)
        {
            DetailQueryText = string.Empty;
            _queryFilteredSerialCodes = null;
            IsQuerySuggestionOpen = false;
            ApplyCandidateFilter();
        }
        else
        {
            UpdateQuerySuggestions();
        }

        return Task.CompletedTask;
    }

    private async Task SearchByDetailQueryAsync()
    {
        try
        {
            var query = DetailQueryText.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                _queryFilteredSerialCodes = null;
                ApplyCandidateFilter();
                ShowToast("상세 쿼리를 입력해줘.", ToastKind.Warning);
                return;
            }

            _queryFilteredSerialCodes = await _specificationService.QuerySerialCodesAsync<Specification>(query);
            ApplyCandidateFilter();
            ShowToast($"상세 쿼리 결과 {CandidateSerialCodes.Count}건을 찾았어.", ToastKind.Info);
        }
        catch (Exception ex)
        {
            ShowToast($"상세 검색 실패: {ex.Message}", ToastKind.Error);
        }
    }

    private void ExitEditMode()
    {
        _editingSerialCode = null;
        IsEditMode = false;
        OnPropertyChanged(nameof(TargetSerialLabel));
    }

    private async Task RefreshSerialsAsync(string? selectedSerialCode = null)
    {
        try
        {
            var codes = await _specificationService.ListSerialCodesAsync();
            var codeList = codes.ToList();

            SerialCodes = new ObservableCollection<string>(codeList);
            ApplyCandidateFilter();

            SelectedCandidate = !string.IsNullOrWhiteSpace(selectedSerialCode) && codeList.Contains(selectedSerialCode)
                ? selectedSerialCode
                : SelectedCandidate is not null && codeList.Contains(SelectedCandidate)
                    ? SelectedCandidate
                    : null;

            if (codeList.Count == 0)
            {
                ShowToast("저장된 식별자가 아직 없어.", ToastKind.Info);
            }
        }
        catch (Exception ex)
        {
            ShowToast($"목록 조회 실패: {ex.Message}", ToastKind.Error);
        }
    }

    private static string ValidateSerialCode(string input)
    {
        var serialCode = input.Trim();
        if (string.IsNullOrWhiteSpace(serialCode))
        {
            throw new InvalidOperationException("식별자를 입력해줘.");
        }

        return serialCode;
    }

    private Specification ReadSpecificationFromNodes()
    {
        var spec = new Specification();
        SpecNodeViewModel.ApplyTo(SpecNodes, spec);
        return spec;
    }

    private void ApplyCandidateFilter()
    {
        var keyword = SearchText.Trim();
        var source = _queryFilteredSerialCodes ?? SerialCodes;
        var filtered = string.IsNullOrWhiteSpace(keyword)
            ? source.ToList()
            : source
                .Where(x => x.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();

        CandidateSerialCodes = new ObservableCollection<string>(filtered);
    }

    private void UpdateQuerySuggestions()
    {
        if (!IsDetailSearchVisible)
        {
            QueryPathSuggestions = [];
            IsQuerySuggestionOpen = false;
            return;
        }

        var fragment = TryGetCurrentQueryFragment(DetailQueryText);
        if (string.IsNullOrWhiteSpace(fragment))
        {
            QueryPathSuggestions = [];
            IsQuerySuggestionOpen = false;
            return;
        }

        var suggestions = _queryablePaths
            .Where(x => x.StartsWith(fragment, StringComparison.OrdinalIgnoreCase)
                        || x.Contains($".{fragment}", StringComparison.OrdinalIgnoreCase))
            .Take(12)
            .ToList();

        QueryPathSuggestions = new ObservableCollection<string>(suggestions);
        IsQuerySuggestionOpen = suggestions.Count > 0;
    }

    private static string? TryGetCurrentQueryFragment(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = QueryFragmentRegex.Match(text);
        if (!match.Success)
        {
            return null;
        }

        return match.Value;
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
                new SolidColorBrush(Color.FromRgb(209, 231, 221)),
                new SolidColorBrush(Color.FromRgb(15, 81, 50)),
                new SolidColorBrush(Color.FromRgb(163, 207, 187))),
            ToastKind.Warning => new ToastStyle(
                new SolidColorBrush(Color.FromRgb(255, 243, 205)),
                new SolidColorBrush(Color.FromRgb(102, 77, 3)),
                new SolidColorBrush(Color.FromRgb(255, 230, 156))),
            ToastKind.Error => new ToastStyle(
                new SolidColorBrush(Color.FromRgb(248, 215, 218)),
                new SolidColorBrush(Color.FromRgb(132, 32, 41)),
                new SolidColorBrush(Color.FromRgb(241, 174, 181))),
            _ => new ToastStyle(
                new SolidColorBrush(Color.FromRgb(207, 244, 252)),
                new SolidColorBrush(Color.FromRgb(5, 81, 96)),
                new SolidColorBrush(Color.FromRgb(158, 234, 249)))
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

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
