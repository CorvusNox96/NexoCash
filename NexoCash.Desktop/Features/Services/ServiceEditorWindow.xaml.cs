using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NexoCash.Domain.Entities;
using NexoCash.Infrastructure.Persistence;

namespace NexoCash.Desktop.Features.Services;

public sealed record Choice<T>(string Label, T Value);
public sealed record PersonChoice(string Label, Guid Id, decimal? CommissionRate = null);

public partial class ServiceEditorWindow : Window
{
    private readonly ServiceRepository _repository = new();
    private readonly Guid? _editingId;
    private bool _readOnly;

    public ServiceEditorWindow(Guid? editingId = null)
    {
        _editingId = editingId;
        InitializeComponent();
        DatePicker.DisplayDateStart = DatabaseInitializer.StartDate.ToDateTime(TimeOnly.MinValue);
        DateCalendar.DisplayDateStart = DatePicker.DisplayDateStart;
        DatePicker.SelectedDate = DatePicker.DisplayDateStart > DateTime.Today
            ? DatePicker.DisplayDateStart : DateTime.Today;
        var types = new List<Choice<ServiceType>>
        {
            new Choice<ServiceType>("Servicio Agencia", ServiceType.Agency),
            new Choice<ServiceType>("Servicio Cliente", ServiceType.OneOffCustomer),
            new Choice<ServiceType>("Servicio Directo", ServiceType.StreetPickup)
        };
        TypeCombo.ItemsSource = types;
        TypeCombo.SelectedIndex = 0;
        var agencyRepository = new AgencyRepository();
        var agencyChoices = agencyRepository.ListActive()
            .Select(x => new PersonChoice(x.Agency.Name, x.Agency.Id, x.Agency.CurrentCommissionPercent))
            .ToList();
        AgencyCombo.ItemsSource = agencyChoices;
        CurrencyCombo.ItemsSource = _repository.ListCurrencies();
        CurrencyCombo.SelectedIndex = 0;
        PaymentCombo.ItemsSource = new[]
        {
            new Choice<PaymentMethod>("Cash", PaymentMethod.Cash),
            new Choice<PaymentMethod>("Transfer", PaymentMethod.Transfer)
        };
        PaymentCombo.SelectedIndex = 0;
        if (editingId is { } id)
        {
            var (service, date, consolidated) = _repository.GetDetail(id);
            if (service.AgencyId is { } selectedAgency && agencyChoices.All(item => item.Id != selectedAgency) &&
                agencyRepository.GetSelection(selectedAgency) is { } historicalAgency)
            {
                agencyChoices.Add(new PersonChoice(historicalAgency.Name, selectedAgency, historicalAgency.CommissionRate));
                AgencyCombo.ItemsSource = null;
                AgencyCombo.ItemsSource = agencyChoices;
            }
            _readOnly = consolidated || service.Status == ServiceStatus.Finished;
            Title = consolidated ? "Servicio consolidado" : _readOnly ? "Servicio finalizado" : "Detalle del servicio";
            EditorTitle.Text = Title;
            EditorSubtitle.Text = consolidated ? "Consulta del servicio consolidado." : _readOnly
                ? "Este servicio ya está finalizado." : "Edita, finaliza o elimina el servicio.";
            StatusHint.Text = consolidated ? "Estado: Finalizado · Consolidado" :
                service.Status == ServiceStatus.Finished ? "Estado: Finalizado" : "Estado: Abierto";
            DatePicker.SelectedDate = date.ToDateTime(TimeOnly.MinValue);
            TypeCombo.SelectedItem = TypeCombo.Items.Cast<Choice<ServiceType>>().First(item => item.Value == service.Type);
            AgencyCombo.SelectedItem = AgencyCombo.Items.Cast<PersonChoice>().FirstOrDefault(item => item.Id == service.AgencyId);
            CommissionBox.Text = service.AppliedCommissionPercent?.ToString(CultureInfo.CurrentCulture) ?? "";
            AmountBox.Text = service.ExpectedPrice?.ToString(CultureInfo.CurrentCulture) ?? "";
            CurrencyCombo.SelectedItem = service.CurrencyCode;
            PaymentCombo.SelectedItem = PaymentCombo.Items.Cast<Choice<PaymentMethod>>().First(item => item.Value == service.PaymentMethod);
            DeleteButton.Visibility = consolidated ? Visibility.Collapsed : Visibility.Visible;
            FinishButton.Visibility = _readOnly ? Visibility.Collapsed : Visibility.Visible;
            SaveButton.Visibility = _readOnly ? Visibility.Collapsed : Visibility.Visible;
            CancelButton.Content = _readOnly ? "Cerrar" : "Cancelar";
            DateText.IsReadOnly = _readOnly;
            CalendarButton.IsEnabled = !_readOnly;
            TypeCombo.IsEnabled = !_readOnly;
            AgencyCombo.IsEnabled = !_readOnly;
            CommissionBox.IsReadOnly = _readOnly;
            AmountBox.IsReadOnly = _readOnly;
            CurrencyCombo.IsEnabled = !_readOnly;
            PaymentCombo.IsEnabled = !_readOnly;
        }
    }

    private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var type = (TypeCombo.SelectedItem as Choice<ServiceType>)?.Value;
        AgencyPanel.Visibility = type == ServiceType.Agency ? Visibility.Visible : Visibility.Collapsed;
        Height = type == ServiceType.Agency ? 560 : 480;
    }

    private void DatePicker_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (DateText is not null)
            DateText.Text = DatePicker.SelectedDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";
    }

    private void DateText_LostFocus(object sender, RoutedEventArgs e) => ApplyTypedDate();

    private void DateText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyTypedDate();
        e.Handled = true;
    }

    private void ApplyTypedDate()
    {
        if (DateTime.TryParseExact(DateText.Text.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date)) DatePicker.SelectedDate = date;
        else DateText.Text = DatePicker.SelectedDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";
    }

    private void CalendarButton_Click(object sender, RoutedEventArgs e)
    {
        DateCalendar.DisplayDate = DatePicker.SelectedDate ?? DateTime.Today;
        DatePopup.IsOpen = true;
    }

    private void DateCalendar_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!DatePopup.IsOpen || DateCalendar.SelectedDate is not { } date) return;
        DatePicker.SelectedDate = date;
        DatePopup.IsOpen = false;
    }

    private void AgencyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AgencyCombo.SelectedItem is PersonChoice agency)
            CommissionBox.Text = (agency.CommissionRate ?? 0m).ToString(CultureInfo.CurrentCulture);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        ErrorText.Text = "";
        try
        {
            SaveCurrent();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void SaveCurrent()
    {
            if (DatePicker.SelectedDate is not { } selectedDate)
                throw new ArgumentException("Elige una fecha de servicio.");
            if (DateOnly.FromDateTime(selectedDate) < DatabaseInitializer.StartDate)
                throw new ArgumentException("La fecha del servicio es anterior al inicio de esta base de datos.");
            if (!TryReadDecimal(AmountBox.Text, out var amount) || amount <= 0)
                throw new ArgumentException("Introduce un importe de servicio mayor que cero.");
            if (TypeCombo.SelectedItem is not Choice<ServiceType> type ||
                PaymentCombo.SelectedItem is not Choice<PaymentMethod> payment ||
                CurrencyCombo.SelectedItem is not string currency)
                throw new ArgumentException("Completa las opciones del servicio.");

            decimal? commission = null;
            Guid? agencyId = null;
            if (type.Value == ServiceType.Agency)
            {
                if (AgencyCombo.SelectedItem is not PersonChoice agency)
                    throw new ArgumentException("Elige una agencia.");
                if (!TryReadDecimal(CommissionBox.Text, out var rate) || rate is < 0 or > 100)
                    throw new ArgumentException("Introduce un porcentaje de comisión entre 0 y 100.");
                agencyId = agency.Id;
                commission = rate;
            }

            var service = new Service
            {
                Type = type.Value,
                Id = _editingId ?? Guid.NewGuid(),
                Status = ServiceStatus.Open,
                PaymentMethod = payment.Value,
                AgencyId = agencyId,
                CurrencyCode = currency,
                ExpectedPrice = amount,
                AppliedCommissionPercent = commission
            };
            if (_editingId is null) _repository.Create(service, DateOnly.FromDateTime(selectedDate));
            else _repository.UpdatePending(service, DateOnly.FromDateTime(selectedDate));
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (_editingId is not { } id || _readOnly) return;
        if (MessageBox.Show(this, "¿Finalizar este servicio? Después no podrás editarlo.",
                "Finalizar servicio", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { SaveCurrent(); _repository.AdvanceStatus(id); DialogResult = true; }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_editingId is not { } id || DeleteButton.Visibility != Visibility.Visible) return;
        if (MessageBox.Show(this, "¿Eliminar este servicio pendiente?", "Eliminar servicio",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { _repository.DeletePending(id); DialogResult = true; }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }

    private static bool TryReadDecimal(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
        || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}
