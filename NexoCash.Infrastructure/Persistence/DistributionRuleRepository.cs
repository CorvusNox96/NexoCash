using System.Globalization;
using Microsoft.Data.Sqlite;
using NexoCash.Domain.Entities;

namespace NexoCash.Infrastructure.Persistence;

public sealed record DistributionRuleItem(Guid Id, string Name, Guid AccountId, string AccountName,
    DistributionRuleType Type, int Priority, decimal Value, string? Currency,
    bool AppliesOnSundays, bool AppliesOnNonWorkingDays)
{
    public string RuleText => Type == DistributionRuleType.Percentage
        ? $"{Value:N2}%" : $"{Value:N2} {Currency}";
    public string WhenText => AppliesOnSundays ? "Every day" : "Except Sunday";
}

public sealed class DistributionRuleRepository
{
    private readonly string _databasePath;
    public DistributionRuleRepository(string? databasePath = null) =>
        _databasePath = databasePath ?? DatabaseInitializer.DatabasePath;

    public IReadOnlyList<DistributionRuleItem> List()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.Id, r.Name, r.DestinationAccountId, a.Name,
                   r.Type, r.Priority, r.Value, r.CurrencyCode,
                   r.AppliesOnSundays, r.AppliesOnNonWorkingDays
            FROM DistributionRules r JOIN Accounts a ON a.Id = r.DestinationAccountId
            WHERE r.IsActive = 1
            ORDER BY r.Priority, r.Name
            """;
        using var reader = command.ExecuteReader(); var result = new List<DistributionRuleItem>();
        while (reader.Read()) result.Add(new(
            Guid.Parse(reader.GetString(0)), reader.GetString(1), Guid.Parse(reader.GetString(2)), reader.GetString(3),
            (DistributionRuleType)reader.GetInt32(4), reader.GetInt32(5),
            decimal.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
            reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetInt32(8) != 0, reader.GetInt32(9) != 0));
        return result;
    }

    public void Save(Guid? id, string name, Guid accountId, DistributionRuleType type, int priority,
        decimal value, string? currency, bool onSundays, bool onNonWorkingDays)
    {
        name = name.Trim();
        if (name.Length == 0 || value <= 0 || type == DistributionRuleType.Percentage && value > 100)
            throw new ArgumentException("Enter a name and a positive value (at most 100% for percentages).");
        if (type == DistributionRuleType.FixedAmount && string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Choose a currency for a fixed amount.");
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        using (var account = connection.CreateCommand())
        {
            account.Transaction = transaction;
            account.CommandText = "SELECT 1 FROM Accounts WHERE Id = $id AND IsActive = 1";
            account.Parameters.AddWithValue("$id", accountId.ToString());
            if (account.ExecuteScalar() is null) throw new ArgumentException("Choose an active account.");
        }
        if (type == DistributionRuleType.FixedAmount)
        {
            using var currencyCheck = connection.CreateCommand(); currencyCheck.Transaction = transaction;
            currencyCheck.CommandText = "SELECT 1 FROM Currencies WHERE Code = $code AND IsActive = 1";
            currencyCheck.Parameters.AddWithValue("$code", currency!);
            if (currencyCheck.ExecuteScalar() is null) throw new ArgumentException("Choose an active currency.");
        }
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = id is null ? """
            INSERT INTO DistributionRules (Id, Name, DestinationAccountId, Type, Priority, Value,
                CurrencyCode, AppliesOnSundays, AppliesOnNonWorkingDays, IsActive)
            VALUES ($id, $name, $account, $type, $priority, $value, $currency, $sunday, $nonworking, 1)
            """ : """
            UPDATE DistributionRules SET Name = $name, DestinationAccountId = $account,
                Type = $type, Priority = $priority, Value = $value, CurrencyCode = $currency,
                AppliesOnSundays = $sunday, AppliesOnNonWorkingDays = $nonworking
            WHERE Id = $id AND IsActive = 1
            """;
        command.Parameters.AddWithValue("$id", (id ?? Guid.NewGuid()).ToString());
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$account", accountId.ToString());
        command.Parameters.AddWithValue("$type", (int)type);
        command.Parameters.AddWithValue("$priority", priority);
        command.Parameters.AddWithValue("$value", value.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$currency", type == DistributionRuleType.FixedAmount ? currency! : DBNull.Value);
        command.Parameters.AddWithValue("$sunday", onSundays ? 1 : 0);
        command.Parameters.AddWithValue("$nonworking", onNonWorkingDays ? 1 : 0);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("The rule could not be saved.");
        transaction.Commit();
    }

    public void Delete(Guid id)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE DistributionRules SET IsActive = 0 WHERE Id = $id AND IsActive = 1";
        command.Parameters.AddWithValue("$id", id.ToString());
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("The rule is no longer available.");
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = _databasePath, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true }.ToString());
        connection.Open(); return connection;
    }
}
