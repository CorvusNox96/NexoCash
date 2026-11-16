using System.Globalization;
using Microsoft.Data.Sqlite;

namespace NexoCash.Infrastructure.Persistence;

public sealed record CurrencySettingsItem(string Code, string Name, bool IsPrimary)
{
    public string PrimaryLabel => IsPrimary ? "Primary" : "";
    public string Display => $"{Code} · {Name}";
}

public sealed record DenominationSettingsItem(Guid Id, string Currency, decimal Value, int Quantity = 0)
{
    public string ValueText => $"{Value:N2} {Currency}";
    public string QuantityText => Quantity.ToString("N0", CultureInfo.CurrentCulture);
}

public sealed record ExchangeRateSettingsItem(Guid Id, string From, string To, decimal Value, DateTimeOffset EffectiveFrom)
{
    public string Pair => $"1 {From} = {Value:N4} {To}";
    public DateOnly Day => DateOnly.FromDateTime(EffectiveFrom.ToLocalTime().DateTime);
    public string TimeText => EffectiveFrom.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
    public string DateText => EffectiveFrom.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
}

public sealed class CurrencySettingsRepository
{
    private static readonly HashSet<string> SupportedCodes = ["CUP", "USD", "MLC", "EUR"];
    private readonly string _databasePath;

    public CurrencySettingsRepository(string? databasePath = null) =>
        _databasePath = databasePath ?? DatabaseInitializer.DatabasePath;

    public IReadOnlyList<CurrencySettingsItem> ListCurrencies()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Code, Name, IsPrimaryCurrency FROM Currencies WHERE IsActive = 1 ORDER BY IsPrimaryCurrency DESC, Code";
        using var reader = command.ExecuteReader();
        var items = new List<CurrencySettingsItem>();
        while (reader.Read()) items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2) == 1));
        return items;
    }

    public void SaveCurrency(string code, string name)
    {
        code = code.Trim().ToUpperInvariant();
        name = name.Trim();
        if (!SupportedCodes.Contains(code)) throw new ArgumentException("Choose CUP, USD, MLC, or EUR.");
        if (name.Length == 0) throw new ArgumentException("Enter a currency name.");
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Currencies (Code, Name, IsActive) VALUES ($code, $name, 1)
            ON CONFLICT(Code) DO UPDATE SET Name = excluded.Name, IsActive = 1
            """;
        command.Parameters.AddWithValue("$code", code);
        command.Parameters.AddWithValue("$name", name);
        command.ExecuteNonQuery();
    }

    public void SetPrimary(string code)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "SELECT IsActive FROM Currencies WHERE Code = $code";
        check.Parameters.AddWithValue("$code", code);
        if (Convert.ToInt32(check.ExecuteScalar()) != 1)
            throw new InvalidOperationException("Choose an active currency.");
        Execute(connection, transaction, "UPDATE Currencies SET IsPrimaryCurrency = 0 WHERE IsPrimaryCurrency = 1");
        Execute(connection, transaction, "UPDATE Currencies SET IsPrimaryCurrency = 1 WHERE Code = $code", ("$code", code));
        transaction.Commit();
    }

    public void DeleteCurrency(string code)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "SELECT IsPrimaryCurrency FROM Currencies WHERE Code = $code AND IsActive = 1";
        check.Parameters.AddWithValue("$code", code);
        var primary = check.ExecuteScalar();
        if (primary is null) throw new InvalidOperationException("The currency is no longer available.");
        if (Convert.ToInt32(primary) == 1) throw new InvalidOperationException("Choose another primary currency first.");
        using var movement = connection.CreateCommand();
        movement.Transaction = transaction;
        movement.CommandText = "SELECT AccountId, Amount FROM Movements WHERE CurrencyCode = $code";
        movement.Parameters.AddWithValue("$code", code);
        using var reader = movement.ExecuteReader();
        var balances = new Dictionary<string, decimal>();
        while (reader.Read())
        {
            var accountId = reader.GetString(0);
            balances[accountId] = balances.GetValueOrDefault(accountId) +
                decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
        }
        reader.Close();
        if (balances.Values.Any(balance => balance != 0))
            throw new InvalidOperationException("Move or withdraw this currency's remaining account balances first.");
        using var agencyFunds = connection.CreateCommand();
        agencyFunds.Transaction = transaction;
        agencyFunds.CommandText = "SELECT AgencyId, Amount FROM AgencyFundMovements WHERE CurrencyCode = $code";
        agencyFunds.Parameters.AddWithValue("$code", code);
        using var fundReader = agencyFunds.ExecuteReader();
        var funds = new Dictionary<string, decimal>();
        while (fundReader.Read())
        {
            var agencyId = fundReader.GetString(0);
            funds[agencyId] = funds.GetValueOrDefault(agencyId) +
                decimal.Parse(fundReader.GetString(1), CultureInfo.InvariantCulture);
        }
        fundReader.Close();
        if (funds.Values.Any(fund => fund != 0))
            throw new InvalidOperationException("Clear the agency funds in this currency first.");
        using var services = connection.CreateCommand();
        services.Transaction = transaction;
        services.CommandText = "SELECT EXISTS(SELECT 1 FROM Services s WHERE s.CurrencyCode = $code AND NOT EXISTS (SELECT 1 FROM ServiceConsolidationItems i WHERE i.ServiceId = s.Id))";
        services.Parameters.AddWithValue("$code", code);
        if (Convert.ToInt32(services.ExecuteScalar()) == 1)
            throw new InvalidOperationException("Consolidate or resolve pending services in this currency first.");
        Execute(connection, transaction, "UPDATE Currencies SET IsActive = 0 WHERE Code = $code", ("$code", code));
        transaction.Commit();
    }

    public IReadOnlyList<DenominationSettingsItem> ListDenominations(string currency)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, CurrencyCode, Value FROM Denominations WHERE CurrencyCode = $currency AND IsActive = 1";
        command.Parameters.AddWithValue("$currency", currency);
        using var reader = command.ExecuteReader();
        var items = new List<DenominationSettingsItem>();
        while (reader.Read()) items.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1),
            decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture)));
        reader.Close();
        return items.Select(item => item with
        {
            Quantity = GetPhysicalQuantity(connection, null, item.Currency, item.Value)
        }).OrderBy(item => item.Value).ToList();
    }

    public void AddDenomination(string currency, decimal value)
    {
        if (value <= 0) throw new ArgumentException("The denomination must be greater than zero.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        EnsureCurrencyActive(connection, transaction, currency);
        AddDenomination(connection, transaction, currency, value);
        transaction.Commit();
    }

    public void UpdateDenomination(Guid id, decimal newValue)
    {
        if (newValue <= 0) throw new ArgumentException("The denomination must be greater than zero.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var item = LoadDenomination(connection, transaction, id);
        if (item.Value == newValue) return;
        if (GetPhysicalQuantity(connection, transaction, item.Currency, item.Value) != 0)
            throw new InvalidOperationException("This denomination still has physical banknotes. Count or remove them before editing it.");
        Execute(connection, transaction, "UPDATE Denominations SET IsActive = 0 WHERE replace(Id, '-', '') = $id", ("$id", id.ToString("N")));
        AddDenomination(connection, transaction, item.Currency, newValue);
        transaction.Commit();
    }

    public void DeleteDenomination(Guid id)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var item = LoadDenomination(connection, transaction, id);
        if (GetPhysicalQuantity(connection, transaction, item.Currency, item.Value) != 0)
            throw new InvalidOperationException("This denomination still has physical banknotes. Count or remove them before deleting it.");
        Execute(connection, transaction, "UPDATE Denominations SET IsActive = 0 WHERE replace(Id, '-', '') = $id", ("$id", id.ToString("N")));
        transaction.Commit();
    }

    public IReadOnlyList<ExchangeRateSettingsItem> ListRates()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, FromCurrencyCode, ToCurrencyCode, Value, EffectiveFrom FROM ExchangeRates ORDER BY EffectiveFrom DESC";
        using var reader = command.ExecuteReader();
        var items = new List<ExchangeRateSettingsItem>();
        while (reader.Read()) items.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2),
            decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture)));
        return items;
    }

    public Guid SaveRate(Guid? id, string from, string to, decimal value)
    {
        if (from == to) throw new ArgumentException("Choose two different currencies.");
        if (value <= 0) throw new ArgumentException("The exchange rate must be greater than zero.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        EnsureCurrencyActive(connection, transaction, from);
        EnsureCurrencyActive(connection, transaction, to);
        var rateId = id ?? Guid.NewGuid();
        if (id is null)
            Execute(connection, transaction, "INSERT INTO ExchangeRates (Id, FromCurrencyCode, ToCurrencyCode, Value, EffectiveFrom) VALUES ($id, $from, $to, $value, $at)",
                ("$id", rateId.ToString()), ("$from", from), ("$to", to),
                ("$value", value.ToString(CultureInfo.InvariantCulture)), ("$at", DateTimeOffset.UtcNow.ToString("O")));
        else
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE ExchangeRates SET FromCurrencyCode = $from, ToCurrencyCode = $to, Value = $value WHERE Id = $id";
            update.Parameters.AddWithValue("$id", rateId.ToString());
            update.Parameters.AddWithValue("$from", from);
            update.Parameters.AddWithValue("$to", to);
            update.Parameters.AddWithValue("$value", value.ToString(CultureInfo.InvariantCulture));
            if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException("The exchange rate is no longer available.");
        }
        transaction.Commit();
        return rateId;
    }

    public void DeleteRate(Guid id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ExchangeRates WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("The exchange rate is no longer available.");
    }

    private static DenominationSettingsItem LoadDenomination(SqliteConnection connection, SqliteTransaction transaction, Guid id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT CurrencyCode, Value FROM Denominations WHERE replace(Id, '-', '') = $id AND IsActive = 1";
        command.Parameters.AddWithValue("$id", id.ToString("N"));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("The denomination is no longer available.");
        return new(id, reader.GetString(0), decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture));
    }

    private static int GetPhysicalQuantity(SqliteConnection connection, SqliteTransaction? transaction, string currency, decimal value)
        => CashRepository.PhysicalQuantity(connection, transaction, currency, value);

    private static void AddDenomination(SqliteConnection connection, SqliteTransaction transaction, string currency, decimal value)
    {
        var valueText = value.ToString("G29", CultureInfo.InvariantCulture);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id, Value, IsActive FROM Denominations WHERE CurrencyCode = $currency";
        command.Parameters.AddWithValue("$currency", currency);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture) != value) continue;
            var id = reader.GetString(0);
            var active = reader.GetInt32(2) == 1;
            reader.Close();
            if (active) throw new InvalidOperationException("That denomination already exists.");
            Execute(connection, transaction, "UPDATE Denominations SET IsActive = 1 WHERE Id = $id", ("$id", id));
            return;
        }
        reader.Close();
        Execute(connection, transaction, "INSERT INTO Denominations (Id, CurrencyCode, Value) VALUES ($id, $currency, $value)",
            ("$id", Guid.NewGuid().ToString()), ("$currency", currency), ("$value", valueText));
    }

    private static void EnsureCurrencyActive(SqliteConnection connection, SqliteTransaction transaction, string code)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT IsActive FROM Currencies WHERE Code = $code";
        command.Parameters.AddWithValue("$code", code);
        if (Convert.ToInt32(command.ExecuteScalar()) != 1)
            throw new InvalidOperationException("Choose an active currency.");
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
            DataSource = _databasePath, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true
        }.ToString());
        connection.Open();
        return connection;
    }
}
