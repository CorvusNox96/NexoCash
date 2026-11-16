using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using NexoCash.Infrastructure.Persistence;

namespace NexoCash.Desktop.Features.Services;

public partial class ServicesView : UserControl
{
    private readonly ServiceRepository _repository = new();
    private bool _showHistory;
    private bool _calendarForFrom;

    public ServicesView()
    {
        InitializeComponent();
        RefreshList();
    }

    public void RefreshList(Guid? selectId = null)
    {
        var services = _repository.ListAll(_showHistory)
            .Where(service => !_showHistory ||
                (HistoryFrom.SelectedDate is null || service.Date >= DateOnly.FromDateTime(HistoryFrom.SelectedDate.Value)) &&
                (HistoryTo.SelectedDate is null || service.Date <= DateOnly.FromDateTime(HistoryTo.SelectedDate.Value)))
            .ToList();
        DateColumn.Visibility = _showHistory ? Visibility.Collapsed : Visibility.Visible;
        var view = CollectionViewSource.GetDefaultView(services);
        if (_showHistory) view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ServiceOverview.Date)));
        ServicesGrid.ItemsSource = view;
        if (selectId is { } id)
            ServicesGrid.SelectedItem = services.FirstOrDefault(service => service.Id == id);
        CountText.Text = services.Count == 1 ? "1 servicio" : $"{services.Count} servicios";
        EmptyText.Visibility = services.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var editor = new ServiceEditorWindow { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true) RefreshList();
    }

    private void ServicesGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var onRow = false;
        for (var source = e.OriginalSource as DependencyObject; source is not null;
             source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
        {
            if (source is DataGridRow) { onRow = true; break; }
            if (source is DataGridColumnHeader or DataGrid) return;
        }
        if (!onRow) return;
        if (ServicesGrid.SelectedItem is not ServiceOverview service) return;
        try
        {
            var editor = new ServiceEditorWindow(service.Id) { Owner = Window.GetWindow(this) };
            if (editor.ShowDialog() == true) RefreshList(service.Id);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ShowError(string message) => MessageBox.Show(Window.GetWindow(this), message,
        "Servicio", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void ServiceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || e.Source != ServiceTabs) return;
        _showHistory = ServiceTabs.SelectedIndex == 1;
        HistoryFilters.Visibility = _showHistory ? Visibility.Visible : Visibility.Collapsed;
        RefreshList();
    }

    private void HistoryDate_Changed(object sender, SelectionChangedEventArgs e)
    {
        HistoryFromText.Text = HistoryFrom.SelectedDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";
        HistoryToText.Text = HistoryTo.SelectedDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";
        if (IsLoaded && _showHistory) RefreshList();
    }

    private void HistoryText_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box) return;
        var picker = box == HistoryFromText ? HistoryFrom : HistoryTo;
        if (string.IsNullOrWhiteSpace(box.Text)) picker.SelectedDate = null;
        else if (DateTime.TryParseExact(box.Text.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture,
                     DateTimeStyles.None, out var date)) picker.SelectedDate = date;
        else box.Text = picker.SelectedDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";
    }

    private void HistoryCalendar_Click(object sender, RoutedEventArgs e)
    {
        _calendarForFrom = ReferenceEquals(sender, HistoryFromCalendarButton);
        HistoryCalendarPopup.PlacementTarget = (UIElement)sender;
        HistoryCalendar.DisplayDate = (_calendarForFrom ? HistoryFrom : HistoryTo).SelectedDate ?? DateTime.Today;
        HistoryCalendarPopup.IsOpen = true;
    }

    private void HistoryCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!HistoryCalendarPopup.IsOpen || HistoryCalendar.SelectedDate is not { } date) return;
        (_calendarForFrom ? HistoryFrom : HistoryTo).SelectedDate = date;
        HistoryCalendarPopup.IsOpen = false;
    }

    private void Consolidate_Click(object sender, RoutedEventArgs e)
    {
        var date = ServicesGrid.SelectedItem is ServiceOverview service
            ? service.Date : DateOnly.FromDateTime(DateTime.Today);
        var window = new ServiceConsolidationWindow(date) { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() == true) RefreshList();
    }

}
