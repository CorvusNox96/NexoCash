using System.Windows;
using NexoCash.Desktop.Shared.Windows;
using NexoCash.Infrastructure.Persistence;

namespace NexoCash.Desktop.Features.Services;

public partial class ServiceBanknotesWindow : Window
{
    private readonly DateOnly _date;
    private readonly List<ConsolidationAllocation> _allocations;
    private readonly Dictionary<string, decimal> _expectedCash;
    private readonly string _report;
    private BanknoteCountResult? _count;

    public ServiceBanknotesWindow(DateOnly date, List<ConsolidationAllocation> allocations, string report)
    {
        InitializeComponent();
        _date = date;
        _allocations = allocations;
        _report = report;
        _expectedCash = allocations.Where(item => item.Payment == "Cash" && !item.IsDirectWithdrawal)
            .GroupBy(item => item.Currency)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));
        ExpectedText.Text = _expectedCash.Count == 0 ? "Sin efectivo para caja" :
            string.Join(" · ", _expectedCash.Select(item => $"{item.Value:N2} {item.Key}"));
        CashBreakdownText.Text = "Solo se cuentan los billetes destinados a cuentas de efectivo. Los retiros directos quedan fuera de la caja.";
        ReportText.Text = report;
        CountButton.IsEnabled = _expectedCash.Count > 0;
        FinishButton.IsEnabled = _expectedCash.Count == 0;
        SkipButton.IsEnabled = _expectedCash.Count > 0;
    }

    private void Count_Click(object sender, RoutedEventArgs e)
    {
        var window = new BanknoteCountWindow(allowedCurrencies: _expectedCash.Keys.ToList(),
            allowMissingRates: true) { Owner = this };
        if (window.ShowDialog() != true || window.Result is null) return;
        _count = window.Result;
        var counted = _count.Counts.ToDictionary(pair => pair.Key,
            pair => pair.Value.Sum(note => note.Key * note.Value));
        CountSummaryText.Text = _count.Summary;
        FinishButton.IsEnabled = _expectedCash.Count == counted.Count &&
            _expectedCash.All(pair => counted.GetValueOrDefault(pair.Key) == pair.Value);
        ErrorText.Text = FinishButton.IsEnabled ? "" :
            "El conteo debe coincidir con el efectivo destinado a caja en cada moneda.";
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (_count is null && _expectedCash.Count > 0) return;
        Save(_count is null ? [] : _count.Counts.Select(pair =>
            new ConsolidationCashCount(pair.Key, pair.Value)).ToList());
    }

    private void Skip_Click(object sender, RoutedEventArgs e) => Save([]);

    private void Save(IReadOnlyList<ConsolidationCashCount> counts)
    {
        try
        {
            new ServiceConsolidationRepository().Consolidate(_date, _allocations, counts);
            MessageBox.Show(this, _report,
                "Consolidación completada", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
