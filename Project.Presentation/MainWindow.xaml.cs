using System.Windows;
using Project.Application;
using Project.Domain;

namespace Project.Presentation;

public partial class MainWindow : Window
{
    private readonly ISpecificationService _specificationService;

    public MainWindow(ISpecificationService specificationService)
    {
        _specificationService = specificationService;
        InitializeComponent();
        SpecTypeComboBox.ItemsSource = Enum.GetValues<SpecType>();
        SpecTypeComboBox.SelectedIndex = 0;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshSerialsAsync();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var serialCode = SerialCodeTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(serialCode))
            {
                ShowStatus("Serial Code를 입력해줘.");
                return;
            }

            var specification = ReadSpecificationFromForm();
            await _specificationService.SaveSpecificationAsync(serialCode, specification);

            await RefreshSerialsAsync(serialCode);
            LoadSpecificationToForm(specification);
            ShowStatus($"{serialCode} 사양을 저장했어.");
        }
        catch (Exception ex)
        {
            ShowStatus($"저장 실패: {ex.Message}");
        }
    }

    private async void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (LoadSerialComboBox.SelectedItem is not string serialCode || string.IsNullOrWhiteSpace(serialCode))
            {
                ShowStatus("불러올 Serial을 선택해줘.");
                return;
            }

            var specification = await _specificationService.LoadSpecificationAsync<Specification>(serialCode);
            if (specification is null)
            {
                ShowStatus($"{serialCode} 사양을 찾지 못했어.");
                return;
            }

            SerialCodeTextBox.Text = serialCode;
            LoadSpecificationToForm(specification);
            ShowStatus($"{serialCode} 사양을 불러왔어.");
        }
        catch (Exception ex)
        {
            ShowStatus($"불러오기 실패: {ex.Message}");
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshSerialsAsync();
    }

    private async Task RefreshSerialsAsync(string? selectedSerialCode = null)
    {
        try
        {
            var serialCodes = await _specificationService.ListSerialCodesAsync();
            SerialSelectionComboBox.ItemsSource = serialCodes;
            LoadSerialComboBox.ItemsSource = serialCodes;

            if (!string.IsNullOrWhiteSpace(selectedSerialCode) && serialCodes.Contains(selectedSerialCode))
            {
                SerialSelectionComboBox.SelectedItem = selectedSerialCode;
                LoadSerialComboBox.SelectedItem = selectedSerialCode;
            }
            else
            {
                SerialSelectionComboBox.SelectedIndex = serialCodes.Count > 0 ? 0 : -1;
                LoadSerialComboBox.SelectedIndex = serialCodes.Count > 0 ? 0 : -1;
            }

            if (serialCodes.Count == 0)
            {
                ShowStatus("저장된 Serial이 아직 없어.");
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"목록 조회 실패: {ex.Message}");
        }
    }

    private Specification ReadSpecificationFromForm()
    {
        if (!int.TryParse(InnerSpecATextBox.Text, out var a))
        {
            throw new InvalidOperationException("InnerSpecA.A는 정수여야 해.");
        }

        if (!int.TryParse(InnerSpecBTextBox.Text, out var b))
        {
            throw new InvalidOperationException("InnerSpecA.B는 정수여야 해.");
        }

        if (SpecTypeComboBox.SelectedItem is not SpecType specType)
        {
            throw new InvalidOperationException("Type을 선택해줘.");
        }

        return new Specification
        {
            Name = NameTextBox.Text.Trim(),
            InnerSpecA = new InnerSpecA
            {
                A = a,
                B = b
            },
            InnerSpecB = new InnerSpecB
            {
                C = InnerSpecCTextBox.Text.Trim(),
                D = InnerSpecDTextBox.Text.Trim(),
                InnerSpecC = new InnerSpecC
                {
                    Type = specType
                }
            }
        };
    }

    private void LoadSpecificationToForm(Specification specification)
    {
        NameTextBox.Text = specification.Name;
        InnerSpecATextBox.Text = specification.InnerSpecA.A.ToString();
        InnerSpecBTextBox.Text = specification.InnerSpecA.B.ToString();
        InnerSpecCTextBox.Text = specification.InnerSpecB.C;
        InnerSpecDTextBox.Text = specification.InnerSpecB.D;
        SpecTypeComboBox.SelectedItem = specification.InnerSpecB.InnerSpecC.Type;
    }

    private void ShowStatus(string message)
    {
        StatusTextBlock.Text = message;
    }
}
