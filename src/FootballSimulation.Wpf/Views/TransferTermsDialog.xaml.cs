using System.Globalization;
using System.Windows;
using FootballSimulation.Models;

namespace FootballSimulation.Wpf.Views;

public partial class TransferTermsDialog : Window
{
    private readonly bool _isLoan;

    public decimal Fee { get; private set; }
    public decimal WeeklyWage { get; private set; }
    public int ContractYears => YearsComboBox.SelectedItem is int years ? years : 4;
    public PlayerRole SquadRole => RoleComboBox.SelectedItem is PlayerRole role ? role : PlayerRole.Rotation;
    public int WageShare => WageShareComboBox.SelectedItem is int share ? share : 50;

    public TransferTermsDialog(string playerName, bool isLoan, decimal suggestedFee, decimal suggestedWage, bool isRenewal = false)
    {
        InitializeComponent();
        _isLoan = isLoan;
        HeadingTextBlock.Text = isLoan ? "Request a Loan" : isRenewal ? "Offer Contract Extension" : "Make an Offer";
        PlayerTextBlock.Text = playerName;
        SubmitButtonTextBlock.Text = isLoan ? "Send Request" : isRenewal ? "Offer Extension" : "Submit Offer";
        PermanentFieldsPanel.Visibility = isLoan ? Visibility.Collapsed : Visibility.Visible;
        LoanFieldsPanel.Visibility = isLoan ? Visibility.Visible : Visibility.Collapsed;
        TransferFeeFieldPanel.Visibility = isRenewal ? Visibility.Collapsed : Visibility.Visible;
        FeeTextBox.Text = (suggestedFee / 1_000_000m).ToString("0.#", CultureInfo.InvariantCulture);
        WageTextBox.Text = (suggestedWage / 1_000m).ToString("0", CultureInfo.InvariantCulture);
        ExpectedFeeTextBlock.Text = $"Expected amount: €{suggestedFee / 1_000_000m:0.#}M.";
        ExpectedWageTextBlock.Text = $"Expected minimum: €{suggestedWage / 1_000m:0}k per week.";
        YearsComboBox.ItemsSource = new[] { 1, 2, 3, 4, 5 };
        YearsComboBox.SelectedItem = 4;
        RoleComboBox.ItemsSource = Enum.GetValues<PlayerRole>();
        RoleComboBox.SelectedItem = PlayerRole.Rotation;
        WageShareComboBox.ItemsSource = new[] { 25, 50, 75, 100 };
        WageShareComboBox.SelectedItem = 50;
    }

    private void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        var feeBox = _isLoan ? LoanFeeTextBox : FeeTextBox;
        if (!decimal.TryParse(feeBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var feeMillions) || feeMillions < 0)
        {
            ValidationTextBlock.Text = "Enter a valid non-negative fee in millions.";
            return;
        }

        Fee = feeMillions * 1_000_000m;
        decimal wageThousands = 0;
        if (!_isLoan && (!decimal.TryParse(WageTextBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out wageThousands) || wageThousands <= 0))
        {
            ValidationTextBlock.Text = "Enter a valid weekly wage in thousands.";
            return;
        }

        if (!_isLoan)
        {
            WeeklyWage = wageThousands * 1_000m;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
