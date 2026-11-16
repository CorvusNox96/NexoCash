using Microsoft.Data.Sqlite;

namespace NexoCash.Infrastructure.Persistence;

public sealed record VehicleRentSettings(Guid AccountId, decimal DailyCupAmount, DateOnly StartedOn);

public sealed class VehicleRentSettingsRepository
{
    private readonly string _databasePath;
    public VehicleRentSettingsRepository(string? databasePath = null) =>
        _databasePath = databasePath ?? DatabaseInitializer.DatabasePath;

    public VehicleRentSettings? Get()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT AccountId, DailyCupAmount, StartedOn FROM VehicleRentSettings WHERE Id = 1";
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new VehicleRentSettings(Guid.Parse(reader.GetString(0)),
                decimal.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture),
                DateOnly.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture))
            : null;
    }

    public void SetAccount(Guid accountId)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT 1 FROM Accounts WHERE Id = $id AND IsActive = 1";
            check.Parameters.AddWithValue("$id", accountId.ToString());
            if (check.ExecuteScalar() is null) throw new InvalidOperationException("Choose an active account.");
        }
        using (var existing = connection.CreateCommand())
        {
            existing.Transaction = transaction;
            existing.CommandText = "SELECT AccountId FROM VehicleRentSettings WHERE Id = 1";
            if (existing.ExecuteScalar() is string oldAccount && oldAccount != accountId.ToString())
            {
                using var history = connection.CreateCommand(); history.Transaction = transaction;
                history.CommandText = "SELECT EXISTS(SELECT 1 FROM VehicleRentSettlements UNION SELECT 1 FROM VehicleRentDayConsolidations)";
                if (Convert.ToInt32(history.ExecuteScalar()) != 0)
                    throw new InvalidOperationException("The rent account has history. Keep this account for consistent weekly records.");
            }
        }
        using var save = connection.CreateCommand(); save.Transaction = transaction;
        save.CommandText = """
            INSERT INTO VehicleRentSettings (Id, AccountId, DailyCupAmount, StartedOn)
            VALUES (1, $account, '9000', $started)
            ON CONFLICT(Id) DO UPDATE SET AccountId = excluded.AccountId,
                StartedOn = CASE WHEN VehicleRentSettings.AccountId = excluded.AccountId
                    THEN VehicleRentSettings.StartedOn ELSE excluded.StartedOn END
            """;
        save.Parameters.AddWithValue("$account", accountId.ToString());
        save.Parameters.AddWithValue("$started", WorkdayRepository.WeekStart(DateOnly.FromDateTime(DateTime.Today))
            .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        save.ExecuteNonQuery(); transaction.Commit();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = _databasePath, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true }.ToString());
        connection.Open(); return connection;
    }
}
