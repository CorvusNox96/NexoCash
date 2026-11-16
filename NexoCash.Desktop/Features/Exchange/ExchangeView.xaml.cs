using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NexoCash.Desktop.Shared.Windows;
using NexoCash.Domain.Entities;
using NexoCash.Infrastructure.Persistence;

namespace NexoCash.Desktop.Features.Exchange;

public partial class ExchangeView : UserControl
{
    private sealed record AccountOption(AccountListItem Item)
    { public string Label => $"{Item.Account.Name} ({Item.TypeLabel})"; }
    private readonly CurrencyExchangeRepository _repository = new();
    private readonly AccountRepository _accounts = new();
    private string _primaryCurrency = "CUP";
    private BanknoteCountResult? _outgoing;
    private BanknoteCountResult? _incoming;
    public ExchangeView() { InitializeComponent(); RefreshList(); }

    public void RefreshList()
    {
        var exchanges = _repository.List();
        var history = CollectionViewSource.GetDefaultView(exchanges);
        history.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CurrencyExchangeOverview.Day)));
        ExchangesGrid.ItemsSource = history;
        var accounts = _accounts.ListActive().Select(item => new AccountOption(item)).ToList();
        FromAccount.ItemsSource = accounts;
        FromAccount.SelectedIndex = accounts.Count > 0 ? 0 : -1;
        var currencies = _accounts.ListCurrencies();
        _primaryCurrency = new CurrencySettingsRepository().ListCurrencies()
            .FirstOrDefault(item => item.IsPrimary)?.Code ?? "CUP";
        RateLabel.Text = $"Tasa real ({_primaryCurrency} por unidad)";
        FromCurrency.ItemsSource = currencies; ToCurrency.ItemsSource = currencies;
        FromCurrency.SelectedItem = currencies.Contains(_primaryCurrency) ? _primaryCurrency : currencies.FirstOrDefault();
        ToCurrency.SelectedItem = currencies.FirstOrDefault(currency => currency != _primaryCurrency);
        UpdateSuggestedRate();
    }

    private void Currency_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (RateBox is not null) UpdateSuggestedRate();
    }

    private void UpdateSuggestedRate()
    {
        if (FromCurrency.SelectedItem is not string from || ToCurrency.SelectedItem is not string to)
        {
            RateBox.Clear();
            UpdatePreview();
            return;
        }
        var suggested = _repository.GetSuggestedRate(from, to);
        RateBox.Text = suggested?.ToString("0.################", CultureInfo.CurrentCulture) ?? "";
        UpdatePreview();
    }

    private void Amount_Changed(object sender, TextChangedEventArgs e)
    {
        if (ReceivedPreview is not null) UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (ReceivedPreview is null) return;
        if (FromCurrency.SelectedItem is not string from || ToCurrency.SelectedItem is not string to ||
            !decimal.TryParse(OutgoingAmount.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) ||
            !decimal.TryParse(RateBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var rate) ||
            amount <= 0 || rate <= 0)
        {
            ReceivedPreview.Text = "Introduce el importe y la tasa para ver cuánto recibirás.";
            return;
        }
        try
        {
            var received = CurrencyExchangeRepository.CalculateReceivedAmount(from, to, _primaryCurrency, amount, rate);
            ReceivedPreview.Text = $"Recibirás {received:N2} {to} · 1 {(from == _primaryCurrency ? to : from)} = {rate:N2} {_primaryCurrency}";
        }
        catch (ArgumentException ex) { ReceivedPreview.Text = ex.Message; }
    }

    private void Account_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CountOutgoing is null || CountIncoming is null) return;
        CountOutgoing.IsEnabled = FromAccount.SelectedItem is AccountOption { Item.Account.Type: AccountType.Cash };
        CountIncoming.IsEnabled = FromAccount.SelectedItem is AccountOption { Item.Account.Type: AccountType.Cash };
    }

    private decimal ReadAmount(TextBox box, string name)
    {
        if (!decimal.TryParse(box.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) || value <= 0)
            throw new ArgumentException($"Introduce un valor positivo para {name}.");
        return value;
    }

    private void CountOutgoing_Click(object sender, RoutedEventArgs e) => Count(true);
    private void CountIncoming_Click(object sender, RoutedEventArgs e) => Count(false);
    private void Count(bool outgoing)
    {
        try
        {
            var currency = (outgoing ? FromCurrency : ToCurrency).SelectedItem as string ?? throw new ArgumentException("Elige una moneda.");
            var amount = ReadAmount(OutgoingAmount, "importe");
            if (!outgoing)
                amount = CurrencyExchangeRepository.CalculateReceivedAmount(
                    FromCurrency.SelectedItem as string ?? throw new ArgumentException("Elige la moneda entregada."),
                    currency, _primaryCurrency, amount, ReadAmount(RateBox, "tasa real"));
            var window = new BanknoteCountWindow(allowedCurrencies: [currency], nativeCurrency: currency,
                nativeExpected: amount, suggestCurrency: outgoing ? currency : null,
                suggestAmount: outgoing ? amount : null) { Owner = Window.GetWindow(this) };
            if (window.ShowDialog() != true || window.Result is null) return;
            if (outgoing) _outgoing = window.Result; else _incoming = window.Result;
            CountSummary.Text = $"Entregado: {_outgoing?.Summary ?? "—"}  ·  Recibido: {_incoming?.Summary ?? "—"}";
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var from = FromAccount.SelectedItem as AccountOption ?? throw new ArgumentException("Elige una cuenta de origen.");
            var fromCurrency = FromCurrency.SelectedItem as string ?? throw new ArgumentException("Elige la moneda entregada.");
            var toCurrency = ToCurrency.SelectedItem as string ?? throw new ArgumentException("Elige la moneda recibida.");
            var amount = ReadAmount(OutgoingAmount, "importe");
            var rate = ReadAmount(RateBox, "tasa real");
            var received = CurrencyExchangeRepository.CalculateReceivedAmount(
                fromCurrency, toCurrency, _primaryCurrency, amount, rate);
            if (from.Item.Account.Type == AccountType.Cash &&
                (_outgoing is null || _outgoing.Counts.GetValueOrDefault(fromCurrency)?.Sum(note => note.Key * note.Value) != amount))
                throw new InvalidOperationException("Cuenta los billetes entregados para el importe actual.");
            if (from.Item.Account.Type == AccountType.Cash &&
                (_incoming is null || _incoming.Counts.GetValueOrDefault(toCurrency)?.Sum(note => note.Key * note.Value) != received))
                throw new InvalidOperationException("Cuenta los billetes recibidos para el importe y la tasa actuales.");
            _repository.Exchange(from.Item.Account.Id, fromCurrency, toCurrency, amount, rate,
                from.Item.Account.Type == AccountType.Cash ? _outgoing!.Counts[fromCurrency] : new Dictionary<decimal, int>(),
                from.Item.Account.Type == AccountType.Cash ? _incoming!.Counts[toCurrency] : new Dictionary<decimal, int>(), DescriptionBox.Text);
            _outgoing = null; _incoming = null; CountSummary.Text = ""; RefreshList();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ShowError(string message) => MessageBox.Show(Window.GetWindow(this), message, "Cambio de divisas", MessageBoxButton.OK, MessageBoxImage.Warning);
}
