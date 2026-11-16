using System.Windows.Controls;
using System.Windows.Media;
using NexoCash.Domain.Entities;
using NexoCash.Infrastructure.Persistence;

namespace NexoCash.Desktop.Features.Overview;

public sealed record CurrencyMetric(string Currency, decimal Total, decimal Cash, double Share)
{
    public string TotalText => $"{Total:N0} {Currency}";
    public string CashText => $"Efectivo: {Cash:N0} {Currency}";
    public string ShareText => $"{Share:N0} % de las cuentas en {Currency}";
}

public sealed record WeeklyChartItem(DateOnly Week, decimal Amount, int Services, double BarHeight, string Currency)
{
    public string WeekLabel => Week.ToString("dd/MM");
    public string AmountLabel => Amount >= 1000 ? $"{Amount / 1000:N0} mil" : Amount.ToString("N0");
    public string Tooltip => $"Semana del {Week:dd/MM/yyyy}: {Amount:N0} {Currency} · {Services} servicios";
}

public sealed record WeeklyFlowChartItem(DateOnly Week, decimal Income, decimal Withdrawals,
    double IncomeHeight, double WithdrawalHeight, string Currency)
{
    public string WeekLabel => Week.ToString("dd/MM");
    public string IncomeTooltip => $"Ingresos: {Income:N0} {Currency}";
    public string WithdrawalTooltip => $"Retiros: {Withdrawals:N0} {Currency}";
}

public partial class OverviewView : UserControl
{
    private readonly AccountRepository _accounts = new();
    private readonly ServiceRepository _services = new();
    private readonly AgencyRepository _agencies = new();
    private readonly OverviewStatisticsRepository _statistics = new();
    private readonly CurrencyExchangeRepository _exchange = new();
    private readonly WorkdayRepository _workdays = new();
    private IReadOnlyList<WeeklyServiceIncome> _weeklyIncome = [];
    private IReadOnlyList<WeeklyAccountFlow> _personalFlow = [];
    private bool _loading;

    public OverviewView() { InitializeComponent(); Refresh(); }

    public void Refresh()
    {
        var accounts = _accounts.ListActive();
        var metrics = accounts.SelectMany(account => account.Balances.Select(balance =>
                new { balance.Currency, balance.Amount, IsCash = account.Account.Type == AccountType.Cash }))
            .GroupBy(item => item.Currency)
            .Select(group =>
            {
                var total = group.Sum(item => item.Amount);
                var cash = group.Where(item => item.IsCash).Sum(item => item.Amount);
                var share = total > 0 ? (double)(cash / total * 100m) : 0d;
                return new CurrencyMetric(group.Key, total, cash, Math.Clamp(share, 0, 100));
            })
            .OrderBy(item => item.Currency == "CUP" ? 0 : 1).ThenBy(item => item.Currency)
            .ToList();

        CurrencyCards.ItemsSource = metrics;
        BalanceText.Text = metrics.Count == 0 ? "Sin saldos" : string.Join("\n", metrics.Select(item => item.TotalText));
        CashText.Text = metrics.Count == 0 ? "Sin efectivo" : string.Join("\n", metrics.Select(item => $"{item.Cash:N0} {item.Currency}"));

        var pending = _services.ListAll();
        ServicesText.Text = pending.Count(item => item.Status == "Abierto").ToString("N0");
        ReadyText.Text = pending.Count(item => item.Status == "Finalizado").ToString("N0");
        PendingAmountText.Text = FormatByCurrency(pending.Where(item => item.Status == "Abierto"));
        ReadyAmountText.Text = FormatByCurrency(pending.Where(item => item.Status == "Finalizado"));

        var agencies = _agencies.ListActive();
        AgencyFundsCardText.Text = $"{agencies.Sum(item => item.CupFund):N0} CUP";

        var primary = new CurrencySettingsRepository().ListCurrencies().FirstOrDefault(item => item.IsPrimary)?.Code ?? "CUP";
        var values = metrics.ToDictionary(item => item.Currency, item => item.Total);
        decimal? ConvertTotal(string target)
        {
            decimal total = 0;
            foreach (var (source, value) in values)
            {
                if (source == target) { total += value; continue; }
                decimal? rate = source == primary || target == primary
                    ? _exchange.GetSuggestedRate(source, target)
                    : _exchange.GetSuggestedRate(source, primary);
                if (rate is null || rate <= 0) return null;
                var primaryValue = source == primary ? value : value * rate.Value;
                if (target == primary) { total += primaryValue; continue; }
                var targetRate = _exchange.GetSuggestedRate(primary, target);
                if (targetRate is null || targetRate <= 0) return null;
                total += primaryValue / targetRate.Value;
            }
            return total;
        }
        var cup = ConvertTotal("CUP");
        var usd = ConvertTotal("USD");
        EquivalentText.Text = $"{(cup is null ? "—" : $"{cup:N0}")} CUP\n{(usd is null ? "—" : $"{usd:N0}")} USD";
        EquivalentHint.Text = cup is null || usd is null ? "Falta una tasa de cambio vigente para alguna moneda." :
            "Estimado con las últimas tasas guardadas; no representa dinero adicional.";

        var rentSettings = new VehicleRentSettingsRepository().Get();
        var rentAccount = rentSettings is null ? null : accounts.FirstOrDefault(item => item.Account.Id == rentSettings.AccountId);
        var rentBalance = rentAccount?.Balances.FirstOrDefault(item => item.Currency == "CUP")?.Amount ?? 0m;
        var rentWeekStart = WorkdayRepository.WeekStart(DateOnly.FromDateTime(DateTime.Today));
        var week = _workdays.GetWeek(rentWeekStart);
        var consolidated = _workdays.GetConsolidatedDates(rentWeekStart);
        var weeklyRent = week.Days.Where(day => day.Date.DayOfWeek != DayOfWeek.Sunday &&
            day.Date >= DatabaseInitializer.StartDate && day.Activity != "No work" &&
            !consolidated.Contains(day.Date)).Sum(_ => rentSettings?.DailyCupAmount ?? 9000m);
        RentFundText.Text = $"{rentBalance:N0} CUP reunidos";
        RentMissingText.Text = $"Faltan {Math.Max(0, weeklyRent - rentBalance):N0} CUP de {weeklyRent:N0} CUP esta semana";

        var currentWeek = DateOnly.FromDateTime(DateTime.Today);
        currentWeek = currentWeek.AddDays(-((int)currentWeek.DayOfWeek + 6) % 7);
        _weeklyIncome = _statistics.ListWeeklyIncome(currentWeek.AddDays(-7 * 7), currentWeek.AddDays(6));
        _personalFlow = _statistics.ListPersonalAccountFlow(currentWeek.AddDays(-7 * 7), currentWeek.AddDays(6));
        var currencies = metrics.Select(item => item.Currency)
            .Concat(_weeklyIncome.Select(item => item.Currency))
            .Concat(_personalFlow.Select(item => item.Currency)).Distinct().ToList();
        if (currencies.Count == 0) currencies.Add("CUP");
        _loading = true;
        var selected = ChartCurrency.SelectedItem as string;
        ChartCurrency.ItemsSource = currencies;
        ChartCurrency.SelectedItem = selected is not null && currencies.Contains(selected) ? selected :
            currencies.Contains("CUP") ? "CUP" : currencies[0];
        _loading = false;
        RefreshChart();
    }

    private static string FormatByCurrency(IEnumerable<ServiceOverview> services)
    {
        var totals = services.GroupBy(item => item.CurrencyCode)
            .Select(group => $"{group.Sum(item => item.Amount):N0} {group.Key}").ToList();
        return totals.Count == 0 ? "Sin importe pendiente" : string.Join(" · ", totals);
    }

    private void ChartCurrency_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && TrendPath is not null) RefreshChart();
    }

    private void RefreshChart()
    {
        if (ChartCurrency.SelectedItem is not string currency) return;
        var currentWeek = DateOnly.FromDateTime(DateTime.Today);
        currentWeek = currentWeek.AddDays(-((int)currentWeek.DayOfWeek + 6) % 7);
        var weeks = Enumerable.Range(0, 8).Select(index => currentWeek.AddDays((index - 7) * 7))
            .Select(week =>
            {
                var income = _weeklyIncome.FirstOrDefault(item => item.WeekStart == week && item.Currency == currency);
                return (Week: week, Amount: income?.Amount ?? 0m, Services: income?.Services ?? 0);
            }).ToList();
        var max = Math.Max(1m, weeks.Max(item => item.Amount));
        BarChart.ItemsSource = weeks.Select(item => new WeeklyChartItem(item.Week, item.Amount,
            item.Services, (double)(item.Amount / max) * 150, currency)).ToList();

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var index = 0; index < weeks.Count; index++)
            {
                var point = new System.Windows.Point(index * 80, 145 - (double)(weeks[index].Amount / max) * 125);
                if (index == 0) context.BeginFigure(point, false, false);
                else context.LineTo(point, true, false);
            }
        }
        geometry.Freeze();
        TrendPath.Data = geometry;

        var thisWeek = weeks[^1];
        var best = weeks.OrderByDescending(item => item.Amount).First();
        CurrentWeekText.Text = $"{thisWeek.Amount:N0} {currency}";
        CurrentWeekCountText.Text = $"{thisWeek.Services} servicios finalizados";
        BestWeekText.Text = $"{best.Amount:N0} {currency}";
        BestWeekDateText.Text = $"Semana del {best.Week:dd/MM/yyyy}";
        PeriodIncomeText.Text = $"{weeks.Sum(item => item.Amount):N0} {currency}";
        PeriodServicesText.Text = $"{weeks.Sum(item => item.Services)} servicios en 8 semanas";
        TrendText.Text = weeks.All(item => item.Amount == 0) ? "Aún no hay ingresos en este período." :
            $"Máximo semanal: {best.Amount:N0} {currency} · semana del {best.Week:dd/MM/yyyy}";

        var flows = weeks.Select(item =>
        {
            var flow = _personalFlow.FirstOrDefault(entry => entry.WeekStart == item.Week && entry.Currency == currency);
            return (item.Week, Income: flow?.Income ?? 0m, Withdrawals: flow?.Withdrawals ?? 0m);
        }).ToList();
        var flowMax = Math.Max(1m, flows.Max(item => Math.Max(item.Income, item.Withdrawals)));
        PersonalFlowChart.ItemsSource = flows.Select(item => new WeeklyFlowChartItem(item.Week,
            item.Income, item.Withdrawals, (double)(item.Income / flowMax) * 112,
            (double)(item.Withdrawals / flowMax) * 112, currency)).ToList();
        PersonalFlowTotalText.Text = $"Ingresos: {flows.Sum(item => item.Income):N0} {currency}  ·  Retiros: {flows.Sum(item => item.Withdrawals):N0} {currency}";
    }
}
