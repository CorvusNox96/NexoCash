using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using NexoCash.Infrastructure.Persistence;

namespace NexoCash.Desktop.Shared.Windows;

public sealed record BanknoteCountResult(decimal TotalCup, string Summary,
    IReadOnlyDictionary<string, IReadOnlyDictionary<decimal, int>> Counts);

public partial class BanknoteCountWindow : Window
{
    private readonly CurrencySettingsRepository _settings = new();
    private readonly CashValuationRepository _valuation = new();
    private readonly decimal? _expectedCup;
    private readonly bool _allowEmpty;
    private readonly bool _allowMissingRates;
    private readonly IReadOnlyDictionary<string, decimal>? _expectedAmounts;
    private readonly bool _enforceExpectedAmounts;
    private readonly Dictionary<string, (TextBlock Counted, TextBlock Difference, Border Card)> _cards = [];
    private readonly string? _nativeCurrency;
    private readonly decimal? _nativeExpected;
    private readonly Dictionary<string, List<(decimal Value, TextBox Quantity, TextBlock LineTotal)>> _inputs = [];

    public BanknoteCountResult? Result { get; private set; }

    public BanknoteCountWindow(decimal? expectedCup = null, IReadOnlyCollection<string>? allowedCurrencies = null,
        bool allowEmpty = false, string? nativeCurrency = null, decimal? nativeExpected = null,
        string? suggestCurrency = null, decimal? suggestAmount = null, bool allowMissingRates = false,
        IReadOnlyDictionary<string, decimal>? expectedAmounts = null, bool enforceExpectedAmounts = true)
    {
        InitializeComponent();
        _expectedCup = expectedCup;
        _allowEmpty = allowEmpty;
        _allowMissingRates = allowMissingRates;
        _expectedAmounts = expectedAmounts;
        _enforceExpectedAmounts = enforceExpectedAmounts;
        _nativeCurrency = nativeCurrency;
        _nativeExpected = nativeExpected;
        ExpectedText.Text = expectedAmounts is not null
            ? "Saldo esperado: " + string.Join(" · ", expectedAmounts.Select(pair => $"{pair.Value:N2} {pair.Key}"))
            : nativeCurrency is not null && nativeExpected is { } native
            ? $"Importe esperado: {native:N2} {nativeCurrency}"
            : expectedCup is { } expected
            ? $"Importe esperado: {expected:N2} CUP" : "Efectivo contado en todas las monedas";
        var sections = new StackPanel();
        foreach (var currency in _settings.ListCurrencies().OrderBy(item => item.Code == "CUP" ? 0 : item.Code == "USD" ? 1 : 2))
        {
            if (allowedCurrencies is not null && !allowedCurrencies.Contains(currency.Code)) continue;
            var denominations = _settings.ListDenominations(currency.Code);
            if (denominations.Count == 0) continue;
            var rows = new List<(decimal, TextBox, TextBlock)>();
            _inputs[currency.Code] = rows;
            var card = new Border { Background = new SolidColorBrush(Color.FromRgb(238, 244, 252)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(175, 199, 229)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 10, 0) };
            var cardLines = new StackPanel();
            cardLines.Children.Add(new TextBlock { Text = currency.Code, FontWeight = FontWeights.SemiBold, FontSize = 14 });
            var target = _expectedAmounts?.GetValueOrDefault(currency.Code)
                ?? (currency.Code == _nativeCurrency ? _nativeExpected : null);
            cardLines.Children.Add(new TextBlock { Text = target is { } amount
                ? $"Saldo objetivo: {amount:N0} {currency.Code}" : "Saldo objetivo: —",
                Margin = new Thickness(0, 4, 0, 0) });
            var countedText = new TextBlock();
            var differenceText = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, FontWeight = FontWeights.SemiBold };
            cardLines.Children.Add(countedText);
            cardLines.Children.Add(differenceText);
            card.Child = cardLines;
            CurrencyCards.Children.Add(card);
            _cards[currency.Code] = (countedText, differenceText, card);
            var panel = new StackPanel { Margin = new Thickness(4, 8, 4, 10) };
            panel.Children.Add(new TextBlock { Text = $"Billetes {currency.Code}", FontSize = 16,
                FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(24, 51, 69)),
                Margin = new Thickness(9, 0, 0, 8) });
            var columns = new UniformGrid { Columns = 3 };
            panel.Children.Add(columns);
            foreach (var denomination in denominations.OrderBy(item => item.Value))
            {
                var row = new Grid { Margin = new Thickness(9, 5, 9, 5) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var valueText = new TextBlock
                {
                    Text = $"{denomination.Value:N0} {currency.Code}",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(24, 51, 69))
                };
                Grid.SetColumn(valueText, 0);
                row.Children.Add(valueText);
                row.Children.Add(new TextBlock { Text = "×", Foreground = Brushes.SlateGray,
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
                Grid.SetColumn(row.Children[^1], 1);
                var quantityBox = new TextBox { Text = "0", Style = (Style)Resources["QuantityBox"],
                    Width = 58, Height = 32, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0),
                    ToolTip = null };
                Grid.SetColumn(quantityBox, 2);
                row.Children.Add(quantityBox);
                row.Children.Add(new TextBlock { Text = "=", Foreground = Brushes.SlateGray,
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
                Grid.SetColumn(row.Children[^1], 3);
                var lineTotal = new TextBlock { Text = $"0 {currency.Code}", Foreground = new SolidColorBrush(Color.FromRgb(40, 104, 199)),
                    FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right, FontWeight = FontWeights.SemiBold };
                Grid.SetColumn(lineTotal, 4);
                row.Children.Add(lineTotal);
                rows.Add((denomination.Value, quantityBox, lineTotal));
                quantityBox.GotKeyboardFocus += (_, _) => quantityBox.SelectAll();
                quantityBox.PreviewMouseLeftButtonDown += (_, e) =>
                {
                    if (quantityBox.IsKeyboardFocusWithin) return;
                    e.Handled = true;
                    quantityBox.Focus();
                };
                quantityBox.PreviewKeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        e.Handled = true;
                        quantityBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    }
                    else if (e.Key is Key.Up or Key.Down)
                    {
                        e.Handled = true;
                        int.TryParse(quantityBox.Text, out var current);
                        if (e.Key == Key.Up && current < int.MaxValue) current++;
                        if (e.Key == Key.Down && current > 0) current--;
                        quantityBox.Text = current.ToString(CultureInfo.InvariantCulture);
                        quantityBox.SelectAll();
                    }
                };
                quantityBox.TextChanged += (_, _) => RefreshTotals();
                columns.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(247, 250, 254)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(175, 199, 229)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 9, 7),
                    CornerRadius = new CornerRadius(7), Child = row
                });
            }
            sections.Children.Add(panel);
        }
        SingleCurrencyHost.Content = sections;
        if (suggestCurrency is not null && suggestAmount is { } requested && _inputs.TryGetValue(suggestCurrency, out var suggestedInputs))
        {
            var suggested = new CashRepository().SuggestBanknotes(suggestCurrency, requested);
            foreach (var (value, box, _) in suggestedInputs)
                if (suggested.TryGetValue(value, out var quantity)) box.Text = quantity.ToString(CultureInfo.InvariantCulture);
        }
        RefreshTotals();
    }

    private void RefreshTotals()
    {
        if (TotalCupText is null) return;
        try
        {
            var counts = ReadCounts();
            var amounts = counts.ToDictionary(pair => pair.Key,
                pair => pair.Value.Sum(note => note.Key * note.Value));
            UpdateMetricCards(amounts);
            foreach (var (currency, metric) in _cards)
            {
                var counted = amounts.GetValueOrDefault(currency);
                var target = _expectedAmounts?.GetValueOrDefault(currency)
                    ?? (currency == _nativeCurrency ? _nativeExpected : null);
                metric.Counted.Text = $"Billetes contados: {counted:N0} {currency}";
                metric.Difference.Text = target is { } expectedAmount
                    ? $"{counted - expectedAmount:+#,##0;-#,##0;0} {currency}" : "";
                metric.Difference.Foreground = target == counted ? Brushes.SeaGreen : Brushes.Firebrick;
                metric.Card.Background = target is null || target == counted
                    ? new SolidColorBrush(Color.FromRgb(238, 244, 252)) : new SolidColorBrush(Color.FromRgb(255, 241, 241));
            }
            if (SingleCurrencySummary.Visibility == Visibility.Visible)
            {
                var currency = _inputs.Keys.Single();
                SingleCurrencyTotalText.Text = $"{amounts.GetValueOrDefault(currency):N2} {currency}";
            }
            decimal? cup = null;
            try { cup = _valuation.ToCup(amounts); }
            catch (InvalidOperationException) when (_nativeCurrency is not null || _allowMissingRates) { }
            TotalCupText.Text = cup is { } converted ? $"{converted:N2} CUP" : "No hay tasa de referencia en CUP";
            CurrencyTotalsText.Text = _expectedAmounts is not null
                ? string.Join(" · ", _expectedAmounts.Select(pair =>
                    $"{pair.Key}: {amounts.GetValueOrDefault(pair.Key) - pair.Value:+#,##0.00;-#,##0.00;0.00}"))
                : _nativeCurrency is not null && _nativeExpected is { } native
                ? $"Diferencia: {amounts.GetValueOrDefault(_nativeCurrency) - native:+#,##0.00;-#,##0.00;0.00} {_nativeCurrency}"
                : _expectedCup is { } expected
                ? $"Diferencia: {cup - expected:+#,##0.00;-#,##0.00;0.00} CUP"
                : amounts.Count == 0 ? "Introduce cantidades para ver el total." : "Los totales incluyen todas las monedas contadas.";
            CurrencyTotalsText.Foreground = (_expectedAmounts is not null && _expectedAmounts.Any(pair =>
                    amounts.GetValueOrDefault(pair.Key) != pair.Value))
                || (_nativeCurrency is not null && _nativeExpected is { } nativeValue
                    && amounts.GetValueOrDefault(_nativeCurrency) != nativeValue)
                || (_expectedCup is { } expectedValue && cup != expectedValue)
                ? new SolidColorBrush(Color.FromRgb(180, 64, 64))
                : new SolidColorBrush(Color.FromRgb(22, 130, 103));
            ErrorText.Text = "";
        }
        catch (Exception ex)
        {
            TotalCupText.Text = "— CUP";
            CurrencyTotalsText.Text = "Comprueba las cantidades o las tasas de cambio.";
            ErrorText.Text = ex.Message;
        }
    }

    private void UpdateMetricCards(IReadOnlyDictionary<string, decimal> amounts)
    {
        CurrencyMetricsText.Text = string.Join(" · ", _inputs.Keys.Select(currency =>
            $"{currency}: {amounts.GetValueOrDefault(currency):N2}"));
        foreach (var (currency, entries) in _inputs)
            foreach (var (value, quantityBox, lineTotal) in entries)
                lineTotal.Text = int.TryParse(quantityBox.Text.Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var quantity) && quantity >= 0
                    ? $"{value * quantity:N0} {currency}" : "—";
    }

    private Dictionary<string, IReadOnlyDictionary<decimal, int>> ReadCounts()
    {
        var counts = new Dictionary<string, IReadOnlyDictionary<decimal, int>>();
        foreach (var (currency, entries) in _inputs)
        {
            var notes = new Dictionary<decimal, int>();
            foreach (var (value, quantityBox, _) in entries)
            {
                var text = quantityBox.Text.Trim();
                if (text.Length == 0) continue;
                if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity < 0)
                    throw new ArgumentException($"Introduce una cantidad entera no negativa para {value} {currency}.");
                if (quantity > 0) notes.Add(value, quantity);
            }
            if (notes.Count > 0) counts.Add(currency, notes);
        }
        return counts;
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var counts = ReadCounts();
            if (counts.Count == 0 && !_allowEmpty) throw new InvalidOperationException("Introduce al menos un billete.");
            var amounts = counts.ToDictionary(pair => pair.Key,
                pair => pair.Value.Sum(note => note.Key * note.Value));
            if (_enforceExpectedAmounts && _expectedAmounts is not null && (_expectedAmounts.Any(pair =>
                    amounts.GetValueOrDefault(pair.Key) != pair.Value) ||
                amounts.Any(pair => !_expectedAmounts.ContainsKey(pair.Key))))
                throw new InvalidOperationException("El conteo debe coincidir con el saldo de caja en cada moneda.");
            decimal cup;
            try { cup = _valuation.ToCup(amounts); }
            catch (InvalidOperationException) when (_nativeCurrency is not null || _allowMissingRates) { cup = 0; }
            var summary = string.Join(" | ", counts.Select(pair =>
                $"{pair.Key}: {string.Join(", ", pair.Value.Select(note => $"{note.Value} × {note.Key:N2}"))}"));
            Result = new(cup, summary, counts);
            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

