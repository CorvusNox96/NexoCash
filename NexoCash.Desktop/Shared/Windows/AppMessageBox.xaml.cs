using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NexoCash.Desktop.Shared.Windows;

public static class AppMessageBox
{
    public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons,
        MessageBoxImage image) => Show(System.Windows.Application.Current?.MainWindow, message, title, buttons, image);

    public static MessageBoxResult Show(Window? owner, string message, string title,
        MessageBoxButton buttons, MessageBoxImage image)
    {
        var dialog = new AppMessageBoxWindow(message, title, buttons);
        if (owner is { IsLoaded: true } && owner != dialog) dialog.Owner = owner;
        dialog.ShowDialog();
        return dialog.Result;
    }
}

public partial class AppMessageBoxWindow : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    public AppMessageBoxWindow(string message, string title, MessageBoxButton buttons)
    {
        InitializeComponent();
        Title = title;
        HeadingText.Text = title;
        MessageText.Text = message;
        switch (buttons)
        {
            case MessageBoxButton.YesNo:
                AddButton("No", MessageBoxResult.No);
                AddButton("Sí", MessageBoxResult.Yes, true);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("Cancelar", MessageBoxResult.Cancel);
                AddButton("No", MessageBoxResult.No);
                AddButton("Sí", MessageBoxResult.Yes, true);
                break;
            case MessageBoxButton.OKCancel:
                AddButton("Cancelar", MessageBoxResult.Cancel);
                AddButton("Aceptar", MessageBoxResult.OK, true);
                break;
            default:
                AddButton("Aceptar", MessageBoxResult.OK, true);
                break;
        }
    }

    private void AddButton(string label, MessageBoxResult result, bool primary = false)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 86,
            Padding = new Thickness(16, 9, 16, 9),
            Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(primary ? "#2868C7" : "#E8F0FB")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(primary ? "#FFFFFF" : "#244B80")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(primary ? "#2868C7" : "#CDDCF1")),
            BorderThickness = new Thickness(1)
        };
        button.Click += (_, _) => { Result = result; DialogResult = true; };
        ButtonsPanel.Children.Add(button);
    }
}
