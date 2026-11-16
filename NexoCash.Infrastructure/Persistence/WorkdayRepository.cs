using System.Globalization;
using Microsoft.Data.Sqlite;
using NexoCash.Domain.Entities;

namespace NexoCash.Infrastructure.Persistence;

public sealed record WorkdayOverview(DateOnly Date, string Status, string Activity, int Services,
    int Finished, int Consolidated, string Reason)
{
    public string DateText => Date.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);
}

public sealed record RentWeekDay(DateOnly Date, string Activity, string Reason, int Services, decimal Due)
{
    public string DayName => Date.ToDateTime(TimeOnly.MinValue).ToString("dddd", CultureInfo.CurrentCulture);
    public string DateText => Date.ToString("dd/MM", CultureInfo.CurrentCulture);
    public string DueText => $"{Due:N2} CUP";
}

public sealed record RentWeek(DateOnly Start, DateOnly End, IReadOnlyList<RentWeekDay> Days,
    decimal DailyAmount, decimal Required, decimal Paid, decimal Balance, decimal EarlierUnpaid)
{
    public decimal Remaining => Math.Max(0, Required - Paid);
    public decimal AvailableForWeek => Math.Max(0, Balance - EarlierUnpaid);
    public decimal Missing => Math.Max(0, Remaining - AvailableForWeek);
    public decimal Extra => Math.Max(0, Balance - EarlierUnpaid - Remaining);
    public decimal PayableThrough(DateOnly date) => Math.Max(0,
        Days.Where(day => day.Date <= date).Sum(day => day.Due) - Paid);
}

public sealed class WorkdayRepository
{
    private readonly string _databasePath;
    public WorkdayRepository(string? databasePath = null) => _databasePath = databasePath ?? DatabaseInitializer.DatabasePath;

    public IReadOnlyList<WorkdayOverview> List()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT w.Date, w.Status, w.WasWorked, w.NoWorkReason,
                   COUNT(s.Id), SUM(CASE WHEN s.Status = 2 THEN 1 ELSE 0 END),
                   COUNT(ci.ServiceId)
            FROM Workdays w
            LEFT JOIN Services s ON s.WorkdayId = w.Id
            LEFT JOIN ServiceConsolidationItems ci ON ci.ServiceId = s.Id
            GROUP BY w.Id ORDER BY w.Date DESC
            """;
        using var reader = command.ExecuteReader();
        var result = new List<WorkdayOverview>();
        while (reader.Read()) result.Add(new(
            DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            reader.GetInt32(1) == 0 ? "Open" : "Closed",
            reader.IsDBNull(2) ? "Unspecified" : reader.GetInt32(2) == 1 ? "Worked" : "No work",
            reader.GetInt32(4), reader.IsDBNull(5) ? 0 : reader.GetInt32(5), reader.GetInt32(6),
            reader.IsDBNull(3) ? "" : reader.GetString(3)));
        return result;
    }

    public RentWeek GetWeek(DateOnly anyDate)
    {
        var start = WeekStart(anyDate);
        var end = start.AddDays(6);
        using var connection = Open();
        var days = new List<RentWeekDay>();
        var settings = new VehicleRentSettingsRepository(_databasePath).Get();
        var daily = settings?.DailyCupAmount ?? 9000m;
        for (var offset = 0; offset < 7; offset++)
        {
            var date = start.AddDays(offset);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT WasWorked, NoWorkReason
                FROM Workdays WHERE Date = $date
                """;
            command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            using var reader = command.ExecuteReader();
            bool? marked = null; var reason = "";
            if (reader.Read())
            {
                marked = reader.IsDBNull(0) ? null : reader.GetInt32(0) == 1;
                reason = reader.IsDBNull(1) ? "" : reader.GetString(1);
            }
            var activity = marked == true ? "Worked" : marked == false || date.DayOfWeek == DayOfWeek.Sunday
                ? "No work" : "Pending";
            days.Add(new(date, activity, reason, 0,
                activity == "Worked" && settings is not null && date >= settings.StartedOn ? daily : 0));
        }
        var paid = 0m;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT AmountCup FROM VehicleRentSettlements WHERE WeekStart = $week";
            command.Parameters.AddWithValue("$week", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            using var reader = command.ExecuteReader();
            while (reader.Read()) paid += decimal.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
        }
        var balance = 0m;
        if (settings is not null)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Amount FROM Movements WHERE AccountId = $account AND CurrencyCode = 'CUP'";
            command.Parameters.AddWithValue("$account", settings.AccountId.ToString());
            using var reader = command.ExecuteReader();
            while (reader.Read()) balance += decimal.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
        }
        var earlierUnpaid = 0m;
        if (settings is not null && start > settings.StartedOn)
        {
            var earlierWorked = 0;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM Workdays WHERE WasWorked = 1 AND Date >= $start AND Date < $week";
                command.Parameters.AddWithValue("$start", settings.StartedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$week", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                earlierWorked = Convert.ToInt32(command.ExecuteScalar());
            }
            var earlierPaid = 0m;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT AmountCup FROM VehicleRentSettlements WHERE WeekStart < $week";
                command.Parameters.AddWithValue("$week", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                using var reader = command.ExecuteReader();
                while (reader.Read()) earlierPaid += decimal.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
            }
            earlierUnpaid = Math.Max(0, earlierWorked * daily - earlierPaid);
        }
        return new(start, end, days, daily, days.Sum(day => day.Due), paid, balance, earlierUnpaid);
    }

    public void SetWorked(DateOnly date, bool worked, string? reason = null)
        => SetActivity(date, worked, reason);

    public void SetActivity(DateOnly date, bool? worked, string? reason = null)
    {
        if (DatabaseInitializer.GetStartDate(_databasePath) is { } start && date < start)
            throw new InvalidOperationException("No se pueden registrar días anteriores al inicio de esta base de datos.");
        if (date.DayOfWeek == DayOfWeek.Sunday)
            throw new InvalidOperationException("El domingo es día libre.");
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        using (var locked = connection.CreateCommand())
        {
            locked.Transaction = transaction;
            locked.CommandText = "SELECT 1 FROM VehicleRentDayConsolidations WHERE Date = $date";
            locked.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (locked.ExecuteScalar() is not null)
                throw new InvalidOperationException("Este día ya está consolidado y no se puede cambiar.");
        }
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Workdays (Id, Date, Status, WasWorked, NoWorkReason)
            VALUES ($id, $date, 0, $worked, $reason)
            ON CONFLICT(Date) DO UPDATE SET WasWorked = excluded.WasWorked,
                NoWorkReason = excluded.NoWorkReason
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$worked", worked is null ? DBNull.Value : worked.Value ? 1 : 0);
        command.Parameters.AddWithValue("$reason", worked == false ? (object?)reason?.Trim() ?? DBNull.Value : DBNull.Value);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public IReadOnlySet<DateOnly> GetConsolidatedDates(DateOnly anyDate)
    {
        var start = WeekStart(anyDate);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Date FROM VehicleRentDayConsolidations WHERE WeekStart = $week";
        command.Parameters.AddWithValue("$week", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        using var reader = command.ExecuteReader();
        var result = new HashSet<DateOnly>();
        while (reader.Read())
            result.Add(DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture));
        return result;
    }

    public void ConsolidateDays(IReadOnlyCollection<DateOnly> selectedDates,
        IReadOnlyDictionary<decimal, int>? banknotes = null)
    {
        if (selectedDates.Count == 0) throw new ArgumentException("Selecciona al menos un día para consolidar.");
        var dates = selectedDates.Distinct().OrderBy(date => date).ToList();
        var weekStart = WeekStart(dates[0]);
        if (DatabaseInitializer.GetStartDate(_databasePath) is { } start && dates.Any(date => date < start))
            throw new InvalidOperationException("No se pueden consolidar días anteriores al inicio de esta base de datos.");
        if (dates.Any(date => WeekStart(date) != weekStart || date.DayOfWeek == DayOfWeek.Sunday))
            throw new ArgumentException("Selecciona días de lunes a sábado de una misma semana.");

        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var workedDates = new List<DateOnly>();
        foreach (var date in dates)
        {
            using var status = connection.CreateCommand();
            status.Transaction = transaction;
            status.CommandText = "SELECT WasWorked FROM Workdays WHERE Date = $date";
            status.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            var value = status.ExecuteScalar();
            if (value is null or DBNull)
                throw new InvalidOperationException($"Define el estado del {date:dd/MM/yyyy} antes de consolidarlo.");
            if (Convert.ToInt32(value) == 1) workedDates.Add(date);

            using var existing = connection.CreateCommand();
            existing.Transaction = transaction;
            existing.CommandText = "SELECT 1 FROM VehicleRentDayConsolidations WHERE Date = $date";
            existing.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (existing.ExecuteScalar() is not null)
                throw new InvalidOperationException($"El día {date:dd/MM/yyyy} ya está consolidado.");
        }

        banknotes ??= new Dictionary<decimal, int>();
        string? accountId = null;
        var cashAccount = false;
        var daily = 9000m;
        if (workedDates.Count > 0)
        {
            using var settings = connection.CreateCommand();
            settings.Transaction = transaction;
            settings.CommandText = """
                SELECT s.AccountId, s.DailyCupAmount, a.Type FROM VehicleRentSettings s
                JOIN Accounts a ON a.Id = s.AccountId AND a.IsActive = 1 WHERE s.Id = 1
                """;
            using var reader = settings.ExecuteReader();
            if (!reader.Read())
                throw new InvalidOperationException("Asigna una cuenta activa para el alquiler en Configuración.");
            accountId = reader.GetString(0);
            daily = decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
            cashAccount = reader.GetInt32(2) == (int)AccountType.Cash;
            reader.Close();

            using var funds = connection.CreateCommand();
            funds.Transaction = transaction;
            funds.CommandText = "SELECT Amount FROM Movements WHERE AccountId = $account AND CurrencyCode = 'CUP'";
            funds.Parameters.AddWithValue("$account", accountId);
            using var balances = funds.ExecuteReader();
            var balance = 0m;
            while (balances.Read()) balance += decimal.Parse(balances.GetString(0), CultureInfo.InvariantCulture);
            if (balance < workedDates.Count * daily)
                throw new InvalidOperationException("La cuenta de alquiler no tiene suficientes CUP para estos días.");
        }

        var amountToPay = workedDates.Count * daily;
        if (banknotes.Count > 0)
        {
            if (!cashAccount)
                throw new InvalidOperationException("Solo una cuenta de efectivo puede usar billetes.");
            if (banknotes.Any(note => note.Key <= 0 || note.Value <= 0) ||
                banknotes.Sum(note => note.Key * note.Value) != amountToPay)
                throw new InvalidOperationException("Los billetes deben sumar exactamente el importe a extraer.");
        }

        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        Guid? movementId = null;
        if (amountToPay > 0)
        {
            movementId = Guid.NewGuid();
            using var movement = connection.CreateCommand();
            movement.Transaction = transaction;
            movement.CommandText = """
                INSERT INTO Movements (Id, AccountId, CurrencyCode, Amount, Type, OccurredAt, RecordedAt, Description)
                VALUES ($id, $account, 'CUP', $amount, 1, $at, $at, $description)
                """;
            movement.Parameters.AddWithValue("$id", movementId.Value.ToString());
            movement.Parameters.AddWithValue("$account", accountId!);
            movement.Parameters.AddWithValue("$amount", (-amountToPay).ToString(CultureInfo.InvariantCulture));
            movement.Parameters.AddWithValue("$at", now);
            movement.Parameters.AddWithValue("$description", $"Alquiler del vehículo · semana {weekStart:dd/MM/yyyy} · {workedDates.Count} día(s)");
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
                detail.Parameters.AddWithValue("$movement", movementId.Value.ToString());
                detail.Parameters.AddWithValue("$denomination", AccountRepository.GetActiveDenomination(connection, transaction, "CUP", note.Key));
                detail.Parameters.AddWithValue("$quantity", note.Value);
                detail.ExecuteNonQuery();
            }
        }
        foreach (var date in dates)
        {
            var amount = workedDates.Contains(date) ? daily : 0m;

            using var consolidated = connection.CreateCommand();
            consolidated.Transaction = transaction;
            consolidated.CommandText = """
                INSERT INTO VehicleRentDayConsolidations (Date, WeekStart, AmountCup, MovementId, ConsolidatedAt)
                VALUES ($date, $week, $amount, $movement, $at)
                """;
            consolidated.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            consolidated.Parameters.AddWithValue("$week", weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            consolidated.Parameters.AddWithValue("$amount", amount.ToString(CultureInfo.InvariantCulture));
            consolidated.Parameters.AddWithValue("$movement", amount > 0 ? movementId!.Value.ToString() : DBNull.Value);
            consolidated.Parameters.AddWithValue("$at", now);
            consolidated.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-((int)date.DayOfWeek + 6) % 7);

    public void SettleRent(DateOnly throughDate, decimal amount, IReadOnlyDictionary<decimal, int> banknotes)
    {
        if (amount <= 0) throw new ArgumentException("Enter a positive rent payment.");
        if (throughDate > DateOnly.FromDateTime(DateTime.Today))
            throw new ArgumentException("A rent payment cannot cover future workdays.");
        var weekStart = WeekStart(throughDate);
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        using var settings = connection.CreateCommand(); settings.Transaction = transaction;
        settings.CommandText = """
            SELECT r.AccountId, r.DailyCupAmount, a.Type, r.StartedOn FROM VehicleRentSettings r
            JOIN Accounts a ON a.Id = r.AccountId WHERE r.Id = 1 AND a.IsActive = 1
            """;
        string accountId; decimal daily; bool cash; DateOnly startedOn;
        using (var reader = settings.ExecuteReader())
        {
            if (!reader.Read()) throw new InvalidOperationException("Select an active vehicle rent account in Settings first.");
            accountId = reader.GetString(0);
            daily = decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
            cash = reader.GetInt32(2) == (int)AccountType.Cash;
            startedOn = DateOnly.Parse(reader.GetString(3), CultureInfo.InvariantCulture);
        }
        if (throughDate < startedOn)
            throw new InvalidOperationException("Vehicle rent tracking starts on the configured week.");
        var previousDue = 0m;
        if (weekStart > startedOn)
        {
            using var previous = connection.CreateCommand(); previous.Transaction = transaction;
            previous.CommandText = "SELECT COUNT(*) FROM Workdays WHERE WasWorked = 1 AND Date >= $start AND Date < $week";
            previous.Parameters.AddWithValue("$start", startedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            previous.Parameters.AddWithValue("$week", weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            previousDue = Convert.ToInt32(previous.ExecuteScalar()) * daily;
            previous.CommandText = "SELECT AmountCup FROM VehicleRentSettlements WHERE WeekStart < $week";
            using var previousReader = previous.ExecuteReader();
            while (previousReader.Read()) previousDue -= decimal.Parse(previousReader.GetString(0), CultureInfo.InvariantCulture);
        }
        if (previousDue > 0)
            throw new InvalidOperationException("Settle the older unpaid vehicle rent week first.");
        var worked = 0;
        using (var days = connection.CreateCommand())
        {
            days.Transaction = transaction;
            days.CommandText = "SELECT Date FROM Workdays WHERE WasWorked = 1 AND Date >= $start AND Date <= $through";
            days.Parameters.AddWithValue("$start", (weekStart > startedOn ? weekStart : startedOn).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            days.Parameters.AddWithValue("$through", throughDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            using var reader = days.ExecuteReader(); while (reader.Read()) worked++;
        }
        var paid = 0m;
        using (var payments = connection.CreateCommand())
        {
            payments.Transaction = transaction;
            payments.CommandText = "SELECT AmountCup FROM VehicleRentSettlements WHERE WeekStart = $week";
            payments.Parameters.AddWithValue("$week", weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            using var reader = payments.ExecuteReader();
            while (reader.Read()) paid += decimal.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
        }
        if (amount > worked * daily - paid)
            throw new InvalidOperationException("The payment exceeds rent due for worked days through the selected date.");
        var balance = 0m;
        using (var movements = connection.CreateCommand())
        {
            movements.Transaction = transaction;
            movements.CommandText = "SELECT Amount FROM Movements WHERE AccountId = $account AND CurrencyCode = 'CUP'";
            movements.Parameters.AddWithValue("$account", accountId);
            using var reader = movements.ExecuteReader();
            while (reader.Read()) balance += decimal.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
        }
        if (balance < amount) throw new InvalidOperationException("The vehicle rent account has insufficient CUP.");
        if (cash && (banknotes.Count == 0 || banknotes.Sum(note => note.Key * note.Value) != amount))
            throw new InvalidOperationException("Count the exact CUP banknotes for this rent payment.");
        if (!cash && banknotes.Count > 0)
            throw new InvalidOperationException("A virtual rent account cannot use physical banknotes.");
        foreach (var note in banknotes)
            if (note.Key <= 0 || note.Value <= 0 || AccountRepository.GetBanknoteQuantity(connection, transaction, "CUP", note.Key) < note.Value)
                throw new InvalidOperationException($"Not enough {note.Key} CUP banknotes are available.");
        var movementId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow.ToString("O");
        using (var movement = connection.CreateCommand())
        {
            movement.Transaction = transaction;
            movement.CommandText = """
                INSERT INTO Movements (Id, AccountId, CurrencyCode, Amount, Type, OccurredAt, RecordedAt, Description)
                VALUES ($id, $account, 'CUP', $amount, 1, $at, $at, 'Vehicle rent weekly payment')
                """;
            movement.Parameters.AddWithValue("$id", movementId.ToString());
            movement.Parameters.AddWithValue("$account", accountId);
            movement.Parameters.AddWithValue("$amount", (-amount).ToString(CultureInfo.InvariantCulture));
            movement.Parameters.AddWithValue("$at", now);
            movement.ExecuteNonQuery();
        }
        foreach (var note in banknotes)
        {
            using var detail = connection.CreateCommand(); detail.Transaction = transaction;
            detail.CommandText = "INSERT INTO BanknoteMovementDetails (Id, MovementId, DenominationId, Quantity) VALUES ($id, $movement, $denomination, $quantity)";
            detail.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            detail.Parameters.AddWithValue("$movement", movementId.ToString());
            detail.Parameters.AddWithValue("$denomination", AccountRepository.GetActiveDenomination(connection, transaction, "CUP", note.Key));
            detail.Parameters.AddWithValue("$quantity", note.Value);
            detail.ExecuteNonQuery();
        }
        using (var settlement = connection.CreateCommand())
        {
            settlement.Transaction = transaction;
            settlement.CommandText = """
                INSERT INTO VehicleRentSettlements (Id, WeekStart, ThroughDate, MovementId, AmountCup, SettledAt)
                VALUES ($id, $week, $through, $movement, $amount, $at)
                """;
            settlement.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            settlement.Parameters.AddWithValue("$week", weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            settlement.Parameters.AddWithValue("$through", throughDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            settlement.Parameters.AddWithValue("$movement", movementId.ToString());
            settlement.Parameters.AddWithValue("$amount", amount.ToString(CultureInfo.InvariantCulture));
            settlement.Parameters.AddWithValue("$at", now);
            settlement.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void MarkNoWork(DateOnly date, string reason)
    {
        SetWorked(date, false, reason);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = _databasePath, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true }.ToString());
        connection.Open();
        return connection;
    }
}
