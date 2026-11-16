using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using System.ComponentModel;
using System.Windows.Controls.Primitives;
using NexoCash.Desktop.Shared.Windows;
using NexoCash.Infrastructure.Persistence;

namespace NexoCash.Desktop.Features.Cash;

public partial class CashView : UserControl
{
    private readonly CashRepository _repository = new();
    private readonly Dictionary<string, List<EditableBanknote>> _rows = new();

    public CashView() { InitializeComponent(); RefreshList(); }

    public void RefreshList()
    {
        var inventory = _repository.ListInventory();
        var comparisons = _repository.ListBalanceComparisons();
        ComparisonCards.ItemsSource = comparisons;
        CurrencySections.Children.Clear();
        _rows.Clear();
        foreach (var comparison in comparisons.OrderBy(item => item.Currency == "CUP" ? 0 : item.Currency == "USD" ? 1 : 2))
        {
            var rows = inventory.Where(item => item.Currency == comparison.Currency)
                .Select(item => new EditableBanknote(item)).ToList();
            _rows[comparison.Currency] = rows;
            var equations = new ItemsControl
            {
                ItemsSource = rows,
                ItemTemplate = (DataTemplate)Resources["BanknoteEquation"],
                ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(UniformGrid)))
            };
            ((FrameworkElementFactory)equations.ItemsPanel.VisualTree).SetValue(UniformGrid.ColumnsProperty, 3);
            var content = new ScrollViewer
            {
                Content = equations,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(14, 14, 4, 0)
            };
            var section = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
            section.Children.Add(new TextBlock
            {
                Text = $"Billetes {comparison.Currency}", FontSize = 16,
                FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.DarkSlateGray,
                Margin = new Thickness(14, 0, 0, 8)
            });
            section.Children.Add(content);
            CurrencySections.Children.Add(section);
        }
    }

    public void Recalculate()
    {
        Focus();
        try
        {
            var counts = new Dictionary<string, IReadOnlyDictionary<decimal, int>>();
            foreach (var (currency, rows) in _rows)
            {
                var notes = new Dictionary<decimal, int>();
                foreach (var row in rows)
                {
                    if (!int.TryParse(row.QuantityText.Trim(), NumberStyles.None,
                            CultureInfo.InvariantCulture, out var quantity) || quantity < 0)
                        throw new InvalidOperationException($"Introduce una cantidad entera no negativa para {row.Value:N0} {currency}.");
                    if (quantity > 0) notes.Add(row.Value, quantity);
                }
                counts[currency] = notes;
            }
            _repository.Reconcile(counts);
            RefreshList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "No se pudieron recalcular los billetes",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public sealed class EditableBanknote(CashInventoryItem item) : INotifyPropertyChanged
    {
        public decimal Value => item.Value;
        public string ValueText => $"{Value:N0} {item.Currency}";
        public string AmountText => int.TryParse(QuantityText, NumberStyles.None,
            CultureInfo.InvariantCulture, out var quantity)
            ? $"{Value * quantity:N0} {item.Currency}" : "—";
        private string _quantityText = item.Quantity.ToString(CultureInfo.InvariantCulture);
        public string QuantityText
        {
            get => _quantityText;
            set
            {
                _quantityText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AmountText)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public void Reconcile()
    {
        var expected = _repository.ListBalanceComparisons().ToDictionary(item => item.Currency, item => item.CashBalance);
        var window = new BanknoteCountWindow(allowedCurrencies: expected.Keys.ToList(),
            allowEmpty: true, allowMissingRates: true, expectedAmounts: expected, enforceExpectedAmounts: false)
            { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() != true || window.Result is null) return;
        try
        {
            _repository.Reconcile(window.Result.Counts);
            RefreshList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "No se pudo guardar el conteo",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
