using System.Windows;

namespace NexoCash.Desktop.Features.Settings;

public partial class DatabaseSetupWindow : Window
{
    public DatabaseSetupWindow()
    {
        InitializeComponent();
        Picker.DatabaseActivated += (_, path) =>
        {
            DatabasePath = path;
            DialogResult = true;
        };
    }

    public string? DatabasePath { get; private set; }
}
