using System.Globalization;
using Microsoft.Data.Sqlite;

namespace NexoCash.Infrastructure.Persistence;

public sealed record CashInventoryItem(string Currency, decimal Value, int Quantity)
{
    public decimal Amount => Value * Quantity;
    public string ValueText => $"{Value:N0} {Currency}";
    public string AmountText => $"{Amount:N0} {Currency}";
}

public sealed record CashBalanceComparison(string Currency, decimal CashBalance, decimal BanknoteTotal)
{
    public decimal Difference => BanknoteTotal - CashBalance;
    public string BalanceText => $"{CashBalance:N0} {Currency}";
    public string BanknotesText => $"{BanknoteTotal:N0} {Currency}";
    public string DifferenceText => $"{Difference:+#,##0;-#,##0;0} {Currency}";
    public bool Matches => Difference == 0;
}

public sealed class CashRepository
{
    private readonly string _databasePath;
    public CashRepository(string? databasePath = null) => _databasePath = databasePath ?? DatabaseInitializer.DatabasePath;

    public IReadOnlyList<CashInventoryItem> ListInventory()
    {
        using var connection = Open();
        return LoadInventory(connection, null);
    }

    internal static int PhysicalQuantity(SqliteConnection connection, SqliteTransaction? transaction,
        string currency, decimal value) => LoadInventory(connection, transaction)
        .FirstOrDefault(item => item.Currency == currency && item.Value == value)?.Quantity ?? 0;

    public IReadOnlyDictionary<decimal, int> SuggestBanknotes(string currency, decimal amount)
    {
        if (amount <= 0) return new Dictionary<decimal, int>();
        var options = ListInventory().Where(item => item.Currency == currency && item.Quantity > 0)
            .OrderByDescending(item => item.Value).ToList();
        var target = decimal.ToInt64(amount * 100m);
        if (target != amount * 100m) return new Dictionary<decimal, int>();
        var chosen = new Dictionary<decimal, int>();
        var failed = new HashSet<(int Index, long Remaining)>();
        bool Find(int index, long remaining)
        {
            if (remaining == 0) return true;
            if (index == options.Count || !failed.Add((index, remaining))) return false;
            var item = options[index];
            var value = decimal.ToInt64(item.Value * 100m);
            for (var quantity = Math.Min(item.Quantity, (int)Math.Min(int.MaxValue, remaining / value)); quantity >= 0; quantity--)
            {
                if (quantity > 0) chosen[item.Value] = quantity;
                else chosen.Remove(item.Value);
                if (Find(index + 1, remaining - quantity * value)) return true;
            }
            chosen.Remove(item.Value);
            return false;
        }
        return Find(0, target) ? chosen : new Dictionary<decimal, int>();
    }

    public IReadOnlyList<CashBalanceComparison> ListBalanceComparisons()
    {
        using var connection = Open();
        var balances = ReadCashBalances(connection, null);
        var notes = LoadInventory(connection, null).GroupBy(item => item.Currency)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Code FROM Currencies WHERE IsActive = 1 ORDER BY IsPrimaryCurrency DESC, Code";
        using var reader = command.ExecuteReader();
        var result = new List<CashBalanceComparison>();
        while (reader.Read())
        {
            var currency = reader.GetString(0);
            result.Add(new(currency, balances.GetValueOrDefault(currency), notes.GetValueOrDefault(currency)));
        }
        return result;
    }

    public void Reconcile(IReadOnlyDictionary<string, IReadOnlyDictionary<decimal, int>> counted)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var inventory = LoadInventory(connection, transaction);
        var known = inventory.ToDictionary(item => (item.Currency, item.Value));
        foreach (var (currency, notes) in counted)
            foreach (var (value, quantity) in notes)
                if (quantity < 0 || !known.ContainsKey((currency, value)))
                    throw new InvalidOperationException("Cuenta solo denominaciones activas y cantidades no negativas.");
        var differences = inventory.Select(item =>
            (item.Currency, item.Value, Difference: counted.GetValueOrDefault(item.Currency)?.GetValueOrDefault(item.Value) - item.Quantity ?? -item.Quantity)).ToList();
        foreach (var difference in differences.Where(item => item.Difference != 0))
        {
            Execute(connection, transaction, """
                INSERT INTO CashInventoryCorrections (DenominationId, QuantityDelta)
                VALUES ($denomination, $delta)
                ON CONFLICT(DenominationId) DO UPDATE SET QuantityDelta = QuantityDelta + excluded.QuantityDelta
                """, ("$denomination", FindDenomination(connection, transaction, difference.Currency, difference.Value)),
                ("$delta", difference.Difference));
        }
        transaction.Commit();
    }

    private static IReadOnlyList<CashInventoryItem> LoadInventory(SqliteConnection connection, SqliteTransaction? transaction)
    {
        var entries = new Dictionary<(string Currency, decimal Value), int>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT CurrencyCode, Value FROM Denominations WHERE IsActive = 1";
        using (var reader = command.ExecuteReader())
            while (reader.Read()) entries[(reader.GetString(0), decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture))] = 0;
        command.CommandText = """
            SELECT d.CurrencyCode, d.Value, m.Amount, b.Quantity FROM BanknoteMovementDetails b
            JOIN Denominations d ON d.Id = b.DenominationId JOIN Movements m ON m.Id = b.MovementId
            JOIN Accounts a ON a.Id = m.AccountId WHERE a.Type = 0
            """;
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var key = (reader.GetString(0), decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture));
                entries[key] = entries.GetValueOrDefault(key) +
                    (decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture) >= 0 ? 1 : -1) * reader.GetInt32(3);
            }
        command.CommandText = "SELECT d.CurrencyCode, d.Value, b.Quantity FROM ServiceConsolidationBanknotes b JOIN Denominations d ON d.Id = b.DenominationId";
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var key = (reader.GetString(0), decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture));
                entries[key] = entries.GetValueOrDefault(key) + reader.GetInt32(2);
            }
        command.CommandText = "SELECT d.CurrencyCode, d.Value, c.QuantityDelta FROM CashInventoryCorrections c JOIN Denominations d ON d.Id = c.DenominationId";
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var key = (reader.GetString(0), decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture));
                entries[key] = entries.GetValueOrDefault(key) + reader.GetInt32(2);
            }
        return entries.OrderBy(item => item.Key.Currency).ThenBy(item => item.Key.Value)
            .Select(item => new CashInventoryItem(item.Key.Currency, item.Key.Value, item.Value)).ToList();
    }

    private static Dictionary<string, decimal> ReadCashBalances(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT m.CurrencyCode, m.Amount FROM Movements m JOIN Accounts a ON a.Id = m.AccountId WHERE a.Type = 0";
        using var reader = command.ExecuteReader();
        var balances = new Dictionary<string, decimal>();
        while (reader.Read()) balances[reader.GetString(0)] = balances.GetValueOrDefault(reader.GetString(0)) + decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
        return balances;
    }

    private static string FindDenomination(SqliteConnection connection, SqliteTransaction transaction, string currency, decimal value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id, Value FROM Denominations WHERE CurrencyCode = $currency AND IsActive = 1";
        command.Parameters.AddWithValue("$currency", currency);
        using var reader = command.ExecuteReader();
        while (reader.Read()) if (decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture) == value) return reader.GetString(0);
        throw new InvalidOperationException($"The {value} {currency} denomination is no longer active.");
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = _databasePath, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true }.ToString());
        connection.Open(); return connection;
    }
}
