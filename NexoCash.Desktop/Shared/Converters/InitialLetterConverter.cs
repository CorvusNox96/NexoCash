using System.Globalization;
using System.Windows.Data;

namespace NexoCash.Desktop.Shared.Converters;

public sealed class InitialLetterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string name && !string.IsNullOrWhiteSpace(name)
            ? name.Trim()[0].ToString().ToUpper(culture)
            : "?";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
