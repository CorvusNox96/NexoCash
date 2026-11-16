using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NexoCash.Infrastructure.Persistence;

namespace NexoCash.Desktop.Features.Agencies;

public partial class AgenciesView : UserControl
{
    private readonly AgencyRepository _repository = new();

    public AgenciesView()
    {
        InitializeComponent();
        RefreshList();
    }

    private void RefreshList(Guid? selectId = null)
    {
        var agencies = _repository.ListActive();
        AgenciesGrid.ItemsSource = agencies;
        if (selectId is { } id)
            AgenciesGrid.SelectedItem = agencies.FirstOrDefault(a => a.Agency.Id == id);
    }

    private void New_Click(object sender, RoutedEventArgs e) => OpenEditor(null);

    private void AgenciesGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        for (var element = e.OriginalSource as DependencyObject;
             element is not null;
             element = VisualTreeHelper.GetParent(element))
        {
            if (element is not DataGridRow row) continue;
            if (row.Item is AgencyListItem item)
            {
                new AgencyDetailsWindow(item) { Owner = Window.GetWindow(this) }.ShowDialog();
                RefreshList(item.Agency.Id);
            }
            break;
        }
    }

    private void OpenEditor(AgencyListItem? item)
    {
        var editor = new AgencyEditorWindow(item) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true) RefreshList(editor.SavedAgencyId);
    }

}
