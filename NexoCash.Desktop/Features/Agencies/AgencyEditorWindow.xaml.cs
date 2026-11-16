using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NexoCash.Domain.Entities;
using NexoCash.Infrastructure.Persistence;

namespace NexoCash.Desktop.Features.Agencies;

public partial class AgencyEditorWindow : Window
{
    private readonly AgencyRepository _repository = new();
    private readonly Guid? _selectedId;
    private byte[]? _photo;

    public Guid? SavedAgencyId { get; private set; }

    public AgencyEditorWindow(AgencyListItem? item)
    {
        InitializeComponent();
        if (item is null) return;

        _selectedId = item.Agency.Id;
        _photo = item.Agency.Photo;
        Title = "Editar agencia";
        FormTitle.Text = Title;
        NameBox.Text = item.Agency.Name;
        NumberBox.Text = item.Agency.AdministrationNumber ?? "";
        PercentageBox.Text = (item.Agency.CurrentCommissionPercent ?? 0).ToString(CultureInfo.CurrentCulture);
        PhotoPreview.Source = ToImage(_photo);
    }

    private void ChoosePhoto_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Elegir foto de la agencia",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var file = new FileInfo(dialog.FileName);
            if (file.Length > 5 * 1024 * 1024)
                throw new InvalidOperationException("La foto debe tener 5 MB o menos.");
            var bytes = File.ReadAllBytes(dialog.FileName);
            var preview = ToImage(bytes) ?? throw new InvalidOperationException("No se pudo abrir la imagen.");
            _photo = bytes;
            PhotoPreview.Source = preview;
            ErrorText.Text = "";
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void RemovePhoto_Click(object sender, RoutedEventArgs e)
    {
        _photo = null;
        PhotoPreview.Source = null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        try
        {
            if (!TryReadDecimal(PercentageBox.Text, out var percentage) || percentage is < 0 or > 100)
                throw new ArgumentException("Introduce un porcentaje entre 0 y 100.");

            var agency = new Agency
            {
                Id = _selectedId ?? Guid.NewGuid(),
                Name = NameBox.Text.Trim(),
                AdministrationNumber = string.IsNullOrWhiteSpace(NumberBox.Text) ? null : NumberBox.Text.Trim(),
                Photo = _photo,
                CurrentCommissionPercent = percentage
            };
            if (_selectedId is null) _repository.Create(agency, 0m);
            else _repository.UpdateDetails(agency);
            SavedAgencyId = agency.Id;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static bool TryReadDecimal(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
        || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static BitmapImage? ToImage(byte[]? bytes)
    {
        if (bytes is null) return null;
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
