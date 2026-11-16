using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NexoCash.Infrastructure.Persistence;

namespace NexoCash.Desktop.Features.Settings;

public partial class DatabasePickerView : UserControl
{
    private string? _path;
    private bool _createNew;
    public event EventHandler<string>? DatabaseActivated;

    public DatabasePickerView()
    {
        InitializeComponent();
        StartDatePicker.DisplayDateEnd = DateTime.Today;
        StartDatePicker.SelectedDate = DateTime.Today;
        var selected = DatabaseInitializer.LoadSelectedPath();
        if (selected is not null) SelectPath(selected, false);
        else if (File.Exists(DatabaseInitializer.LegacyPath)) SelectPath(DatabaseInitializer.LegacyPath, false);
        else DateHintText.Text = "Elige una fecha. No se podrán registrar días anteriores.";
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Crear base de datos de NexoCash", Filter = "Base de datos de NexoCash (*.db)|*.db",
            DefaultExt = ".db", AddExtension = true, OverwritePrompt = false,
            FileName = "NexoCash.db"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) SelectPath(dialog.FileName, true);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Elegir base de datos de NexoCash", Filter = "Base de datos (*.db)|*.db|Todos los archivos (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) SelectPath(dialog.FileName, false);
    }

    private void SelectPath(string path, bool createNew)
    {
        try
        {
            _path = Path.GetFullPath(path);
            _createNew = createNew;
            PathText.Text = _path;
            ErrorText.Text = "";
            var fixedDate = !createNew && File.Exists(_path) ? DatabaseInitializer.GetStartDate(_path) : null;
            StartDatePicker.IsEnabled = fixedDate is null;
            StartDatePicker.SelectedDate = (fixedDate ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue);
            DateHintText.Text = fixedDate is { } start
                ? $"Fecha fijada para este archivo: {start:dd/MM/yyyy}. No se puede cambiar."
                : "La fecha quedará fija al activar esta base de datos por primera vez.";
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_path is null) throw new InvalidOperationException("Elige o crea un archivo de base de datos.");
            if (_createNew && File.Exists(_path))
                throw new InvalidOperationException("El archivo ya existe. Usa «Elegir archivo» para abrirlo.");
            if (!_createNew && !File.Exists(_path))
                throw new InvalidOperationException("No se encuentra el archivo elegido.");
            if (StartDatePicker.SelectedDate is not { } selected)
                throw new InvalidOperationException("Elige la fecha de inicio.");
            DatabaseInitializer.Initialize(_path);
            if (DatabaseInitializer.GetStartDate(_path) is null)
                DatabaseInitializer.SetStartDate(_path, DateOnly.FromDateTime(selected));
            DatabaseActivated?.Invoke(this, _path);
            ErrorText.Text = "";
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
}
