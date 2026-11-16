using System.Globalization;
using Microsoft.Data.Sqlite;

namespace NexoCash.Infrastructure.Persistence;

public sealed record CurrencyExchangeOverview(DateTimeOffset Date, string FromAccount, string ToAccount,
    string OutCurrency, decimal OutAmount, string InCurrency, decimal InAmount, decimal Rate)
{
    public DateOnly Day => DateOnly.FromDateTime(Date.ToLocalTime().DateTime);
    public string TimeText => Date.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
    public string DateText => Date.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
    public string AccountText => FromAccount == ToAccount ? FromAccount : $"{FromAccount} → {ToAccount}";
    public string OutText => $"{OutAmount:N2} {OutCurrency}";
    public string InText => $"{InAmount:N2} {InCurrency}";
    public string RateText => Rate.ToString("N4", CultureInfo.CurrentCulture);
}

public sealed class CurrencyExchangeRepository
{
    private readonly string _databasePath;
    public CurrencyExchangeRepository(string? databasePath = null) => _databasePath = databasePath ?? DatabaseInitializer.DatabasePath;

    public static decimal CalculateReceivedAmount(string fromCurrency, string toCurrency,
        string primaryCurrency, decimal amount, decimal rate)
    {
        if (fromCurrency == toCurrency || fromCurrency != primaryCurrency && toCurrency != primaryCurrency)
            throw new ArgumentException($"El cambio debe incluir la moneda principal ({primaryCurrency}).");
        if (amount <= 0 || rate <= 0)
            throw new ArgumentException("El importe y la tasa deben ser positivos.");
        return decimal.Round(fromCurrency == primaryCurrency ? amount / rate : amount * rate,
            2, MidpointRounding.AwayFromZero);
    }

    public decimal? GetSuggestedRate(string fromCurrency, string toCurrency)
    {
        using var connection = Open();
        var primary = GetPrimaryCurrency(connection);
        if (fromCurrency == toCurrency || fromCurrency != primary && toCurrency != primary) return null;
        var other = fromCurrency == primary ? toCurrency : fromCurrency;
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT FromCurrencyCode, ToCurrencyCode, Value FROM ExchangeRates
            WHERE ((FromCurrencyCode = $other AND ToCurrencyCode = $primary)
                OR (FromCurrencyCode = $primary AND ToCurrencyCode = $other))
                AND EffectiveFrom <= $now
            ORDER BY EffectiveFrom DESC LIMIT 1
            """;
        command.Parameters.AddWithValue("$other", other);
        command.Parameters.AddWithValue("$primary", primary);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var value = decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
        return value <= 0 ? null : reader.GetString(0) == primary ? 1m / value : value;
    }

    private static string GetPrimaryCurrency(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Code FROM Currencies WHERE IsPrimaryCurrency = 1 AND IsActive = 1 LIMIT 1";
        return command.ExecuteScalar() as string
            ?? throw new InvalidOperationException("Define una moneda principal en Ajustes.");
    }

    public IReadOnlyList<CurrencyExchangeOverview> List()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT x.OccurredAt, a1.Name, a2.Name, m1.CurrencyCode, m1.Amount,
                   m2.CurrencyCode, m2.Amount, x.AppliedRate
            FROM CurrencyExchanges x
            JOIN Movements m1 ON m1.Id = x.OutgoingMovementId
            JOIN Movements m2 ON m2.Id = x.IncomingMovementId
            JOIN Accounts a1 ON a1.Id = m1.AccountId JOIN Accounts a2 ON a2.Id = m2.AccountId
            ORDER BY x.OccurredAt DESC
            """;
        using var reader = command.ExecuteReader();
        var result = new List<CurrencyExchangeOverview>();
        while (reader.Read()) result.Add(new(
            DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), -decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
            reader.GetString(5), decimal.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
            decimal.Parse(reader.GetString(7), CultureInfo.InvariantCulture)));
        return result;
    }

    public void Exchange(Guid accountId, string fromCurrency, string toCurrency,
        decimal outgoingAmount, decimal appliedRate,
        IReadOnlyDictionary<decimal, int> outgoingNotes, IReadOnlyDictionary<decimal, int> incomingNotes,
        string? description)
    {
        using var connection = Open();
        var incomingAmount = CalculateReceivedAmount(fromCurrency, toCurrency,
            GetPrimaryCurrency(connection), outgoingAmount, appliedRate);
        if (incomingAmount <= 0) throw new ArgumentException("El importe recibido debe ser positivo.");
        using var transaction = connection.BeginTransaction();
        var sourceCash = AccountIsCash(connection, transaction, accountId);
        var balance = ReadBalance(connection, transaction, accountId, fromCurrency);
        if (balance < outgoingAmount) throw new InvalidOperationException("The source account has insufficient funds.");
        if (sourceCash ? outgoingNotes.Count == 0 || outgoingNotes.Sum(note => note.Key * note.Value) != outgoingAmount : outgoingNotes.Count != 0)
            throw new InvalidOperationException("Count the exact outgoing cash banknotes.");
        if (sourceCash ? incomingNotes.Count == 0 || incomingNotes.Sum(note => note.Key * note.Value) != incomingAmount : incomingNotes.Count != 0)
            throw new InvalidOperationException("Count the exact incoming cash banknotes.");
        foreach (var note in outgoingNotes)
            if (note.Key <= 0 || note.Value <= 0 || PhysicalQuantity(connection, transaction, fromCurrency, note.Key) < note.Value)
                throw new InvalidOperationException($"Not enough {note.Key} {fromCurrency} banknotes are available.");
        foreach (var note in incomingNotes)
            if (note.Key <= 0 || note.Value <= 0)
                throw new InvalidOperationException("Banknote quantities must be positive.");
        var id = Guid.NewGuid().ToString();
        var outgoingId = Guid.NewGuid().ToString();
        var incomingId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToString("O");
        var detail = string.IsNullOrWhiteSpace(description) ? "Currency exchange" : description.Trim();
        InsertMovement(connection, transaction, outgoingId, accountId, fromCurrency, -outgoingAmount, now, id, detail);
        InsertMovement(connection, transaction, incomingId, accountId, toCurrency, incomingAmount, now, id, detail);
        InsertNotes(connection, transaction, outgoingId, fromCurrency, outgoingNotes);
        InsertNotes(connection, transaction, incomingId, toCurrency, incomingNotes);
        Execute(connection, transaction, """
            INSERT INTO CurrencyExchanges (Id, OutgoingMovementId, IncomingMovementId, AppliedRate, OccurredAt, Description)
            VALUES ($id, $outgoing, $incoming, $rate, $at, $description)
            """, ("$id", id), ("$outgoing", outgoingId), ("$incoming", incomingId),
            ("$rate", appliedRate.ToString(CultureInfo.InvariantCulture)), ("$at", now), ("$description", detail));
        transaction.Commit();
    }

    private static bool AccountIsCash(SqliteConnection connection, SqliteTransaction transaction, Guid id)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT Type FROM Accounts WHERE Id = $id AND IsActive = 1";
        command.Parameters.AddWithValue("$id", id.ToString());
        var result = command.ExecuteScalar();
        if (result is null) throw new InvalidOperationException("Choose an active account.");
        return Convert.ToInt32(result) == 0;
    }

    private static decimal ReadBalance(SqliteConnection connection, SqliteTransaction transaction, Guid id, string currency)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT Amount FROM Movements WHERE AccountId = $id AND CurrencyCode = $currency";
        command.Parameters.AddWithValue("$id", id.ToString()); command.Parameters.AddWithValue("$currency", currency);
        using var reader = command.ExecuteReader(); var total = 0m;
        while (reader.Read()) total += decimal.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
        return total;
    }

    private static int PhysicalQuantity(SqliteConnection connection, SqliteTransaction transaction, string currency, decimal value)
        => CashRepository.PhysicalQuantity(connection, transaction, currency, value);

    private static void InsertMovement(SqliteConnection connection, SqliteTransaction transaction, string id, Guid account,
        string currency, decimal amount, string at, string operation, string detail) => Execute(connection, transaction, """
            INSERT INTO Movements (Id, AccountId, CurrencyCode, Amount, Type, OccurredAt, RecordedAt, OperationId, Description)
            VALUES ($id, $account, $currency, $amount, 3, $at, $at, $operation, $detail)
            """, ("$id", id), ("$account", account.ToString()), ("$currency", currency),
            ("$amount", amount.ToString(CultureInfo.InvariantCulture)), ("$at", at), ("$operation", operation), ("$detail", detail));

    private static void InsertNotes(SqliteConnection connection, SqliteTransaction transaction, string movement,
        string currency, IReadOnlyDictionary<decimal, int> notes)
    {
        foreach (var note in notes)
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT Id, Value FROM Denominations WHERE CurrencyCode = $currency AND IsActive = 1";
            command.Parameters.AddWithValue("$currency", currency);
            using var reader = command.ExecuteReader(); string? denominationId = null;
            while (reader.Read()) if (decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture) == note.Key)
            { denominationId = reader.GetString(0); break; }
            reader.Close();
            if (denominationId is null) throw new InvalidOperationException($"Add the {note.Key} {currency} denomination in Settings first.");
            Execute(connection, transaction, "INSERT INTO BanknoteMovementDetails (Id, MovementId, DenominationId, Quantity) VALUES ($id, $movement, $denomination, $quantity)",
                ("$id", Guid.NewGuid().ToString()), ("$movement", movement), ("$denomination", denominationId), ("$quantity", note.Value));
        }
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    { using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value); command.ExecuteNonQuery(); }
    private SqliteConnection Open() { var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = _databasePath, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true }.ToString()); connection.Open(); return connection; }
}
