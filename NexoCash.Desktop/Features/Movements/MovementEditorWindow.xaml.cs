using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NexoCash.Domain.Entities;
using NexoCash.Infrastructure.Persistence;
using NexoCash.Desktop.Shared.Windows;

namespace NexoCash.Desktop.Features.Movements;

public enum MovementEntryKind { Deposit, Withdrawal, Transfer }

public partial class MovementEditorWindow : Window
{
    private sealed record AccountOption(AccountListItem Item)
    {
        public string Label => $"{Item.Account.Name} ({(Item.Account.Type == AccountType.Cash ? "Efectivo" : "Virtual")})";
    }

    private readonly AccountRepository _repository = new();
    private readonly CashValuationRepository _valuation = new();
    private readonly MovementEntryKind _kind;
    private BanknoteCountResult? _countResult;

    public MovementEditorWindow(MovementEntryKind kind)
    {
        InitializeComponent();
        _kind = kind;
        Title = FormTitle.Text = kind switch
        {
            MovementEntryKind.Deposit => "Ingreso",
            MovementEntryKind.Withdrawal => "Retiro",
            _ => "Transferencia entre cuentas"
        };
        SaveButton.Content = kind == MovementEntryKind.Transfer ? "Guardar transferencia" : kind == MovementEntryKind.Deposit ? "Guardar ingreso" : "Guardar retiro";
        HelpText.Text = kind switch
        {
            MovementEntryKind.Deposit => "Registra el dinero recibido en una cuenta.",
            MovementEntryKind.Withdrawal => "Registra el dinero retirado de una cuenta.",
            _ => "Mueve dinero entre dos cuentas en la misma moneda."
        };
        SourceLabel.Text = kind == MovementEntryKind.Transfer ? "Cuenta de origen" : "Cuenta";
        DestinationPanel.Visibility = kind == MovementEntryKind.Transfer ? Visibility.Visible : Visibility.Collapsed;
        var accounts = _repository.ListActive().Select(item => new AccountOption(item)).ToList();
        SourcePicker.ItemsSource = accounts;
        DestinationPicker.ItemsSource = accounts;
        SourcePicker.SelectedIndex = accounts.Count > 0 ? 0 : -1;
        DestinationPicker.SelectedIndex = accounts.Count > 1 ? 1 : -1;
        CurrencyPicker.ItemsSource = _repository.ListCurrencies();
        CurrencyPicker.SelectedIndex = CurrencyPicker.Items.Count > 0 ? 0 : -1;
        UpdateBanknotes();
    }

    private void Account_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (BanknotesPanel is not null) { InvalidateCount(); UpdateBanknotes(); }
    }

    private void Currency_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CountSummaryText is not null) InvalidateCount();
    }

    private void Amount_Changed(object sender, TextChangedEventArgs e)
    {
        if (CountSummaryText is not null) InvalidateCount();
    }

    private void InvalidateCount()
    {
        _countResult = null;
        CountSummaryText.Text = "Conteo opcional de billetes.";
    }

    private void UpdateBanknotes()
    {
        if (SourcePicker.SelectedItem is not AccountOption source) return;
        var needsNotes = _kind == MovementEntryKind.Transfer
            ? DestinationPicker.SelectedItem is AccountOption destination &&
              source.Item.Account.Type != destination.Item.Account.Type
            : source.Item.Account.Type == AccountType.Cash;
        BanknotesPanel.Visibility = needsNotes ? Visibility.Visible : Visibility.Collapsed;
        if (_kind == MovementEntryKind.Transfer && needsNotes)
            BanknotesLabel.Text = source.Item.Account.Type == AccountType.Cash
                ? "Billetes de efectivo entregados" : "Billetes de efectivo recibidos";
    }

    private void CountBanknotes_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (SourcePicker.SelectedItem is not AccountOption source) throw new ArgumentException("Elige una cuenta.");
            if (CurrencyPicker.SelectedItem is not string currency) throw new ArgumentException("Elige una moneda.");
            if (!decimal.TryParse(AmountBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
                throw new ArgumentException("Primero introduce un importe positivo.");
            var expectedCup = _valuation.ToCup(new Dictionary<string, decimal> { [currency] = amount });
            var suggest = _kind == MovementEntryKind.Withdrawal
                || _kind == MovementEntryKind.Transfer && source.Item.Account.Type == AccountType.Cash;
            var window = new BanknoteCountWindow(expectedCup, [currency],
                suggestCurrency: suggest ? currency : null, suggestAmount: suggest ? amount : null) { Owner = this };
            if (window.ShowDialog() != true || window.Result is null) return;
            _countResult = window.Result;
            CountSummaryText.Text = $"{_countResult.Summary} · {_countResult.TotalCup:N2} CUP";
            ErrorText.Text = _countResult.TotalCup == expectedCup ? "" :
                $"Contado: {_countResult.TotalCup:N2} CUP; esperado: {expectedCup:N2} CUP. Ajusta el conteo antes de guardar.";
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (SourcePicker.SelectedItem is not AccountOption source) throw new ArgumentException("Elige una cuenta.");
            if (CurrencyPicker.SelectedItem is not string currency) throw new ArgumentException("Elige una moneda.");
            if (!decimal.TryParse(AmountBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
                throw new ArgumentException("Introduce un importe positivo.");
            var needsNotes = BanknotesPanel.Visibility == Visibility.Visible;
            var countRequired = _kind == MovementEntryKind.Transfer && needsNotes;
            if (countRequired && _countResult is null)
                throw new InvalidOperationException("Primero cuenta los billetes.");
            var hasCount = needsNotes && _countResult is not null;
            var expectedCup = hasCount
                ? _valuation.ToCup(new Dictionary<string, decimal> { [currency] = amount }) : 0m;
            if (hasCount && _countResult!.TotalCup != expectedCup)
                throw new InvalidOperationException("Los billetes contados deben coincidir con el importe indicado en CUP.");
            var notes = hasCount
                ? _countResult!.Counts.GetValueOrDefault(currency) ?? new Dictionary<decimal, int>()
                : new Dictionary<decimal, int>();
            if (_kind == MovementEntryKind.Transfer)
            {
                if (DestinationPicker.SelectedItem is not AccountOption destination)
                    throw new ArgumentException("Elige una cuenta de destino.");
                _repository.Transfer(source.Item.Account.Id, destination.Item.Account.Id, currency, amount, notes, DescriptionBox.Text);
            }
            else
            {
                _repository.Record(source.Item.Account.Id, currency, amount,
                    _kind == MovementEntryKind.Deposit, notes, DescriptionBox.Text);
            }
            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

}
