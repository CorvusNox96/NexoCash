using System.Globalization;
using Microsoft.Data.Sqlite;

namespace NexoCash.Infrastructure.Persistence;

public sealed record WeeklyServiceIncome(DateOnly WeekStart, string Currency, decimal Amount, int Services);
public sealed record WeeklyAccountFlow(DateOnly WeekStart, string Currency, decimal Income, decimal Withdrawals);

public sealed class OverviewStatisticsRepository
{
    private readonly string _databasePath;

    public OverviewStatisticsRepository(string? databasePath = null) =>
        _databasePath = databasePath ?? DatabaseInitializer.DatabasePath;

    public IReadOnlyList<WeeklyServiceIncome> ListWeeklyIncome(DateOnly from, DateOnly through)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath, Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT w.Date, s.CurrencyCode, s.CollectedAmount
            FROM Services s JOIN Workdays w ON w.Id = s.WorkdayId
            WHERE s.Status = 2 AND w.Date >= $from AND w.Date <= $through
            """;
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$through", through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        using var reader = command.ExecuteReader();
        var rows = new List<(DateOnly Date, string Currency, decimal Amount)>();
        while (reader.Read())
            rows.Add((DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(1), decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture)));
        return rows.GroupBy(row => (Week: row.Date.AddDays(-((int)row.Date.DayOfWeek + 6) % 7), row.Currency))
            .Select(group => new WeeklyServiceIncome(group.Key.Week, group.Key.Currency,
                group.Sum(row => row.Amount), group.Count()))
            .OrderBy(item => item.WeekStart).ThenBy(item => item.Currency).ToList();
    }

    public IReadOnlyList<WeeklyAccountFlow> ListPersonalAccountFlow(DateOnly from, DateOnly through)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath, Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.OccurredAt, m.CurrencyCode, m.Amount, m.Type
            FROM Movements m JOIN Accounts a ON a.Id = m.AccountId
            WHERE a.Name = 'Cuenta Personal' COLLATE NOCASE
              AND m.Type IN (0, 1) AND substr(m.OccurredAt, 1, 10) >= $from
              AND substr(m.OccurredAt, 1, 10) <= $through
            """;
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$through", through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        using var reader = command.ExecuteReader();
        var rows = new List<(DateOnly Date, string Currency, decimal Amount, int Type)>();
        while (reader.Read())
            rows.Add((DateOnly.FromDateTime(DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture).ToLocalTime().Date),
                reader.GetString(1), decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture), reader.GetInt32(3)));
        return rows.GroupBy(row => (Week: row.Date.AddDays(-((int)row.Date.DayOfWeek + 6) % 7), row.Currency))
            .Select(group => new WeeklyAccountFlow(group.Key.Week, group.Key.Currency,
                group.Where(row => row.Type == 0).Sum(row => Math.Abs(row.Amount)),
                group.Where(row => row.Type == 1).Sum(row => Math.Abs(row.Amount))))
            .OrderBy(item => item.WeekStart).ThenBy(item => item.Currency).ToList();
    }
}
