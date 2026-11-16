using System.Globalization;
using Microsoft.Data.Sqlite;
using NexoCash.Domain.Entities;

namespace NexoCash.Infrastructure.Persistence;

public sealed record AgencyListItem(Agency Agency, decimal CupFund);
public sealed record AgencyTopUpItem(DateTimeOffset OccurredAt, decimal Amount, string Description,
    string? TransactionNumber, string? AccountName, decimal? SurchargePercent, decimal? DebitedAmount)
{
    public DateOnly Day => DateOnly.FromDateTime(OccurredAt.ToLocalTime().DateTime);
    public string TimeText => OccurredAt.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
    public string AmountText => $"{Amount:N0} CUP";
    public string ConceptText => Description == "Initial fund" ? "Fondo inicial" : Description;
    public string TransactionText => TransactionNumber ?? "—";
    public string AccountText => AccountName ?? "—";
    public string SurchargeText => SurchargePercent is { } percent ? $"{percent:N2} %" : "—";
    public string DebitedText => DebitedAmount is { } amount ? $"{amount:N0} CUP" : "—";
}
public sealed class AgencyRepository
{
    public IReadOnlyList<AgencyTopUpItem> ListTopUps(Guid agencyId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT f.OccurredAt, f.Amount, f.Description, f.TransactionNumber,
                   a.Name, f.SurchargePercent, m.Amount
            FROM AgencyFundMovements f
            LEFT JOIN Movements m ON m.Id = f.AccountMovementId
            LEFT JOIN Accounts a ON a.Id = m.AccountId
            WHERE f.AgencyId = $id AND f.CurrencyCode = 'CUP' AND f.Type = $type
            ORDER BY f.OccurredAt DESC
            """;
        command.Parameters.AddWithValue("$id", agencyId.ToString());
        command.Parameters.AddWithValue("$type", (int)AgencyFundMovementType.TopUp);
        using var reader = command.ExecuteReader();
        var result = new List<AgencyTopUpItem>();
        while (reader.Read()) result.Add(new(DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
            ParseDecimal(reader.GetString(1)), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : ParseDecimal(reader.GetString(5)),
            reader.IsDBNull(6) ? null : Math.Abs(ParseDecimal(reader.GetString(6)))));
        return result;
    }

    public void TopUp(Guid agencyId, Guid accountId, string transactionNumber, decimal amount,
        decimal surchargePercent, IReadOnlyDictionary<decimal, int> banknotes)
    {
        if (amount <= 0) throw new ArgumentException("La recarga debe ser mayor que cero.");
        if (string.IsNullOrWhiteSpace(transactionNumber))
            throw new ArgumentException("Introduce el número de transacción.");
        transactionNumber = transactionNumber.Trim();
        if (transactionNumber.Length > 80) throw new ArgumentException("El número de transacción es demasiado largo.");
        if (surchargePercent is < 0 or > 100)
            throw new ArgumentException("El porcentaje debe estar entre 0 y 100.");
        if (banknotes.Any(note => note.Key <= 0 || note.Value <= 0))
            throw new ArgumentException("El conteo de billetes no es válido.");
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "SELECT 1 FROM Agencies WHERE Id = $id AND IsActive = 1";
        check.Parameters.AddWithValue("$id", agencyId.ToString());
        if (check.ExecuteScalar() is null) throw new InvalidOperationException("La agencia ya no está disponible.");
        using var duplicate = connection.CreateCommand();
        duplicate.Transaction = transaction;
        duplicate.CommandText = "SELECT 1 FROM AgencyFundMovements WHERE AgencyId = $agency AND TransactionNumber = $number";
        duplicate.Parameters.AddWithValue("$agency", agencyId.ToString());
        duplicate.Parameters.AddWithValue("$number", transactionNumber);
        if (duplicate.ExecuteScalar() is not null)
            throw new InvalidOperationException("Ese número de transacción ya está registrado para la agencia.");
        using var account = connection.CreateCommand();
        account.Transaction = transaction;
        account.CommandText = "SELECT Type FROM Accounts WHERE Id = $id AND IsActive = 1";
        account.Parameters.AddWithValue("$id", accountId.ToString());
        var accountType = account.ExecuteScalar();
        if (accountType is null) throw new InvalidOperationException("Elige una cuenta activa.");
        var isCash = Convert.ToInt32(accountType) == (int)AccountType.Cash;
        if (isCash && surchargePercent != 0)
            throw new InvalidOperationException("El porcentaje adicional solo se aplica a transferencias.");
        if (!isCash && banknotes.Count != 0)
            throw new InvalidOperationException("Solo se cuentan billetes de cuentas de efectivo.");
        var debit = decimal.Round(amount * (1m + surchargePercent / 100m), 2, MidpointRounding.AwayFromZero);
        if (isCash && banknotes.Count > 0 && banknotes.Sum(note => note.Key * note.Value) != debit)
            throw new InvalidOperationException("El conteo debe coincidir con el retiro de la cuenta.");
        using var balance = connection.CreateCommand();
        balance.Transaction = transaction;
        balance.CommandText = "SELECT Amount FROM Movements WHERE AccountId = $id AND CurrencyCode = 'CUP'";
        balance.Parameters.AddWithValue("$id", accountId.ToString());
        using (var reader = balance.ExecuteReader())
        {
            decimal available = 0;
            while (reader.Read()) available += ParseDecimal(reader.GetString(0));
            if (available < debit) throw new InvalidOperationException("La cuenta no tiene saldo suficiente en CUP.");
        }
        foreach (var note in banknotes)
            if (AccountRepository.GetBanknoteQuantity(connection, transaction, "CUP", note.Key) < note.Value)
                throw new InvalidOperationException($"No hay suficientes billetes de {note.Key:N0} CUP.");
        var movementId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("O");
        using var movement = connection.CreateCommand();
        movement.Transaction = transaction;
        movement.CommandText = """
            INSERT INTO Movements (Id, AccountId, CurrencyCode, Amount, Type, OccurredAt, RecordedAt, OperationId, Description)
            VALUES ($id, $account, 'CUP', $amount, 1, $at, $at, $operation, $description)
            """;
        movement.Parameters.AddWithValue("$id", movementId.ToString());
        movement.Parameters.AddWithValue("$account", accountId.ToString());
        movement.Parameters.AddWithValue("$amount", (-debit).ToString(CultureInfo.InvariantCulture));
        movement.Parameters.AddWithValue("$at", now);
        movement.Parameters.AddWithValue("$operation", transactionNumber);
        movement.Parameters.AddWithValue("$description", $"Recarga de agencia · {transactionNumber}");
        movement.ExecuteNonQuery();
        foreach (var note in banknotes)
        {
            using var detail = connection.CreateCommand();
            detail.Transaction = transaction;
            detail.CommandText = """
                INSERT INTO BanknoteMovementDetails (Id, MovementId, DenominationId, Quantity)
                VALUES ($id, $movement, $denomination, $quantity)
                """;
            detail.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            detail.Parameters.AddWithValue("$movement", movementId.ToString());
            detail.Parameters.AddWithValue("$denomination", AccountRepository.GetActiveDenomination(connection, transaction, "CUP", note.Key));
            detail.Parameters.AddWithValue("$quantity", note.Value);
            detail.ExecuteNonQuery();
        }
        using var fund = connection.CreateCommand();
        fund.Transaction = transaction;
        fund.CommandText = """
            INSERT INTO AgencyFundMovements
                (Id, AgencyId, AccountMovementId, CurrencyCode, Amount, Type, OccurredAt, Description, TransactionNumber, SurchargePercent)
            VALUES ($id, $agency, $movement, 'CUP', $amount, $type, $at, 'Recarga de fondo', $number, $percent)
            """;
        fund.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        fund.Parameters.AddWithValue("$agency", agencyId.ToString());
        fund.Parameters.AddWithValue("$movement", movementId.ToString());
        fund.Parameters.AddWithValue("$amount", amount.ToString(CultureInfo.InvariantCulture));
        fund.Parameters.AddWithValue("$type", (int)AgencyFundMovementType.TopUp);
        fund.Parameters.AddWithValue("$at", now);
        fund.Parameters.AddWithValue("$number", transactionNumber);
        fund.Parameters.AddWithValue("$percent", surchargePercent.ToString(CultureInfo.InvariantCulture));
        fund.ExecuteNonQuery();
        transaction.Commit();
    }

    public (string Name, decimal? CommissionRate)? GetSelection(Guid id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name, CurrentCommissionPercent FROM Agencies WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return (reader.GetString(0), reader.IsDBNull(1) ? null : ParseDecimal(reader.GetString(1)));
    }

    public IReadOnlyList<ServiceListItem> ListServices(
        Guid agencyId, DateOnly? fromDate = null, DateOnly? toDate = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT j.Date, s.Status, s.CurrencyCode, s.ExpectedPrice,
                   s.CollectedAmount, s.CommissionAmount, s.Description
            FROM Services s
            JOIN Workdays j ON j.Id = s.WorkdayId
            WHERE s.AgencyId = $agency
              AND ($fromDate IS NULL OR j.Date >= $fromDate)
              AND ($toDate IS NULL OR j.Date <= $toDate)
            ORDER BY j.Date DESC, s.CreatedAt DESC
            """;
        command.Parameters.AddWithValue("$agency", agencyId.ToString());
        command.Parameters.AddWithValue("$fromDate", fromDate is { } start
            ? start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value);
        command.Parameters.AddWithValue("$toDate", toDate is { } end
            ? end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value);
        using var reader = command.ExecuteReader();
        var services = new List<ServiceListItem>();
        while (reader.Read())
        {
            services.Add(new ServiceListItem(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                (ServiceStatus)reader.GetInt32(1) switch
                {
                    ServiceStatus.Open => "Abierto",
                    ServiceStatus.Finished => "Finalizado",
                    _ => "Unknown"
                },
                reader.GetString(2),
                reader.IsDBNull(3) ? null : ParseDecimal(reader.GetString(3)),
                reader.IsDBNull(4) ? null : ParseDecimal(reader.GetString(4)),
                reader.IsDBNull(5) ? null : ParseDecimal(reader.GetString(5)),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }
        return services;
    }

    public IReadOnlyList<AgencyListItem> ListActive()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, AdministrationNumber, Photo, CurrentCommissionPercent
            FROM Agencies WHERE IsActive = 1 ORDER BY Name COLLATE NOCASE
            """;
        using var reader = command.ExecuteReader();
        var agencies = new List<Agency>();
        while (reader.Read())
        {
            agencies.Add(new Agency
            {
                Id = Guid.Parse(reader.GetString(0)),
                Name = reader.GetString(1),
                AdministrationNumber = reader.IsDBNull(2) ? null : reader.GetString(2),
                Photo = reader.IsDBNull(3) ? null : reader.GetFieldValue<byte[]>(3),
                CurrentCommissionPercent = reader.IsDBNull(4) ? null : ParseDecimal(reader.GetString(4))
            });
        }
        reader.Close();

        return agencies.Select(a => new AgencyListItem(a, GetCupFund(connection, a.Id))).ToList();
    }

    public void Create(Agency agency, decimal initialCupFund)
    {
        Validate(agency, initialCupFund);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Agencies (Id, Name, AdministrationNumber, Photo, CurrentCommissionPercent, IsActive)
            VALUES ($id, $name, $number, $photo, $percentage, 1)
            """;
        AddAgencyParameters(command, agency);
        command.ExecuteNonQuery();
        if (initialCupFund > 0)
            InsertFundMovement(connection, transaction, agency.Id, initialCupFund,
                AgencyFundMovementType.TopUp, "Fondo inicial");
        transaction.Commit();
    }

    public void Update(Agency agency, decimal newCupFund)
    {
        Validate(agency, newCupFund);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var previousFund = GetCupFund(connection, agency.Id, transaction);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Agencies SET Name = $name, AdministrationNumber = $number,
                Photo = $photo, CurrentCommissionPercent = $percentage
            WHERE Id = $id AND IsActive = 1
            """;
        AddAgencyParameters(command, agency);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("The agency is no longer available.");

        var difference = newCupFund - previousFund;
        if (difference != 0)
            InsertFundMovement(connection, transaction, agency.Id, difference,
                AgencyFundMovementType.Adjustment, "Manual fund adjustment");
        transaction.Commit();
    }

    public void UpdateDetails(Agency agency)
    {
        Validate(agency, 0);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Agencies SET Name = $name, AdministrationNumber = $number,
                Photo = $photo, CurrentCommissionPercent = $percentage
            WHERE Id = $id AND IsActive = 1
            """;
        AddAgencyParameters(command, agency);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("La agencia ya no está disponible.");
    }

    public void Delete(Guid agencyId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Agencies SET IsActive = 0 WHERE Id = $id AND IsActive = 1";
        command.Parameters.AddWithValue("$id", agencyId.ToString());
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("The agency is no longer available.");
    }

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabaseInitializer.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true
        }.ToString());
        connection.Open();
        return connection;
    }

    private static decimal GetCupFund(SqliteConnection connection, Guid agencyId,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Amount FROM AgencyFundMovements
            WHERE AgencyId = $id AND CurrencyCode = 'CUP'
            """;
        command.Parameters.AddWithValue("$id", agencyId.ToString());
        using var reader = command.ExecuteReader();
        var total = 0m;
        while (reader.Read()) total += ParseDecimal(reader.GetString(0));
        return total;
    }

    private static void InsertFundMovement(SqliteConnection connection,
        SqliteTransaction transaction, Guid agencyId, decimal amount,
        AgencyFundMovementType type, string description)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO AgencyFundMovements
                (Id, AgencyId, CurrencyCode, Amount, Type, OccurredAt, Description)
            VALUES ($id, $agency, 'CUP', $amount, $type, $date, $description)
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$agency", agencyId.ToString());
        command.Parameters.AddWithValue("$amount", amount.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$type", (int)type);
        command.Parameters.AddWithValue("$date", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$description", description);
        command.ExecuteNonQuery();
    }

    private static void AddAgencyParameters(SqliteCommand command, Agency agency)
    {
        command.Parameters.AddWithValue("$id", agency.Id.ToString());
        command.Parameters.AddWithValue("$name", agency.Name.Trim());
        command.Parameters.AddWithValue("$number", (object?)agency.AdministrationNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$photo", (object?)agency.Photo ?? DBNull.Value);
        command.Parameters.AddWithValue("$percentage", agency.CurrentCommissionPercent is { } value
            ? value.ToString(CultureInfo.InvariantCulture) : DBNull.Value);
    }

    private static decimal ParseDecimal(string value) =>
        decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static void Validate(Agency agency, decimal cupFund)
    {
        if (string.IsNullOrWhiteSpace(agency.Name))
            throw new ArgumentException("Enter the agency name.");
        if (agency.CurrentCommissionPercent is < 0 or > 100)
            throw new ArgumentException("The percentage must be between 0 and 100.");
        if (cupFund < 0)
            throw new ArgumentException("The fund cannot be negative.");
    }
}
