using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NexoCash.Desktop.Shared.Windows;
using NexoCash.Domain.Entities;
using NexoCash.Infrastructure.Persistence;

namespace NexoCash.Desktop.Features.Agencies;

public partial class AgencyTopUpWindow : Window
{
    private sealed record AccountOption(AccountListItem Item)
    {
        public string Label => $"{Item.Account.Name} · {(Item.Account.Type == AccountType.Cash ? "Efectivo" : "Transferencia")}";
    }

    private readonly Guid _agencyId;
    private readonly AgencyRepository _repository = new();
    private BanknoteCountResult? _count;

    public AgencyTopUpWindow(Guid agencyId)
    {
        _agencyId = agencyId;
        InitializeComponent();
        var accounts = new AccountRepository().ListActive()
            .Where(item => item.Balances.Any(balance => balance.Currency == "CUP" && balance.Amount > 0))
            .Select(item => new AccountOption(item)).ToList();
        AccountPicker.ItemsSource = accounts;
        AccountPicker.SelectedIndex = accounts.Count > 0 ? 0 : -1;
        TransactionBox.Focus();
        UpdatePreview();
    }

    private void Account_Changed(object sender, SelectionChangedEventArgs e)
    {
        InvalidateCount();
        UpdatePreview();
    }

    private void Values_Changed(object sender, TextChangedEventArgs e)
    {
        InvalidateCount();
        UpdatePreview();
    }

    private void InvalidateCount()
    {
        _count = null;
        if (CountSummary is not null) CountSummary.Text = "Puedes omitir el conteo de billetes.";
    }

    private void UpdatePreview()
    {
        if (DebitPreview is null || AccountPicker is null) return;
        var cash = AccountPicker.SelectedItem is AccountOption { Item.Account.Type: AccountType.Cash };
        PercentBox.IsEnabled = !cash;
        CountButton.Visibility = cash ? Visibility.Visible : Visibility.Collapsed;
        if (cash) PercentBox.Text = "0";
        if (TryAmounts(out var amount, out var percent, out var debit))
            DebitPreview.Text = $"Se retiran {debit:N2} CUP · se abonan {amount:N2} CUP al fondo";
        else DebitPreview.Text = "Introduce el importe para calcular el retiro.";
    }

    private bool TryAmounts(out decimal amount, out decimal percent, out decimal debit)
    {
        var validAmount = decimal.TryParse(AmountBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out amount) && amount > 0;
        var validPercent = decimal.TryParse(PercentBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out percent) && percent is >= 0 and <= 100;
        debit = validAmount && validPercent
            ? decimal.Round(amount * (1m + percent / 100m), 2, MidpointRounding.AwayFromZero) : 0m;
        return validAmount && validPercent;
    }

    private void Count_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        if (AccountPicker.SelectedItem is not AccountOption { Item.Account.Type: AccountType.Cash }) return;
        if (!TryAmounts(out _, out _, out var debit))
        {
            ErrorText.Text = "Introduce primero un importe válido.";
            return;
        }
        var window = new BanknoteCountWindow(allowedCurrencies: ["CUP"], nativeCurrency: "CUP",
            nativeExpected: debit, suggestCurrency: "CUP", suggestAmount: debit) { Owner = this };
        if (window.ShowDialog() != true || window.Result is null) return;
        _count = window.Result;
        CountSummary.Text = $"Billetes contados: {_count.Summary}";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        try
        {
            if (AccountPicker.SelectedItem is not AccountOption account)
                throw new ArgumentException("Elige una cuenta de origen con saldo en CUP.");
            if (!TryAmounts(out var amount, out var percent, out var debit))
                throw new ArgumentException("Introduce un importe positivo y un porcentaje entre 0 y 100.");
            var notes = account.Item.Account.Type == AccountType.Cash
                ? _count?.Counts.GetValueOrDefault("CUP") ?? new Dictionary<decimal, int>()
                : new Dictionary<decimal, int>();
            if (notes.Count > 0 && notes.Sum(note => note.Key * note.Value) != debit)
                throw new InvalidOperationException("El conteo debe coincidir con el retiro actual.");
            _repository.TopUp(_agencyId, account.Item.Account.Id, TransactionBox.Text, amount, percent, notes);
            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
