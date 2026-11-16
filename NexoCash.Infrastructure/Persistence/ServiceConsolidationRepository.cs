using System.Globalization;
using Microsoft.Data.Sqlite;

namespace NexoCash.Infrastructure.Persistence;

public sealed record ConsolidationAccount(Guid Id, string Name, bool IsCash);
public sealed record ConsolidationService(Guid Id, DateOnly Date, string Recipient, string Type,
    string Currency, string Payment, decimal Amount, decimal Commission);
public sealed record ConsolidationAllocation(Guid AccountId, string Currency, string Payment,
    decimal Amount, bool IsDirectWithdrawal = false);
public sealed record ConsolidationCashCount(string Currency, IReadOnlyDictionary<decimal, int> Banknotes);
public sealed record ConsolidationSummary(string Currency, string Payment, string Type, decimal Amount);

public sealed class ServiceConsolidationRepository
{
    private readonly string _databasePath;

    public ServiceConsolidationRepository(string? databasePath = null) =>
        _databasePath = databasePath ?? DatabaseInitializer.DatabasePath;

    public IReadOnlyList<ConsolidationAccount> ListAccounts()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Type FROM Accounts WHERE IsActive = 1 ORDER BY Name COLLATE NOCASE";
        using var reader = command.ExecuteReader();
        var result = new List<ConsolidationAccount>();
        while (reader.Read()) result.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt32(2) == 0));
        return result;
    }

    public IReadOnlyList<ConsolidationService> ListPending(DateOnly date)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.Id, w.Date, s.Type, COALESCE(a.Name, ''),
                   s.CurrencyCode, s.PaymentMethod, s.CollectedAmount, s.CommissionAmount
            FROM Services s JOIN Workdays w ON w.Id = s.WorkdayId
            LEFT JOIN Agencies a ON a.Id = s.AgencyId
            WHERE w.Date = $date AND s.Status = 2 AND NOT EXISTS
                (SELECT 1 FROM ServiceConsolidationItems i WHERE i.ServiceId = s.Id)
            ORDER BY s.CreatedAt
            """;
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        using var reader = command.ExecuteReader();
        var result = new List<ConsolidationService>();
        while (reader.Read())
        {
            var type = reader.GetInt32(2) switch { 0 => "Servicio Agencia", 2 => "Servicio Directo", _ => "Servicio Cliente" };
            var recipient = reader.GetInt32(2) switch { 2 => "Servicio Directo", 3 => "Servicio Cliente", _ => reader.GetString(3) };
            result.Add(new(Guid.Parse(reader.GetString(0)), DateOnly.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                recipient, type, reader.GetString(4), reader.GetInt32(5) == 0 ? "Cash" : "Transfer",
                decimal.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
                reader.IsDBNull(7) ? 0m : decimal.Parse(reader.GetString(7), CultureInfo.InvariantCulture)));
        }
        return result;
    }

    public Guid Consolidate(DateOnly date, IReadOnlyList<ConsolidationAllocation> allocations,
        IReadOnlyList<ConsolidationCashCount> cashCounts)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var services = LoadPending(connection, transaction, date);
        if (services.Count == 0) throw new InvalidOperationException("There are no finished services to consolidate for this date.");
        var accounts = ListAccounts().ToDictionary(a => a.Id);
        var required = services.GroupBy(s => (s.Currency, s.Payment))
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Amount));
        var assigned = new Dictionary<(string, string), decimal>();
        foreach (var allocation in allocations)
        {
            if (allocation.Amount <= 0) throw new InvalidOperationException("Allocation amounts must be positive.");
            if (allocation.Payment is not ("Cash" or "Transfer"))
                throw new InvalidOperationException("Elige un método de pago válido.");
            if (allocation.IsDirectWithdrawal)
            {
                if (allocation.AccountId != Guid.Empty)
                    throw new InvalidOperationException("El retiro directo no debe tener una cuenta de destino.");
            }
            else
            {
                if (!accounts.TryGetValue(allocation.AccountId, out var account))
                    throw new InvalidOperationException("Selecciona una cuenta activa para cada distribución.");
                if (account.IsCash != (allocation.Payment == "Cash"))
                    throw new InvalidOperationException("El efectivo debe ir a una cuenta de efectivo y las transferencias a una virtual.");
            }
            var key = (allocation.Currency, allocation.Payment);
            assigned[key] = assigned.GetValueOrDefault(key) + allocation.Amount;
        }
        if (required.Count != assigned.Count || required.Any(pair => assigned.GetValueOrDefault(pair.Key) != pair.Value))
            throw new InvalidOperationException("Allocations must exactly match the finished service totals for each currency and payment method.");
        var cashTotals = allocations.Where(item => item.Payment == "Cash" && !item.IsDirectWithdrawal)
            .GroupBy(item => item.Currency).ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));
        if (cashCounts.GroupBy(c => c.Currency).Any(group => group.Count() > 1))
            throw new InvalidOperationException("Only one banknote count is allowed per currency.");
        var countedAmounts = new Dictionary<string, decimal>();
        foreach (var count in cashCounts)
        {
            if (count.Banknotes.Any(note => note.Key <= 0 || note.Value <= 0))
                throw new InvalidOperationException("Banknote values and quantities must be positive.");
            if (count.Banknotes.Count > 0)
                countedAmounts.Add(count.Currency, count.Banknotes.Sum(note => note.Key * note.Value));
        }
        if (cashCounts.Count > 0 && (cashTotals.Count != countedAmounts.Count ||
            cashTotals.Any(pair => countedAmounts.GetValueOrDefault(pair.Key) != pair.Value)))
            throw new InvalidOperationException("El conteo de billetes debe coincidir con el efectivo destinado a caja en cada moneda.");

        var consolidationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("O");
        Execute(connection, transaction, "INSERT INTO ServiceConsolidations (Id, ConsolidatedAt) VALUES ($id, $at)",
            ("$id", consolidationId.ToString()), ("$at", now));
        foreach (var service in services)
            Execute(connection, transaction, "INSERT INTO ServiceConsolidationItems (ConsolidationId, ServiceId) VALUES ($close, $service)",
                ("$close", consolidationId.ToString()), ("$service", service.Id.ToString()));
        foreach (var allocation in allocations)
        {
            if (allocation.IsDirectWithdrawal)
            {
                Execute(connection, transaction, """
                    INSERT INTO ServiceDirectWithdrawals
                        (Id, ConsolidationId, CurrencyCode, PaymentMethod, Amount, RecordedAt)
                    VALUES ($id, $consolidation, $currency, $payment, $amount, $at)
                    """, ("$id", Guid.NewGuid().ToString()), ("$consolidation", consolidationId.ToString()),
                    ("$currency", allocation.Currency),
                    ("$payment", allocation.Payment == "Cash" ? 0 : 1),
                    ("$amount", allocation.Amount.ToString(CultureInfo.InvariantCulture)), ("$at", now));
                continue;
            }
            var movementId = Guid.NewGuid();
            Execute(connection, transaction, """
                INSERT INTO Movements (Id, AccountId, CurrencyCode, Amount, Type, OccurredAt, RecordedAt, OperationId, Description)
                VALUES ($id, $account, $currency, $amount, 0, $at, $at, $operation, 'Service consolidation')
                """, ("$id", movementId.ToString()), ("$account", allocation.AccountId.ToString()),
                ("$currency", allocation.Currency), ("$amount", allocation.Amount.ToString(CultureInfo.InvariantCulture)),
                ("$at", now), ("$operation", consolidationId.ToString()));
        }
        foreach (var count in cashCounts)
        {
            foreach (var note in count.Banknotes)
            {
                string denominationId;
                using (var denomination = connection.CreateCommand())
                {
                    denomination.Transaction = transaction;
                    denomination.CommandText = "SELECT Id, Value FROM Denominations WHERE CurrencyCode = $currency AND IsActive = 1";
                    denomination.Parameters.AddWithValue("$currency", count.Currency);
                    using var reader = denomination.ExecuteReader();
                    denominationId = "";
                    while (reader.Read())
                        if (decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture) == note.Key)
                        {
                            denominationId = reader.GetString(0);
                            break;
                        }
                    if (denominationId.Length == 0)
                        throw new InvalidOperationException($"The {note.Key} {count.Currency} denomination is not active. Add it in Settings first.");
                }
                Execute(connection, transaction, "INSERT INTO ServiceConsolidationBanknotes (ConsolidationId, DenominationId, Quantity) VALUES ($consolidation, $denomination, $quantity)",
                    ("$consolidation", consolidationId.ToString()), ("$denomination", denominationId), ("$quantity", note.Value));
            }
        }
        transaction.Commit();
        return consolidationId;
    }

    private static List<(Guid Id, string Currency, string Payment, decimal Amount)> LoadPending(
        SqliteConnection connection, SqliteTransaction transaction, DateOnly date)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT s.Id, s.CurrencyCode, s.PaymentMethod, s.CollectedAmount
            FROM Services s JOIN Workdays w ON w.Id = s.WorkdayId
            WHERE w.Date = $date AND s.Status = 2 AND NOT EXISTS
                (SELECT 1 FROM ServiceConsolidationItems i WHERE i.ServiceId = s.Id)
            """;
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        using var reader = command.ExecuteReader();
        var result = new List<(Guid, string, string, decimal)>();
        while (reader.Read()) result.Add((Guid.Parse(reader.GetString(0)), reader.GetString(1),
            reader.GetInt32(2) == 0 ? "Cash" : "Transfer",
            decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture)));
        return result;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
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
