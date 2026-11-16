using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NexoCash.Desktop.Shell;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, FrameworkElement> _sectionActions = new();
    public MainWindow()
    {
        InitializeComponent();
        AttachActions("Accounts", AccountsControl);
        AttachActions("Movements", MovementsControl);
        AttachActions("Services", ServicesControl);
        if (ServicesControl.FindName("HistoryFilters") is StackPanel historyFilters &&
            historyFilters.Parent is Panel filterParent &&
            ServicesControl.FindName("HeaderActions") is StackPanel serviceActions)
        {
            filterParent.Children.Remove(historyFilters);
            serviceActions.Children.Insert(0, historyFilters);
        }
        if (ServicesControl.FindName("ServiceTabs") is TabControl serviceTabs &&
            serviceTabs.Parent is Panel tabsParent)
        {
            tabsParent.Children.Remove(serviceTabs);
            serviceTabs.Margin = new Thickness(0);
            ServiceTabsHost.Children.Add(serviceTabs);
        }
        if (_sectionActions.TryGetValue("Services", out var actions) && actions.Parent is Panel actionsParent)
        {
            actionsParent.Children.Remove(actions);
            Grid.SetColumn(actions, 1);
            actions.VerticalAlignment = VerticalAlignment.Bottom;
            actions.Margin = new Thickness(0, 0, 0, 16);
            ServiceTabsHost.Children.Add(actions);
        }
        AttachActions("Agencies", AgenciesPanel.Child as UserControl);
        AttachActions("Currency exchange", ExchangeControl);
        AttachActions("Workdays", WorkdaysControl);
        if (WorkdaysControl.FindName("WeekSelectorPanel") is FrameworkElement selector)
            selector.Visibility = Visibility.Collapsed;
        if (WorkdaysControl.FindName("ConsolidateButton") is Button consolidate &&
            consolidate.Parent is Panel buttonParent && _sectionActions["Workdays"] is StackPanel weekActions)
        {
            buttonParent.Children.Remove(consolidate);
            consolidate.Margin = new Thickness(12, 0, 0, 0);
            weekActions.Children.Add(consolidate);
        }
        SelectNavigation("Overview");
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string section }) return;
        Navigate(section);
    }

    private void CashRecalculate_Click(object sender, RoutedEventArgs e) => CashControl.Recalculate();
    private void CashReconcile_Click(object sender, RoutedEventArgs e) => CashControl.Reconcile();

    private void AttachActions(string section, UserControl? view)
    {
        if (view?.FindName("HeaderActions") is not FrameworkElement actions ||
            actions.Parent is not Panel parent) return;
        parent.Children.Remove(actions);
        actions.Margin = new Thickness(0);
        HeaderActionsHost.Children.Add(actions);
        _sectionActions[section] = actions;
        actions.Visibility = Visibility.Collapsed;
    }

    private void Navigate(string section)
    {
        SelectNavigation(section);

        PageTitle.Text = section switch
        {
            "Overview" => "Resumen", "Workdays" => "Días trabajados", "Services" => "Servicios",
            "Agencies" => "Agencias", "Accounts" => "Cuentas",
            "Movements" => "Movimientos", "Cash" => "Efectivo",
            "Currency exchange" => "Cambio de divisas", "Settings" => "Configuración", _ => section
        };
        AgenciesPanel.Visibility = section == "Agencies" ? Visibility.Visible : Visibility.Collapsed;
        ServicesPanel.Visibility = section == "Services" ? Visibility.Visible : Visibility.Collapsed;
        ServiceTabsHost.Visibility = section == "Services" ? Visibility.Visible : Visibility.Collapsed;
        AccountsPanel.Visibility = section == "Accounts" ? Visibility.Visible : Visibility.Collapsed;
        if (section == "Accounts") AccountsControl.RefreshList();
        MovementsPanel.Visibility = section == "Movements" ? Visibility.Visible : Visibility.Collapsed;
        if (section == "Movements") MovementsControl.RefreshList();
        SettingsPanel.Visibility = section == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        if (section == "Settings") SettingsControl.RefreshAll();
        WorkdaysPanel.Visibility = section == "Workdays" ? Visibility.Visible : Visibility.Collapsed;
        if (section == "Workdays") WorkdaysControl.RefreshList();
        CashPanel.Visibility = section == "Cash" ? Visibility.Visible : Visibility.Collapsed;
        CashActions.Visibility = section == "Cash" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (actionSection, actions) in _sectionActions)
            actions.Visibility = actionSection == section ? Visibility.Visible : Visibility.Collapsed;
        if (section == "Cash") CashControl.RefreshList();
        ExchangePanel.Visibility = section == "Currency exchange" ? Visibility.Visible : Visibility.Collapsed;
        if (section == "Currency exchange") ExchangeControl.RefreshList();
        OverviewPanel.Visibility = section == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        if (section == "Overview") OverviewControl.Refresh();
        PageSubtitle.Text = section switch
        {
            "Overview" => "Tu trabajo y dinero de un vistazo.",
            "Agencies" => "Administra tus agencias, comisiones y fondos en un solo lugar.",
            "Services" => "Sigue cada viaje desde la reserva hasta su finalización.",
            "Accounts" => "Administra cuentas y saldos en todas las monedas.",
            "Movements" => "Revisa y registra el dinero que entra, sale o se mueve entre cuentas.",
            "Settings" => "Administra el alquiler del vehículo, monedas, tasas y denominaciones de billetes.",
            "Workdays" => "Consulta tus días trabajados y el saldo del alquiler semanal.",
            "Cash" => "Consulta los billetes físicos y concilia un conteo de efectivo.",
            "Currency exchange" => "Cambia monedas usando la tasa real y los billetes contados.",
            _ => "Esta sección está lista para una próxima etapa."
        };
        PageSubtitle.Visibility = Visibility.Visible;
        SectionLabel.Visibility = Visibility.Visible;
    }

    private void SelectNavigation(string section)
    {
        foreach (var button in NavigationButtons.Children.OfType<Button>())
        {
            var selected = button.Tag as string == section;
            button.Background = selected ? new SolidColorBrush(Color.FromRgb(40, 104, 199)) : Brushes.Transparent;
            button.Foreground = selected ? Brushes.White : new SolidColorBrush(Color.FromRgb(198, 213, 226));
        }
    }
}
