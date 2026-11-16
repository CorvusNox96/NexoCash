using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NexoCash.Infrastructure.Persistence;
using NexoCash.Domain.Entities;

namespace NexoCash.Desktop.Features.Settings;

public partial class CurrencySettingsView : UserControl
{
    private readonly CurrencySettingsRepository _repository = new();
    private readonly DistributionRuleRepository _rules = new();
    private readonly VehicleRentSettingsRepository _rent = new();
    private sealed record RuleAccountOption(ConsolidationAccount Account)
    { public string Label => Account.Name; }
    private bool _loading;

    public CurrencySettingsView()
    {
        InitializeComponent();
        DatabasePicker.DatabaseActivated += (_, path) => ((App)System.Windows.Application.Current).SwitchDatabase(path);
        RefreshAll();
    }

    public void RefreshAll()
    {
        _loading = true;
        try
        {
            var selectedCurrency = DenominationCurrencyPicker.SelectedItem as string;
            RulesGrid.ItemsSource = _rules.List();
            RuleAccountPicker.ItemsSource = new ServiceConsolidationRepository().ListAccounts()
                .Select(account => new RuleAccountOption(account)).ToList();
            RentAccountPicker.ItemsSource = new ServiceConsolidationRepository().ListAccounts()
                .Select(account => new RuleAccountOption(account)).ToList();
            var rentSettings = _rent.Get();
            RentAccountPicker.SelectedItem = RentAccountPicker.Items.Cast<RuleAccountOption>()
                .FirstOrDefault(item => item.Account.Id == rentSettings?.AccountId);
            RentAccountSummary.Text = rentSettings is null ? "Aún no se ha seleccionado una cuenta de alquiler."
                : $"Cuenta guardada: {(RentAccountPicker.SelectedItem as RuleAccountOption)?.Label ?? "Unavailable"} · 9,000 CUP por día trabajado · seguimiento desde {rentSettings.StartedOn:dd/MM/yyyy}";
            var currencies = _repository.ListCurrencies();
            CurrenciesGrid.ItemsSource = currencies;
            var codes = currencies.Select(item => item.Code).ToList();
            FromPicker.ItemsSource = codes;
            ToPicker.ItemsSource = codes;
            DenominationCurrencyPicker.ItemsSource = codes;
            RuleCurrencyPicker.ItemsSource = codes;
            FromPicker.SelectedIndex = codes.Count > 0 ? 0 : -1;
            ToPicker.SelectedIndex = codes.Count > 1 ? 1 : -1;
            DenominationCurrencyPicker.SelectedItem = selectedCurrency is not null && codes.Contains(selectedCurrency)
                ? selectedCurrency : codes.FirstOrDefault();
            RuleCurrencyPicker.SelectedItem = codes.Contains("CUP") ? "CUP" : codes.FirstOrDefault();
            RefreshRates();
            RefreshDenominations();
        }
        finally { _loading = false; }
    }

    private void RefreshDenominations()
    {
        DenominationsGrid.ItemsSource = DenominationCurrencyPicker.SelectedItem is string code
            ? _repository.ListDenominations(code) : null;
    }

    private void RefreshRates()
    {
        var rates = _repository.ListRates();
        var view = CollectionViewSource.GetDefaultView(rates);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ExchangeRateSettingsItem.Day)));
        RatesGrid.ItemsSource = view;
    }

    private void SaveRentAccount_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (RentAccountPicker.SelectedItem is not RuleAccountOption account)
            throw new ArgumentException("Elige una cuenta para el alquiler del vehículo.");
        _rent.SetAccount(account.Account.Id);
        RefreshAll();
    });

    private void RulesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || RulesGrid.SelectedItem is not DistributionRuleItem rule) return;
        RuleNameBox.Text = rule.Name;
        RuleAccountPicker.SelectedItem = RuleAccountPicker.Items.Cast<RuleAccountOption>()
            .FirstOrDefault(item => item.Account.Id == rule.AccountId);
        RuleTypePicker.SelectedIndex = rule.Type == DistributionRuleType.FixedAmount ? 0 : 1;
        RuleValueBox.Text = rule.Value.ToString(CultureInfo.CurrentCulture);
        RuleCurrencyPicker.SelectedItem = rule.Currency ?? "CUP";
        RuleSundayCheck.IsChecked = rule.AppliesOnSundays;
    }

    private void AddRule_Click(object sender, RoutedEventArgs e) => SaveRule(null);
    private void UpdateRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is DistributionRuleItem rule) SaveRule(rule.Id);
    }

    private void SaveRule(Guid? id) => Run(() =>
    {
        if (RuleAccountPicker.SelectedItem is not RuleAccountOption account)
            throw new ArgumentException("Elige una cuenta de destino.");
        var type = RuleTypePicker.SelectedIndex == 0 ? DistributionRuleType.FixedAmount : DistributionRuleType.Percentage;
        _rules.Save(id, RuleNameBox.Text, account.Account.Id, type, 0,
            ReadPositiveDecimal(RuleValueBox.Text), RuleCurrencyPicker.SelectedItem as string,
            RuleSundayCheck.IsChecked == true, false);
        RulesGrid.ItemsSource = _rules.List();
        RuleNameBox.Clear(); RuleValueBox.Clear();
    });

    private void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not DistributionRuleItem rule) return;
        if (!Confirm($"¿Quitar la regla de distribución {rule.Name}?")) return;
        Run(() => { _rules.Delete(rule.Id); RulesGrid.ItemsSource = _rules.List(); RuleNameBox.Clear(); RuleValueBox.Clear(); });
    }

    private static decimal ReadPositiveDecimal(string text)
    {
        if ((!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value)
                && !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value)) || value <= 0)
            throw new ArgumentException("Introduce un valor mayor que cero.");
        return value;
    }

    private string CurrencyFormCode => (CurrencyCodePicker.SelectedItem as ComboBoxItem)?.Content?.ToString()
        ?? throw new ArgumentException("Elige un código de moneda.");

    private void CurrenciesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CurrenciesGrid.SelectedItem is not CurrencySettingsItem item) return;
        CurrencyCodePicker.SelectedItem = CurrencyCodePicker.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(option => option.Content?.ToString() == item.Code);
        CurrencyNameBox.Text = item.Name;
    }

    private void SaveCurrency_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        _repository.SaveCurrency(CurrencyFormCode, CurrencyNameBox.Text);
        RefreshAll();
    });

    private void Primary_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        _repository.SetPrimary(CurrencyFormCode);
        RefreshAll();
    });

    private void RemoveCurrency_Click(object sender, RoutedEventArgs e)
    {
        if (CurrenciesGrid.SelectedItem is not CurrencySettingsItem item) return;
        if (!Confirm($"¿Quitar {item.Code} de las monedas activas? Se conservará su historial.")) return;
        Run(() => { _repository.DeleteCurrency(item.Code); RefreshAll(); });
    }

    private void RatesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || RatesGrid.SelectedItem is not ExchangeRateSettingsItem item) return;
        FromPicker.SelectedItem = item.From;
        ToPicker.SelectedItem = item.To;
        RateBox.Text = item.Value.ToString(CultureInfo.CurrentCulture);
    }

    private void AddRate_Click(object sender, RoutedEventArgs e) => SaveRate(null);
    private void UpdateRate_Click(object sender, RoutedEventArgs e)
    {
        if (RatesGrid.SelectedItem is not ExchangeRateSettingsItem item) return;
        SaveRate(item.Id);
    }
    private void SaveRate(Guid? id) => Run(() =>
    {
        _repository.SaveRate(id, FromPicker.SelectedItem as string ?? "", ToPicker.SelectedItem as string ?? "",
            ReadPositiveDecimal(RateBox.Text));
        RefreshRates();
        RateBox.Clear();
    });

    private void RemoveRate_Click(object sender, RoutedEventArgs e)
    {
        if (RatesGrid.SelectedItem is not ExchangeRateSettingsItem item) return;
        if (!Confirm($"¿Quitar la tasa {item.Pair}?")) return;
        Run(() => { _repository.DeleteRate(item.Id); RefreshRates(); });
    }

    private void DenominationCurrency_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && DenominationsGrid is not null) RefreshDenominations();
    }

    private void DenominationsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DenominationsGrid.SelectedItem is not DenominationSettingsItem item) return;
        DenominationValueBox.Text = item.Value.ToString(CultureInfo.CurrentCulture);
    }

    private void AddDenomination_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (DenominationCurrencyPicker.SelectedItem is not string currency) throw new ArgumentException("Elige una moneda.");
        _repository.AddDenomination(currency, ReadPositiveDecimal(DenominationValueBox.Text));
        RefreshDenominations();
        DenominationValueBox.Clear();
    });

    private void UpdateDenomination_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (DenominationsGrid.SelectedItem is not DenominationSettingsItem item) throw new ArgumentException("Selecciona una denominación.");
        _repository.UpdateDenomination(item.Id, ReadPositiveDecimal(DenominationValueBox.Text));
        RefreshDenominations();
        DenominationValueBox.Clear();
    });

    private void RemoveDenomination_Click(object sender, RoutedEventArgs e)
    {
        if (DenominationsGrid.SelectedItem is not DenominationSettingsItem item) return;
        if (!Confirm($"¿Quitar la denominación {item.ValueText}?")) return;
        Run(() => { _repository.DeleteDenomination(item.Id); RefreshDenominations(); DenominationValueBox.Clear(); });
    }

    private bool Confirm(string message) => MessageBox.Show(Window.GetWindow(this), message, "Settings",
        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "No se pudo guardar la configuración",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
