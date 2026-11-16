using System.Globalization;
using System.Windows.Data;

namespace NexoCash.Desktop.Shared.Converters;

public sealed class SpanishDisplayConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["New"] = "Nuevo", ["Confirmed"] = "Confirmado", ["Finished"] = "Terminado",
        ["Open"] = "Abierto", ["Closed"] = "Cerrado", ["Unspecified"] = "Sin especificar",
        ["Pending"] = "Pendiente", ["Worked"] = "Trabajado", ["No work"] = "Sin trabajo",
        ["Agency"] = "Agencia", ["Fixed customer"] = "Cliente fijo",
        ["One-off customer"] = "Cliente ocasional", ["Street pickup"] = "Recogida en la calle",
        ["Cash"] = "Efectivo", ["Virtual"] = "Virtual", ["Transfer"] = "Transferencia",
        ["Deposit"] = "Ingreso", ["Income"] = "Ingreso", ["Withdrawal"] = "Retiro", ["Exchange"] = "Cambio",
        ["Currency exchange"] = "Cambio de divisas", ["Cash adjustment"] = "Ajuste de efectivo",
        ["External"] = "Externo", ["No route specified"] = "Sin ruta especificada",
        ["Adjustment"] = "Ajuste", ["Primary"] = "Principal", ["Secondary"] = "Secundaria"
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string text && Labels.TryGetValue(text, out var label) ? label : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value;
}
