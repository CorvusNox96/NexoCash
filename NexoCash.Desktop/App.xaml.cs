using System.Windows;
using System.Globalization;
using System.Threading;
using NexoCash.Infrastructure.Persistence;
using NexoCash.Desktop.Shell;
using NexoCash.Desktop.Features.Settings;

namespace NexoCash.Desktop;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var culture = CultureInfo.GetCultureInfo("es-CU");
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        try
        {
            var selected = DatabaseInitializer.LoadSelectedPath();
            if (selected is not null)
            {
                try
                {
                    DatabaseInitializer.Initialize(selected);
                    if (DatabaseInitializer.GetStartDate(selected) is not null)
                    {
                        DatabaseInitializer.Activate(selected);
                        ShowMainWindow();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"No se pudo abrir la base asignada. Elige otra base o crea una nueva.\n\n{ex.Message}",
                        "Base de datos", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var setup = new DatabaseSetupWindow();
            if (setup.ShowDialog() != true || setup.DatabasePath is null)
            {
                Shutdown();
                return;
            }
            DatabaseInitializer.Activate(setup.DatabasePath);
            ShowMainWindow();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"NexoCash no pudo abrir la base de datos.\n\n{ex.Message}",
                "Error al iniciar NexoCash", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void ShowMainWindow()
    {
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }

    public void SwitchDatabase(string path)
    {
        var previous = DatabaseInitializer.DatabasePath;
        var oldWindow = MainWindow;
        try
        {
            DatabaseInitializer.Activate(path);
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
            oldWindow?.Close();
        }
        catch
        {
            DatabaseInitializer.Activate(previous);
            throw;
        }
    }
}
