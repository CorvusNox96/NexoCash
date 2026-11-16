using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NexoCash.Infrastructure.Persistence;

namespace NexoCash.Desktop.Features.Accounts;

public sealed record AccountGridRow(AccountListItem Item, IReadOnlyDictionary<string, string> CurrencyBalances,
    string TotalCup)
{
    public string Name => Item.Account.Name;
    public string TypeLabel => Item.TypeLabel;
}

public partial class AccountsView : UserControl
{
    private readonly AccountRepository _repository = new();
    private readonly CashValuationRepository _valuation = new();
    public AccountsView() { InitializeComponent(); RefreshList(); }

    public void RefreshList(Guid? selectId = null)
    {
        var accounts = _repository.ListActive();
        var currencies = _repository.ListCurrencies();
        while (AccountsGrid.Columns.Count > 3) AccountsGrid.Columns.RemoveAt(2);
        foreach (var currency in currencies)
            AccountsGrid.Columns.Insert(AccountsGrid.Columns.Count - 1, new DataGridTextColumn
            {
                Header = currency,
                Binding = new Binding($"CurrencyBalances[{currency}]"),
                ElementStyle = (Style)Resources["TextStyle"],
                Width = new DataGridLength(105)
            });
        var rows = accounts.Select(account => CreateRow(account, currencies)).ToList();
        AccountsGrid.ItemsSource = rows;
        CountText.Text = accounts.Count == 1 ? "1 cuenta" : $"{accounts.Count} cuentas";
        EmptyText.Visibility = accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (selectId is { } id) AccountsGrid.SelectedItem = rows.FirstOrDefault(a => a.Item.Account.Id == id);
    }

    private AccountGridRow CreateRow(AccountListItem account, IReadOnlyList<string> currencies)
    {
        var amounts = currencies.ToDictionary(currency => currency,
            currency => account.Balances.FirstOrDefault(balance => balance.Currency == currency)?.Amount ?? 0m);
        var labels = amounts.ToDictionary(pair => pair.Key, pair => pair.Value.ToString("N2"));
        string total;
        try
        {
            var cup = amounts.Sum(pair => Math.Sign(pair.Value) * _valuation.ToCup(
                new Dictionary<string, decimal> { [pair.Key] = Math.Abs(pair.Value) }));
            total = $"{cup:N2} CUP";
        }
        catch (InvalidOperationException) { total = "—"; }
        return new AccountGridRow(account, labels, total);
    }

    private void AccountsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = AccountsGrid.SelectedItem is AccountGridRow;
        EditButton.IsEnabled = selected;
        DeleteButton.IsEnabled = selected;
    }

    private void New_Click(object sender, RoutedEventArgs e) => OpenEditor(null);
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (AccountsGrid.SelectedItem is AccountGridRow item) OpenEditor(item.Item);
    }
    private void OpenEditor(AccountListItem? item)
    {
        var window = new AccountEditorWindow(item) { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() == true) RefreshList(window.SavedId);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (AccountsGrid.SelectedItem is not AccountGridRow row) return;
        if (MessageBox.Show(Window.GetWindow(this), "¿Quitar esta cuenta? Se conservará su historial de movimientos.",
                "Eliminar cuenta", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { _repository.Delete(row.Item.Account.Id); RefreshList(); }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "No se pudo eliminar la cuenta", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
