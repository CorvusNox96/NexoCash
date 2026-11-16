using System.Globalization;
using Microsoft.Data.Sqlite;

namespace NexoCash.Infrastructure.Persistence;

public sealed class CashValuationRepository
{
    private readonly string _databasePath;

    public CashValuationRepository(string? databasePath = null) =>
        _databasePath = databasePath ?? DatabaseInitializer.DatabasePath;

    public decimal ToCup(IReadOnlyDictionary<string, decimal> amounts)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true
        }.ToString());
        connection.Open();
        return ToCup(connection, null, amounts);
    }

    internal static decimal ToCup(SqliteConnection connection, SqliteTransaction? transaction,
        IReadOnlyDictionary<string, decimal> amounts)
    {
        var total = 0m;
        foreach (var (currency, amount) in amounts)
        {
            if (amount < 0) throw new ArgumentException("Currency amounts cannot be negative.");
            if (amount == 0) continue;
            var rate = GetCupRate(connection, transaction, currency);
            total += decimal.Round(amount * rate, 2, MidpointRounding.AwayFromZero);
        }
        return total;
    }

    private static decimal GetCupRate(SqliteConnection connection, SqliteTransaction? transaction, string currency)
    {
        if (currency == "CUP") return 1m;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Value FROM ExchangeRates
            WHERE FromCurrencyCode = $from AND ToCurrencyCode = 'CUP'
              AND EffectiveFrom <= $now
            ORDER BY EffectiveFrom DESC LIMIT 1
            """;
        command.Parameters.AddWithValue("$from", currency);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        if (command.ExecuteScalar() is string direct)
        {
            var value = decimal.Parse(direct, CultureInfo.InvariantCulture);
            if (value > 0) return value;
        }
        command.CommandText = """
            SELECT Value FROM ExchangeRates
            WHERE FromCurrencyCode = 'CUP' AND ToCurrencyCode = $from
              AND EffectiveFrom <= $now
            ORDER BY EffectiveFrom DESC LIMIT 1
            """;
        if (command.ExecuteScalar() is string reverse)
        {
            var value = decimal.Parse(reverse, CultureInfo.InvariantCulture);
            if (value > 0) return 1m / value;
        }
        throw new InvalidOperationException($"Set an exchange rate between {currency} and CUP in Settings first.");
    }
}
