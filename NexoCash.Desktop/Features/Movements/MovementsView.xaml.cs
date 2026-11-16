using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NexoCash.Infrastructure.Persistence;

namespace NexoCash.Desktop.Features.Movements;

public partial class MovementsView : UserControl
{
    private readonly AccountRepository _repository = new();
    public MovementsView() { InitializeComponent(); RefreshList(); }

    public void RefreshList()
    {
        var movements = _repository.ListMovements();
        var view = CollectionViewSource.GetDefaultView(movements);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AccountActivityItem.Day)));
        MovementsGrid.ItemsSource = view;
        CountText.Text = movements.Count == 1 ? "1 movimiento" : $"{movements.Count} movimientos";
        EmptyText.Visibility = movements.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Deposit_Click(object sender, RoutedEventArgs e) => OpenEditor(MovementEntryKind.Deposit);
    private void Withdraw_Click(object sender, RoutedEventArgs e) => OpenEditor(MovementEntryKind.Withdrawal);
    private void Transfer_Click(object sender, RoutedEventArgs e) => OpenEditor(MovementEntryKind.Transfer);

    private void OpenEditor(MovementEntryKind kind)
    {
        var editor = new MovementEditorWindow(kind) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true) RefreshList();
    }
}
