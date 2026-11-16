using System.Globalization;
using Microsoft.Data.Sqlite;
using NexoCash.Domain.Entities;

namespace NexoCash.Infrastructure.Persistence;

public sealed record ServiceOverview(
    Guid Id, DateOnly Date, string Recipient,
    string Type, string Status, decimal Amount, string CurrencyCode,
    string PaymentMethod, Guid? ConsolidationId = null, bool StartsConsolidation = false,
    bool StartsDate = false, bool EndsDate = false)
{
    public string DateText => Date.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);
    public string AmountText => $"{Amount:N0} {CurrencyCode}";
}

public sealed class ServiceRepository
{
    private readonly string _databasePath;

    public ServiceRepository(string? databasePath = null) =>
        _databasePath = databasePath ?? DatabaseInitializer.DatabasePath;

    public (Service Service, DateOnly Date, bool Consolidated) GetDetail(Guid id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.Type, s.Status, s.PaymentMethod, s.AgencyId, s.CurrencyCode,
                   s.ExpectedPrice, s.AppliedCommissionPercent, w.Date,
                   EXISTS(SELECT 1 FROM ServiceConsolidationItems i WHERE i.ServiceId = s.Id)
            FROM Services s JOIN Workdays w ON w.Id = s.WorkdayId WHERE s.Id = $id
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException("El servicio ya no está disponible.");
        var service = new Service
        {
            Id = id, Type = (ServiceType)reader.GetInt32(0), Status = (ServiceStatus)reader.GetInt32(1),
            PaymentMethod = (PaymentMethod)reader.GetInt32(2),
            AgencyId = reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)),
            CurrencyCode = reader.GetString(4),
            ExpectedPrice = decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
            AppliedCommissionPercent = reader.IsDBNull(6) ? null : decimal.Parse(reader.GetString(6), CultureInfo.InvariantCulture)
        };
        return (service, DateOnly.Parse(reader.GetString(7), CultureInfo.InvariantCulture), reader.GetInt32(8) != 0);
    }

    public void UpdatePending(Service service, DateOnly date)
    {
        var detail = GetDetail(service.Id);
        if (detail.Service.Status != ServiceStatus.Open || detail.Consolidated)
            throw new InvalidOperationException("Solo se pueden editar servicios abiertos y pendientes.");
        service.Status = ServiceStatus.Open;
        Validate(service, date);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var currency = connection.CreateCommand())
        {
            currency.Transaction = transaction;
            currency.CommandText = "SELECT 1 FROM Currencies WHERE Code = $code AND IsActive = 1";
            currency.Parameters.AddWithValue("$code", service.CurrencyCode);
            if (currency.ExecuteScalar() is null) throw new InvalidOperationException("Choose an active currency.");
        }
        if (service.AgencyId is { } agencyId)
        {
            using var agency = connection.CreateCommand(); agency.Transaction = transaction;
            agency.CommandText = "SELECT 1 FROM Agencies WHERE Id = $id AND IsActive = 1";
            agency.Parameters.AddWithValue("$id", agencyId.ToString());
            if (agency.ExecuteScalar() is null) throw new InvalidOperationException("Choose an active agency.");
        }
        var dateText = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using (var ensureDay = connection.CreateCommand())
        {
            ensureDay.Transaction = transaction;
            ensureDay.CommandText = "INSERT OR IGNORE INTO Workdays (Id, Date, Status, WasWorked) VALUES ($id, $date, 0, NULL)";
            ensureDay.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            ensureDay.Parameters.AddWithValue("$date", dateText);
            ensureDay.ExecuteNonQuery();
        }
        string workdayId;
        using (var day = connection.CreateCommand())
        {
            day.Transaction = transaction;
            day.CommandText = "SELECT Id, Status FROM Workdays WHERE Date = $date";
            day.Parameters.AddWithValue("$date", dateText);
            using var reader = day.ExecuteReader();
            if (!reader.Read() || reader.GetInt32(1) != 0)
                throw new InvalidOperationException("The destination workday is closed.");
            workdayId = reader.GetString(0);
        }
        var commission = service.Type == ServiceType.Agency
            ? decimal.Round(service.ExpectedPrice!.Value * service.AppliedCommissionPercent!.Value / 100m,
                2, MidpointRounding.AwayFromZero) : (decimal?)null;
        using var update = connection.CreateCommand(); update.Transaction = transaction;
        update.CommandText = """
            UPDATE Services SET WorkdayId = $workday, Type = $type, PaymentMethod = $payment,
                AgencyId = $agency, CurrencyCode = $currency,
                ExpectedPrice = $amount, AppliedCommissionPercent = $percent, CommissionAmount = $commission
            WHERE Id = $id AND Status = 0 AND NOT EXISTS
                (SELECT 1 FROM ServiceConsolidationItems WHERE ServiceId = $id)
            """;
        update.Parameters.AddWithValue("$workday", workdayId);
        update.Parameters.AddWithValue("$type", (int)service.Type);
        update.Parameters.AddWithValue("$payment", (int)service.PaymentMethod);
        update.Parameters.AddWithValue("$agency", service.AgencyId?.ToString() ?? (object)DBNull.Value);
        update.Parameters.AddWithValue("$currency", service.CurrencyCode);
        update.Parameters.AddWithValue("$amount", service.ExpectedPrice!.Value.ToString(CultureInfo.InvariantCulture));
        update.Parameters.AddWithValue("$percent", service.AppliedCommissionPercent is { } percent
            ? percent.ToString(CultureInfo.InvariantCulture) : DBNull.Value);
        update.Parameters.AddWithValue("$commission", commission is { } charge
            ? charge.ToString(CultureInfo.InvariantCulture) : DBNull.Value);
        update.Parameters.AddWithValue("$id", service.Id.ToString());
        if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException("The service changed. Refresh and try again.");
        transaction.Commit();
    }

    public void DeletePending(Guid id)
    {
        using var connection = OpenConnection(); using var transaction = connection.BeginTransaction();
        using var fees = connection.CreateCommand(); fees.Transaction = transaction;
        fees.CommandText = "DELETE FROM AgencyFundMovements WHERE ServiceId = $id AND Type = 1 AND NOT EXISTS (SELECT 1 FROM ServiceConsolidationItems WHERE ServiceId = $id)";
        fees.Parameters.AddWithValue("$id", id.ToString());
        fees.ExecuteNonQuery();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM Services WHERE Id = $id
            AND NOT EXISTS (SELECT 1 FROM ServiceConsolidationItems WHERE ServiceId = $id)
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Solo se pueden eliminar servicios pendientes.");
        transaction.Commit();
    }

    public void AdvanceStatus(Guid serviceId)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        ServiceStatus currentStatus;
        ServiceType type;
        Guid? agencyId;
        string currencyCode;
        decimal amount;
        decimal commissionAmount;
        using (var load = connection.CreateCommand())
        {
            load.Transaction = transaction;
            load.CommandText = """
                SELECT s.Status, s.Type, s.WorkdayId, s.AgencyId,
                       s.CurrencyCode, s.ExpectedPrice, s.CommissionAmount, w.Status
                FROM Services s JOIN Workdays w ON w.Id = s.WorkdayId
                WHERE s.Id = $id
                """;
            load.Parameters.AddWithValue("$id", serviceId.ToString());
            using var reader = load.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("The service is no longer available.");
            currentStatus = (ServiceStatus)reader.GetInt32(0);
            if (currentStatus == ServiceStatus.Finished)
                throw new InvalidOperationException("This service is already finished.");
            if (reader.GetInt32(7) != (int)WorkdayStatus.Open)
                throw new InvalidOperationException("A service on a closed workday cannot be changed.");
            type = (ServiceType)reader.GetInt32(1);
            agencyId = reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3));
            currencyCode = reader.GetString(4);
            amount = decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture);
            commissionAmount = reader.IsDBNull(6) ? 0m : decimal.Parse(reader.GetString(6), CultureInfo.InvariantCulture);
        }

        var nextStatus = ServiceStatus.Finished;
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Services SET Status = $status,
                    CollectedAmount = $collected, CompletedAt = $completed
                WHERE Id = $id AND Status = $current
                """;
            update.Parameters.AddWithValue("$status", (int)nextStatus);
            update.Parameters.AddWithValue("$collected", amount.ToString(CultureInfo.InvariantCulture));
            update.Parameters.AddWithValue("$completed", DateTimeOffset.UtcNow.ToString("O"));
            update.Parameters.AddWithValue("$id", serviceId.ToString());
            update.Parameters.AddWithValue("$current", (int)currentStatus);
            if (update.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("The service status changed. Refresh and try again.");
        }

        if (nextStatus == ServiceStatus.Finished)
        {
            if (type == ServiceType.Agency && agencyId is { } selectedAgency && commissionAmount > 0)
            {
                using var charge = connection.CreateCommand();
                charge.Transaction = transaction;
                charge.CommandText = """
                    INSERT INTO AgencyFundMovements
                        (Id, AgencyId, ServiceId, CurrencyCode, Amount, Type, OccurredAt, Description)
                    VALUES ($id, $agency, $service, $currency, $amount, $type, $at, $description)
                    """;
                charge.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
                charge.Parameters.AddWithValue("$agency", selectedAgency.ToString());
                charge.Parameters.AddWithValue("$service", serviceId.ToString());
                charge.Parameters.AddWithValue("$currency", currencyCode);
                charge.Parameters.AddWithValue("$amount", (-commissionAmount).ToString(CultureInfo.InvariantCulture));
                charge.Parameters.AddWithValue("$type", (int)AgencyFundMovementType.ServiceFee);
                charge.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
                charge.Parameters.AddWithValue("$description", "Service commission");
                charge.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }

    public IReadOnlyList<string> ListCurrencies()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Code FROM Currencies WHERE IsActive = 1 ORDER BY IsPrimaryCurrency DESC, Code";
        using var reader = command.ExecuteReader();
        var currencies = new List<string>();
        while (reader.Read()) currencies.Add(reader.GetString(0));
        return currencies;
    }

    public IReadOnlyList<ServiceOverview> ListAll(bool consolidated = false)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.Id, w.Date, s.Type,
                   COALESCE(a.Name, ''), s.Status,
                   s.ExpectedPrice, s.CurrencyCode, s.PaymentMethod,
                   ci.ConsolidationId
            FROM Services s
            JOIN Workdays w ON w.Id = s.WorkdayId
            LEFT JOIN Agencies a ON a.Id = s.AgencyId
            LEFT JOIN ServiceConsolidationItems ci ON ci.ServiceId = s.Id
            LEFT JOIN ServiceConsolidations sc ON sc.Id = ci.ConsolidationId
            WHERE (ci.ServiceId IS NOT NULL) = $consolidated
            ORDER BY w.Date DESC, s.CreatedAt DESC
            """;
        command.Parameters.AddWithValue("$consolidated", consolidated ? 1 : 0);
        using var reader = command.ExecuteReader();
        var services = new List<ServiceOverview>();
        while (reader.Read())
        {
            var type = (ServiceType)reader.GetInt32(2);
            var recipient = type switch
            {
                ServiceType.OneOffCustomer => "Servicio Cliente",
                ServiceType.StreetPickup => "Servicio Directo",
                _ => reader.GetString(3)
            };
            services.Add(new ServiceOverview(
                Guid.Parse(reader.GetString(0)),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                recipient,
                TypeLabel(type),
                StatusLabel((ServiceStatus)reader.GetInt32(4)),
                decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                reader.GetString(6),
                (PaymentMethod)reader.GetInt32(7) == PaymentMethod.Cash ? "Cash" : "Transfer",
                reader.IsDBNull(8) ? null : Guid.Parse(reader.GetString(8)),
                consolidated && services.Count > 0 &&
                    services[^1].ConsolidationId != Guid.Parse(reader.GetString(8)),
                consolidated && services.Count > 0 &&
                    services[^1].Date != DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }
        if (consolidated)
            for (var index = 0; index < services.Count; index++)
                services[index] = services[index] with
                {
                    StartsDate = index == 0 || services[index - 1].Date != services[index].Date,
                    EndsDate = index == services.Count - 1 || services[index + 1].Date != services[index].Date
                };
        return services;
    }

    public void Create(Service service, DateOnly date)
    {
        Validate(service, date);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var dateText = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using (var ensureWorkday = connection.CreateCommand())
        {
            ensureWorkday.Transaction = transaction;
            ensureWorkday.CommandText = """
                INSERT OR IGNORE INTO Workdays (Id, Date, Status, WasWorked)
                VALUES ($id, $date, 0, NULL)
                """;
            ensureWorkday.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            ensureWorkday.Parameters.AddWithValue("$date", dateText);
            ensureWorkday.ExecuteNonQuery();
        }

        using (var workday = connection.CreateCommand())
        {
            workday.Transaction = transaction;
            workday.CommandText = "SELECT Id, Status FROM Workdays WHERE Date = $date";
            workday.Parameters.AddWithValue("$date", dateText);
            using var reader = workday.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("The workday could not be opened.");
            if (reader.GetInt32(1) != (int)WorkdayStatus.Open)
                throw new InvalidOperationException("Services cannot be added to a closed workday.");
            service.WorkdayId = Guid.Parse(reader.GetString(0));
        }

        if (service.Type == ServiceType.Agency)
        {
            using var agency = connection.CreateCommand();
            agency.Transaction = transaction;
            agency.CommandText = "SELECT IsActive FROM Agencies WHERE Id = $id";
            agency.Parameters.AddWithValue("$id", service.AgencyId!.Value.ToString());
            if (Convert.ToInt32(agency.ExecuteScalar()) != 1)
                throw new InvalidOperationException("The selected agency is not available.");
        }

        var amount = service.ExpectedPrice!.Value;
        service.CollectedAmount = null;
        service.CompletedAt = null;
        service.CommissionAmount = service.Type == ServiceType.Agency
            ? decimal.Round(amount * service.AppliedCommissionPercent!.Value / 100m, 2,
                MidpointRounding.AwayFromZero)
            : null;

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO Services (
                    Id, WorkdayId, Type, Status, PaymentMethod, AgencyId,
                    CurrencyCode, ExpectedPrice, CollectedAmount,
                    AppliedCommissionPercent, CommissionAmount,
                    CreatedAt, CompletedAt, Description)
                VALUES (
                    $id, $workday, $type, $status, $payment, $agency,
                    $currency, $price, $collected, $percent, $commission,
                    $created, $completed, $description)
                """;
            insert.Parameters.AddWithValue("$id", service.Id.ToString());
            insert.Parameters.AddWithValue("$workday", service.WorkdayId.ToString());
            insert.Parameters.AddWithValue("$type", (int)service.Type);
            insert.Parameters.AddWithValue("$status", (int)service.Status);
            insert.Parameters.AddWithValue("$payment", (int)service.PaymentMethod);
            insert.Parameters.AddWithValue("$agency", service.AgencyId?.ToString() ?? (object)DBNull.Value);
            insert.Parameters.AddWithValue("$currency", service.CurrencyCode);
            insert.Parameters.AddWithValue("$price", amount.ToString(CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$collected", service.CollectedAmount is { } collected
                ? collected.ToString(CultureInfo.InvariantCulture) : DBNull.Value);
            insert.Parameters.AddWithValue("$percent", service.AppliedCommissionPercent is { } percent
                ? percent.ToString(CultureInfo.InvariantCulture) : DBNull.Value);
            insert.Parameters.AddWithValue("$commission", service.CommissionAmount is { } commission
                ? commission.ToString(CultureInfo.InvariantCulture) : DBNull.Value);
            insert.Parameters.AddWithValue("$created", service.CreatedAt.ToString("O"));
            insert.Parameters.AddWithValue("$completed", service.CompletedAt?.ToString("O") ?? (object)DBNull.Value);
            insert.Parameters.AddWithValue("$description", (object?)service.Description ?? DBNull.Value);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private void Validate(Service service, DateOnly date)
    {
        if (date == default) throw new ArgumentException("Choose a service date.");
        if (DatabaseInitializer.GetStartDate(_databasePath) is { } start && date < start)
            throw new ArgumentException("La fecha del servicio es anterior al inicio de esta base de datos.");
        if (service.Status != ServiceStatus.Open)
            throw new ArgumentException("Los servicios nuevos deben estar abiertos.");
        if (service.ExpectedPrice is not > 0)
            throw new ArgumentException("The service amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(service.CurrencyCode))
            throw new ArgumentException("Choose a currency.");
        if (!Enum.IsDefined(service.Type) || !Enum.IsDefined(service.Status)
            || !Enum.IsDefined(service.PaymentMethod))
            throw new ArgumentException("Choose valid service options.");
        if (service.Type == ServiceType.Agency)
        {
            if (service.AgencyId is null)
                throw new ArgumentException("Choose the agency for this service.");
            if (service.AppliedCommissionPercent is < 0 or > 100 or null)
                throw new ArgumentException("Enter a commission percentage from 0 to 100.");
        }
        else if (service.AgencyId is not null)
            throw new ArgumentException("Elige un tipo de servicio válido sin agencia.");
    }

    private static string TypeLabel(ServiceType type) => type switch
    {
        ServiceType.Agency => "Servicio Agencia",
        ServiceType.OneOffCustomer => "Servicio Cliente",
        ServiceType.StreetPickup => "Servicio Directo",
        _ => "Unknown"
    };

    private static string StatusLabel(ServiceStatus status) => status switch
    {
        ServiceStatus.Open => "Abierto",
        ServiceStatus.Finished => "Finalizado",
        _ => "Unknown"
    };

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true
        }.ToString());
        connection.Open();
        return connection;
    }
}
