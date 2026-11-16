using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using NexoCash.Desktop.Shared.Windows;
using NexoCash.Domain.Entities;
using NexoCash.Infrastructure.Persistence;

namespace NexoCash.Desktop.Features.Workdays;

public partial class WorkdaysView : UserControl
{
    private const decimal DailyRentCup = 9_000m;
    private readonly WorkdayRepository _workdays = new();
    private DateOnly _selectedWeek = WorkdayRepository.WeekStart(DateOnly.FromDateTime(DateTime.Today));
    private bool _refreshing;
    private List<DayRow> _rows = [];
    private bool _cashAccount;

    private sealed record DayRow(DateOnly Date, string Name, string DateText, int StatusIndex,
        bool IsEditable, bool IsWorkedChecked, bool IsNoWorkChecked, string StateHint,
        string FundText, string MissingText, bool IsCovered, bool IsNotApplicable);

    public WorkdaysView()
    {
        InitializeComponent();
        var start = DatabaseInitializer.StartDate;
        var firstWeek = FirstAvailableWeek(start);
        WeekPicker.DisplayDateStart = firstWeek.ToDateTime(TimeOnly.MinValue);
        var currentWeek = WorkdayRepository.WeekStart(DateOnly.FromDateTime(DateTime.Today));
        _selectedWeek = currentWeek < firstWeek ? firstWeek : currentWeek;
        _refreshing = true;
        WeekPicker.SelectedDate = _selectedWeek.ToDateTime(TimeOnly.MinValue);
        _refreshing = false;
        RefreshList();
    }

    public void RefreshList()
    {
        WeekDateText.Text = _selectedWeek.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        _refreshing = true;
        try
        {
        var week = _workdays.GetWeek(_selectedWeek);
        var startDate = DatabaseInitializer.StartDate;
        PreviousWeekButton.IsEnabled = week.Start > FirstAvailableWeek(startDate);
        var consolidated = _workdays.GetConsolidatedDates(_selectedWeek);
        var weeklyRent = week.Days.Where(day => day.Date.DayOfWeek != DayOfWeek.Sunday &&
            day.Date >= startDate && day.Activity != "No work" && !consolidated.Contains(day.Date)).Sum(_ => DailyRentCup);
        WeeklyTotalText.Text = FormatCup(weeklyRent);
        WeekRangeText.Text = $"Del {week.Start:dd/MM/yyyy} al {week.End:dd/MM/yyyy} · domingo libre";
        var eligibleDays = week.Days.Count(day => day.Date.DayOfWeek != DayOfWeek.Sunday && day.Date >= startDate);
        WeekStatusText.Text = consolidated.Count == eligibleDays ? "Semana cerrada" :
            $"{consolidated.Count} de {eligibleDays} días consolidados";
        var settings = new VehicleRentSettingsRepository().Get();
        var account = settings is null ? null : new AccountRepository().ListActive()
            .FirstOrDefault(item => item.Account.Id == settings.AccountId);
        var balance = account?.Balances.FirstOrDefault(item => item.Currency == "CUP")?.Amount ?? 0m;
        _cashAccount = account?.Account.Type == AccountType.Cash;
        var available = Math.Max(0m, balance);
        var rows = new List<DayRow>();
        foreach (var day in week.Days)
        {
            var isFree = day.Date.DayOfWeek == DayOfWeek.Sunday;
            var beforeStart = day.Date < startDate;
            var isConsolidated = consolidated.Contains(day.Date);
            var due = isFree || beforeStart || isConsolidated || day.Activity == "No work" ? 0m : DailyRentCup;
            var fromFund = Math.Min(due, available);
            available -= fromFund;
            rows.Add(new DayRow(day.Date,
                CultureInfo.CurrentCulture.TextInfo.ToTitleCase(day.DayName),
                day.Date.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture),
                beforeStart ? 4 : isFree ? 3 : day.Activity switch
                {
                    "Worked" => 1,
                    "No work" => 2,
                    _ => 0
                },
                !isFree && !beforeStart && !isConsolidated,
                day.Activity == "Worked", day.Activity == "No work" && !isFree,
                beforeStart ? "Fuera de período" : isFree ? "Libre" : isConsolidated ? "Consolidado" : "",
                isConsolidated && day.Activity == "Worked" ? "Pagado" : due == 0 ? "—" : FormatCup(fromFund),
                isConsolidated && day.Activity == "Worked" ? "✓" : due == 0 ? "—" :
                    due == fromFund ? "✓" : FormatCup(due - fromFund),
                isConsolidated && day.Activity == "Worked" || due > 0 && due == fromFund,
                due == 0 && !isConsolidated || isConsolidated && day.Activity == "No work"));
        }
        _rows = rows;
        DaysList.ItemsSource = rows;
        UpdateConsolidationSummary();

        RentBalanceText.Text = FormatCup(balance);
        RentAccountText.Text = account is null ? "Sin cuenta de alquiler asignada" :
            account.Account.Name;

        var difference = balance - weeklyRent;
        var covered = difference >= 0;
        DifferenceLabel.Text = covered ? "Excedente" : "Deuda";
        DifferenceText.Text = FormatCup(Math.Abs(difference));
        DifferenceText.Foreground = covered ? Brushes.ForestGreen : Brushes.Firebrick;
        DifferenceCard.Background = new SolidColorBrush(covered
            ? Color.FromRgb(236, 248, 241) : Color.FromRgb(253, 241, 239));
        DifferenceCard.BorderBrush = new SolidColorBrush(covered
            ? Color.FromRgb(185, 226, 201) : Color.FromRgb(241, 199, 193));
        }
        finally { _refreshing = false; }
    }

    private List<DayRow> DaysToConsolidate => _rows.Where(row => row.IsEditable &&
        row.StatusIndex is 1 or 2).ToList();

    private void UpdateConsolidationSummary()
    {
        var days = DaysToConsolidate;
        ConsolidateButton.IsEnabled = days.Count > 0;
        SelectionSummaryText.Text = days.Count == 0
            ? "Marca los días trabajados o no trabajados para poder consolidarlos."
            : $"{days.Count} día(s) listos para consolidar · extracción: {FormatCup(days.Count(day => day.StatusIndex == 1) * DailyRentCup)}";
    }

    private void DayStatus_Changed(object sender, RoutedEventArgs e)
    {
        if (_refreshing || sender is not CheckBox { DataContext: DayRow day } check ||
            !check.IsLoaded || !day.IsEditable ||
            !ReferenceEquals(_rows.FirstOrDefault(row => row.Date == day.Date), day)) return;
        if (check.IsChecked != true &&
            !(check.Name == "WorkedCheck" && day.StatusIndex == 1 ||
              check.Name == "NoWorkCheck" && day.StatusIndex == 2)) return;
        bool? worked = check.IsChecked == true ? check.Name == "WorkedCheck" : null;
        if (worked == (day.StatusIndex == 1 ? true : day.StatusIndex == 2 ? false : null)) return;
        try
        {
            _workdays.SetActivity(day.Date, worked);
            RefreshList();
        }
        catch (Exception ex) { ShowError(ex.Message); RefreshList(); }
    }

    private void Consolidate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var days = DaysToConsolidate;
            var dates = days.Select(day => day.Date).OrderBy(date => date).ToList();
            if (dates.Count == 0) return;
            var amount = days.Count(day => day.StatusIndex == 1) * DailyRentCup;
            IReadOnlyDictionary<decimal, int>? notes = null;
            if (_cashAccount && amount > 0)
            {
                var choice = MessageBox.Show(Window.GetWindow(this),
                    "¿Quieres revisar el desglose sugerido de billetes? Puedes cambiarlo manualmente. Elige No para consolidar sin conteo.",
                    "Billetes del pago", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (choice == MessageBoxResult.Cancel) return;
                if (choice == MessageBoxResult.Yes)
                {
                    var picker = new BanknoteCountWindow(amount, ["CUP"], nativeCurrency: "CUP",
                        nativeExpected: amount, suggestCurrency: "CUP", suggestAmount: amount)
                        { Owner = Window.GetWindow(this) };
                    if (picker.ShowDialog() != true || picker.Result is null) return;
                    notes = picker.Result.Counts.GetValueOrDefault("CUP");
                    if (notes is null || notes.Sum(note => note.Key * note.Value) != amount)
                        throw new InvalidOperationException("Los billetes deben sumar exactamente el importe a extraer.");
                }
            }
            var message = $"¿Consolidar {dates.Count} día(s)? Se extraerán {FormatCup(amount)} de la cuenta de alquiler y los días quedarán bloqueados.";
            if (MessageBox.Show(Window.GetWindow(this), message, "Consolidar semana",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _workdays.ConsolidateDays(dates, notes);
            RefreshList();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ShowError(string message) => MessageBox.Show(Window.GetWindow(this), message,
        "Consolidación de alquiler", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void WeekChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || WeekPicker.SelectedDate is not { } selected) return;
        var firstWeek = FirstAvailableWeek(DatabaseInitializer.StartDate);
        var chosenWeek = WorkdayRepository.WeekStart(DateOnly.FromDateTime(selected));
        _selectedWeek = chosenWeek < firstWeek ? firstWeek : chosenWeek;
        RefreshList();
    }

    private void PreviousWeek_Click(object sender, RoutedEventArgs e) => SelectWeek(_selectedWeek.AddDays(-7));
    private void CalendarButton_Click(object sender, RoutedEventArgs e)
    {
        WeekCalendar.DisplayDate = _selectedWeek.ToDateTime(TimeOnly.MinValue);
        WeekCalendarPopup.IsOpen = true;
    }

    private void WeekCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!WeekCalendarPopup.IsOpen || WeekCalendar.SelectedDate is not { } date) return;
        WeekCalendarPopup.IsOpen = false;
        SelectWeek(DateOnly.FromDateTime(date));
    }

    private void WeekDate_LostFocus(object sender, RoutedEventArgs e) => ApplyTypedWeek();
    private void WeekDate_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyTypedWeek();
        e.Handled = true;
    }

    private void ApplyTypedWeek()
    {
        if (DateOnly.TryParseExact(WeekDateText.Text, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date)) SelectWeek(date);
        else WeekDateText.Text = _selectedWeek.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    }
    private void NextWeek_Click(object sender, RoutedEventArgs e) => SelectWeek(_selectedWeek.AddDays(7));
    private void CurrentWeek_Click(object sender, RoutedEventArgs e) =>
        SelectWeek(DateOnly.FromDateTime(DateTime.Today));

    private void SelectWeek(DateOnly date)
    {
        var firstWeek = FirstAvailableWeek(DatabaseInitializer.StartDate);
        var chosenWeek = WorkdayRepository.WeekStart(date);
        _selectedWeek = chosenWeek < firstWeek ? firstWeek : chosenWeek;
        _refreshing = true;
        WeekPicker.SelectedDate = _selectedWeek.ToDateTime(TimeOnly.MinValue);
        _refreshing = false;
        RefreshList();
    }

    private static string FormatCup(decimal amount) => $"{amount:N2} CUP";
    private static DateOnly FirstAvailableWeek(DateOnly start) => start.DayOfWeek == DayOfWeek.Sunday
        ? start.AddDays(1) : WorkdayRepository.WeekStart(start);
}
