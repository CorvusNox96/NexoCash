using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NexoCash.Infrastructure.Persistence;
using NexoCash.Domain.Entities;

namespace NexoCash.Desktop.Features.Services;

public partial class ServiceConsolidationWindow : Window
{
    private sealed record TotalOption(string Currency, string Payment, decimal Total)
    {
        public string Label => $"{Currency} · {PaymentLabel(Payment)}: {Total:N2}";
    }
    private sealed record AccountOption(ConsolidationAccount? Account)
    {
        public string Label => Account is null ? "Retiro directo" :
            $"{Account.Name} ({(Account.IsCash ? "Efectivo" : "Virtual")})";
    }
    private sealed record AllocationOption(ConsolidationAllocation Allocation, string AccountName)
    {
        public string Label => $"{AccountName} · {Allocation.Amount:N2} {Allocation.Currency} · {PaymentLabel(Allocation.Payment)}";
    }

    private readonly ServiceConsolidationRepository _repository = new();
    private readonly List<AllocationOption> _allocations = [];
    private IReadOnlyList<ConsolidationService> _services = [];

    public ServiceConsolidationWindow(DateOnly date)
    {
        InitializeComponent();
        WorkDatePicker.SelectedDate = date.ToDateTime(TimeOnly.MinValue);
        LoadAccounts();
        Refresh();
    }

    private DateOnly SelectedDate => DateOnly.FromDateTime(WorkDatePicker.SelectedDate ?? DateTime.Today);
    private static string PaymentLabel(string payment) => payment == "Cash" ? "Efectivo" : "Transferencia";

    private void LoadAccounts()
    {
        AccountPicker.ItemsSource = _repository.ListAccounts().Select(a => new AccountOption(a))
            .Append(new AccountOption(null)).ToList();
        AccountPicker.SelectedIndex = AccountPicker.Items.Count > 0 ? 0 : -1;
    }

    private void Refresh()
    {
        _allocations.Clear();
        AllocationsList.ItemsSource = null;
        _services = _repository.ListPending(SelectedDate);
        var groups = _services.GroupBy(s => (s.Currency, s.Payment))
            .Select(g => new TotalOption(g.Key.Currency, g.Key.Payment, g.Sum(s => s.Amount))).ToList();
        TotalPicker.ItemsSource = groups;
        TotalPicker.SelectedIndex = groups.Count > 0 ? 0 : -1;
        var breakdown = _services.GroupBy(s => (s.Currency, s.Payment, s.Type, s.Recipient))
            .Select(g => $"{g.Key.Currency} · {PaymentLabel(g.Key.Payment)} · {g.Key.Type} · {g.Key.Recipient}: {g.Sum(s => s.Amount):N2}" +
                (g.Sum(s => s.Commission) > 0 ? $" (comisión del fondo de la agencia: {g.Sum(s => s.Commission):N2})" : ""));
        SummaryText.Text = _services.Count == 0 ? "No hay servicios terminados en esta fecha." : string.Join("\n", breakdown);
        ServiceCountText.Text = $"{_services.Count} servicios terminados listos para consolidar";
        UpdateRemaining();
    }

    private void UpdateRemaining()
    {
        var groups = _services.GroupBy(s => (s.Currency, s.Payment))
            .Select(g => $"{g.Key.Currency} {PaymentLabel(g.Key.Payment)}: {g.Sum(s => s.Amount) - _allocations.Where(a => a.Allocation.Currency == g.Key.Currency && a.Allocation.Payment == g.Key.Payment).Sum(a => a.Allocation.Amount):N2} restantes");
        AllRemainingText.Text = string.Join("  ·  ", groups);
        if (TotalPicker.SelectedItem is not TotalOption total)
        {
            RemainingText.Text = "";
            return;
        }
        var used = _allocations.Where(a => a.Allocation.Currency == total.Currency && a.Allocation.Payment == total.Payment)
            .Sum(a => a.Allocation.Amount);
        RemainingText.Text = $"Restante: {total.Total - used:N2} {total.Currency} ({PaymentLabel(total.Payment)})";
    }

    private void WorkDate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SummaryText is not null) Refresh();
    }
    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
    private void Total_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (RemainingText is not null) UpdateRemaining();
    }

    private void AddAllocation_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (TotalPicker.SelectedItem is not TotalOption total) throw new InvalidOperationException("Elige un total cobrado.");
            if (AccountPicker.SelectedItem is not AccountOption account) throw new InvalidOperationException("Crea o elige una cuenta.");
            if (account.Account is { } destination && destination.IsCash != (total.Payment == "Cash"))
                throw new InvalidOperationException("Elige una cuenta de efectivo para efectivo o una cuenta virtual para transferencias.");
            if (!decimal.TryParse(AmountBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
                throw new InvalidOperationException("Introduce un importe positivo.");
            var used = _allocations.Where(a => a.Allocation.Currency == total.Currency && a.Allocation.Payment == total.Payment)
                .Sum(a => a.Allocation.Amount);
            if (amount > total.Total - used)
                throw new InvalidOperationException("La distribución supera el importe restante.");
            if (account.Account is null)
            {
                var allocation = new ConsolidationAllocation(Guid.Empty, total.Currency, total.Payment, amount,
                    IsDirectWithdrawal: true);
                _allocations.Add(new(allocation, "Retiro directo"));
            }
            else
                _allocations.Add(new(new(account.Account.Id, total.Currency, total.Payment, amount), account.Label));
            AllocationsList.ItemsSource = null;
            AllocationsList.ItemsSource = _allocations.ToList();
            AmountBox.Clear();
            UpdateRemaining();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void RemoveAllocation_Click(object sender, RoutedEventArgs e)
    {
        if (AllocationsList.SelectedItem is not AllocationOption selected) return;
        _allocations.Remove(selected);
        AllocationsList.ItemsSource = null;
        AllocationsList.ItemsSource = _allocations.ToList();
        UpdateRemaining();
    }

    private void ApplyRules_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_services.Count == 0) throw new InvalidOperationException("No hay servicios terminados en esta fecha.");
            if (_allocations.Count > 0 && MessageBox.Show(this,
                    "¿Reemplazar las distribuciones actuales con las sugeridas por las reglas?", "Reglas de distribución",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var groups = _services.GroupBy(s => (s.Currency, s.Payment))
                .ToDictionary(group => group.Key, group => group.Sum(service => service.Amount));
            var accounts = _repository.ListAccounts().ToDictionary(account => account.Id);
            _allocations.Clear();
            foreach (var rule in new DistributionRuleRepository().List())
            {
                if (SelectedDate.DayOfWeek == DayOfWeek.Sunday && !rule.AppliesOnSundays) continue;
                if (!accounts.TryGetValue(rule.AccountId, out var account)) continue;
                foreach (var (key, total) in groups)
                {
                    if (account.IsCash != (key.Payment == "Cash")) continue;
                    if (rule.Type == DistributionRuleType.FixedAmount && rule.Currency != key.Currency) continue;
                    var remaining = total - _allocations.Where(item => item.Allocation.Currency == key.Currency
                        && item.Allocation.Payment == key.Payment).Sum(item => item.Allocation.Amount);
                    var proposed = rule.Type == DistributionRuleType.FixedAmount ? rule.Value
                        : decimal.Round(total * rule.Value / 100m, 2, MidpointRounding.AwayFromZero);
                    var amount = Math.Min(remaining, proposed);
                    if (amount <= 0) continue;
                    var allocation = new ConsolidationAllocation(account.Id, key.Currency, key.Payment, amount);
                    _allocations.Add(new(allocation, $"{account.Name} · {rule.Name}"));
                }
            }
            AllocationsList.ItemsSource = null;
            AllocationsList.ItemsSource = _allocations.ToList();
            UpdateRemaining();
            if (_allocations.Count == 0)
                MessageBox.Show(this, "Ninguna regla guardada coincide con estos servicios y tipos de cuenta.",
                    "Reglas de distribución", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_services.Count == 0) throw new InvalidOperationException("No hay servicios terminados para consolidar.");
            var totals = _services.GroupBy(s => (s.Currency, s.Payment)).ToDictionary(g => g.Key, g => g.Sum(s => s.Amount));
            if (totals.Any(t => _allocations.Where(a => a.Allocation.Currency == t.Key.Currency && a.Allocation.Payment == t.Key.Payment)
                .Sum(a => a.Allocation.Amount) != t.Value))
                throw new InvalidOperationException("Distribuye todos los totales hasta que el importe restante sea cero.");
            var report = string.Join("\n", totals.Select(total =>
                $"{total.Value:N2} {total.Key.Currency} · {PaymentLabel(total.Key.Payment)}"));
            var next = new ServiceBanknotesWindow(SelectedDate,
                _allocations.Select(a => a.Allocation).ToList(), report) { Owner = this };
            if (next.ShowDialog() == true) DialogResult = true;
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ShowError(string message) => MessageBox.Show(this, message, "Consolidación de servicios", MessageBoxButton.OK, MessageBoxImage.Warning);
}
