using System.Windows;
using System.Windows.Data;
using NexoCash.Infrastructure.Persistence;

namespace NexoCash.Desktop.Features.Agencies;

public partial class AgencyDetailsWindow : Window
{
    private readonly AgencyRepository _repository = new();
    private readonly Guid _agencyId;

    public AgencyDetailsWindow(AgencyListItem item)
    {
        InitializeComponent();
        DataContext = item;
        _agencyId = item.Agency.Id;
        Title = item.Agency.Name;
        LoadServices();
        LoadTopUps();
    }

    private void RefreshAgency()
    {
        var item = _repository.ListActive().FirstOrDefault(value => value.Agency.Id == _agencyId);
        if (item is null) { Close(); return; }
        DataContext = item;
        Title = item.Agency.Name;
        LoadServices();
        LoadTopUps();
    }

    private void LoadTopUps()
    {
        var items = _repository.ListTopUps(_agencyId);
        var view = CollectionViewSource.GetDefaultView(items);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AgencyTopUpItem.Day)));
        TopUpsGrid.ItemsSource = view;
        EmptyTopUpsText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadServices()
    {
        var fromDate = FromPicker.SelectedDate is { } start ? DateOnly.FromDateTime(start) : (DateOnly?)null;
        var toDate = ToPicker.SelectedDate is { } end ? DateOnly.FromDateTime(end) : (DateOnly?)null;
        if (fromDate > toDate)
        {
            MessageBox.Show(this, "La fecha inicial no puede ser posterior a la final.",
                "Filtro de fechas", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var services = _repository.ListServices(_agencyId, fromDate, toDate);
            var view = CollectionViewSource.GetDefaultView(services);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ServiceListItem.Date)));
            ServicesGrid.ItemsSource = view;
            ServicesCountText.Text = services.Count == 1 ? "1 servicio" : $"{services.Count} servicios";
            EmptyText.Visibility = services.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "No se pudieron cargar los servicios",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Filter_Click(object sender, RoutedEventArgs e) => LoadServices();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        FromPicker.SelectedDate = null;
        ToPicker.SelectedDate = null;
        LoadServices();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AgencyListItem item) return;
        var editor = new AgencyEditorWindow(item) { Owner = this };
        if (editor.ShowDialog() == true) RefreshAgency();
    }

    private void TopUp_Click(object sender, RoutedEventArgs e)
    {
        var window = new AgencyTopUpWindow(_agencyId) { Owner = this };
        if (window.ShowDialog() == true) RefreshAgency();
    }
}
