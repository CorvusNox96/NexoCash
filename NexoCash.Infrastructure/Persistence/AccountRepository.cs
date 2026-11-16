using System.Globalization;
using Microsoft.Data.Sqlite;
using NexoCash.Domain.Entities;

namespace NexoCash.Infrastructure.Persistence;

public sealed record AccountBalance(string Currency, decimal Amount)
{
    public string Label => $"{Amount:N2} {Currency}";
}

public sealed record AccountListItem(Account Account, IReadOnlyList<AccountBalance> Balances)
{
    public string TypeLabel => Account.Type == AccountType.Cash ? "Cash" : "Virtual";
}

public sealed record AccountActivityItem(DateTimeOffset OccurredAt, string Type, string From,
    string To, decimal Amount, string Currency, string Description)
{
    public DateOnly Day => DateOnly.FromDateTime(OccurredAt.ToLocalTime().DateTime);
    public string TimeText => OccurredAt.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
    public string DateText => OccurredAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
    public string AmountText => $"{Amount:N2} {Currency}";
}

public sealed class AccountRepository
{
    private readonly string _databasePath;

    public AccountRepository(string? databasePath = null) =>
        _databasePath = databasePath ?? DatabaseInitializer.DatabasePath;

    public IReadOnlyList<AccountListItem> ListActive()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Type FROM Accounts WHERE IsActive = 1 ORDER BY Name COLLATE NOCASE";
        using var reader = command.ExecuteReader();
        var accounts = new List<Account>();
        while (reader.Read()) accounts.Add(new Account
        {
            Id = Guid.Parse(reader.GetString(0)), Name = reader.GetString(1),
            Type = (AccountType)reader.GetInt32(2)
        });
        reader.Close();
        return accounts.Select(a => new AccountListItem(a, GetBalances(connection, a.Id))).ToList();
    }

    public IReadOnlyList<string> ListCurrencies()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Code FROM Currencies WHERE IsActive = 1 ORDER BY IsPrimaryCurrency DESC, Code";
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    public IReadOnlyList<AccountActivityItem> ListMovements()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.OccurredAt, m.Type, m.Amount, m.CurrencyCode, m.Description,
                   a.Name, destination.Name,
                   other.Amount, other.CurrencyCode
            FROM Movements m
            JOIN Accounts a ON a.Id = m.AccountId
            LEFT JOIN Movements other ON m.Type IN (2, 3) AND other.OperationId = m.OperationId
                AND other.Id <> m.Id AND other.Type = m.Type
            LEFT JOIN Accounts destination ON destination.Id = other.AccountId
            WHERE m.Type IN (0, 1, 2, 3, 4)
            ORDER BY m.OccurredAt DESC, m.RecordedAt DESC
            """;
        using var reader = command.ExecuteReader();
        var result = new List<AccountActivityItem>();
        while (reader.Read())
        {
            var type = (MovementType)reader.GetInt32(1);
            var signedAmount = decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
            if (type is MovementType.Transfer or MovementType.Exchange && signedAmount > 0) continue;
            var account = reader.GetString(5);
            var destination = reader.IsDBNull(6) ? "" : reader.GetString(6);
            if (type == MovementType.Exchange && !reader.IsDBNull(7))
                destination += $" · {decimal.Parse(reader.GetString(7), CultureInfo.InvariantCulture):N2} {reader.GetString(8)}";
            result.Add(new(
                DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
                type switch
                {
                    MovementType.Income => "Income",
                    MovementType.Expense => "Withdrawal",
                    MovementType.Transfer => "Transfer",
                    MovementType.Exchange => "Currency exchange",
                    _ => "Cash adjustment"
                },
                type == MovementType.Income || type == MovementType.Adjustment && signedAmount > 0 ? "External" : account,
                type is MovementType.Transfer or MovementType.Exchange ? destination
                    : type == MovementType.Income || type == MovementType.Adjustment && signedAmount > 0 ? account : "External",
                decimal.Abs(signedAmount), reader.GetString(3), reader.IsDBNull(4) ? "" : reader.GetString(4)));
        }
        return result;
    }

    public void Transfer(Guid sourceId, Guid destinationId, string currency, decimal amount,
        IReadOnlyDictionary<decimal, int> banknotes, string? description)
    {
        if (sourceId == destinationId) throw new ArgumentException("Choose two different accounts.");
        if (amount <= 0) throw new ArgumentException("Enter an amount greater than zero.");
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Choose a currency.");
        if (banknotes.Any(note => note.Key <= 0 || note.Value <= 0))
            throw new ArgumentException("Banknote values and quantities must be positive.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        EnsureCurrencyActive(connection, transaction, currency);
        var sourceCash = GetAccountType(connection, transaction, sourceId);
        var destinationCash = GetAccountType(connection, transaction, destinationId);
        var balance = GetBalances(connection, sourceId, transaction).FirstOrDefault(b => b.Currency == currency)?.Amount ?? 0m;
        if (balance < amount) throw new InvalidOperationException("The source account has insufficient funds in this currency.");
        if (sourceCash == destinationCash && banknotes.Count > 0)
            throw new InvalidOperationException("Banknotes are only needed when money moves between cash and virtual accounts.");
        if (sourceCash != destinationCash &&
            (banknotes.Count == 0 || banknotes.Sum(note => note.Key * note.Value) != amount))
            throw new InvalidOperationException("Banknotes must equal the transfer amount.");
        if (sourceCash && !destinationCash)
            foreach (var note in banknotes)
                if (GetBanknoteQuantity(connection, transaction, currency, note.Key) < note.Value)
                    throw new InvalidOperationException($"Not enough {note.Key} {currency} banknotes are available.");

        var operationId = Guid.NewGuid().ToString();
        var outgoingId = Guid.NewGuid().ToString();
        var incomingId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToString("O");
        var detail = string.IsNullOrWhiteSpace(description) ? "Account transfer" : description.Trim();
        Execute(connection, transaction, """
            INSERT INTO Movements (Id, AccountId, CurrencyCode, Amount, Type, OccurredAt, RecordedAt, OperationId, Description)
            VALUES ($id, $account, $currency, $amount, 2, $at, $at, $operation, $description)
            """, ("$id", outgoingId), ("$account", sourceId.ToString()), ("$currency", currency),
            ("$amount", (-amount).ToString(CultureInfo.InvariantCulture)), ("$at", now),
            ("$operation", operationId), ("$description", detail));
        Execute(connection, transaction, """
            INSERT INTO Movements (Id, AccountId, CurrencyCode, Amount, Type, OccurredAt, RecordedAt, OperationId, Description)
            VALUES ($id, $account, $currency, $amount, 2, $at, $at, $operation, $description)
            """, ("$id", incomingId), ("$account", destinationId.ToString()), ("$currency", currency),
            ("$amount", amount.ToString(CultureInfo.InvariantCulture)), ("$at", now),
            ("$operation", operationId), ("$description", detail));
        if (sourceCash != destinationCash)
        {
            var cashMovementId = sourceCash ? outgoingId : incomingId;
            foreach (var note in banknotes)
            {
                var denominationId = GetActiveDenomination(connection, transaction, currency, note.Key);
                Execute(connection, transaction, "INSERT INTO BanknoteMovementDetails (Id, MovementId, DenominationId, Quantity) VALUES ($id, $movement, $denomination, $quantity)",
                    ("$id", Guid.NewGuid().ToString()), ("$movement", cashMovementId),
                    ("$denomination", denominationId), ("$quantity", note.Value));
            }
        }
        transaction.Commit();
    }

    private static bool GetAccountType(SqliteConnection connection, SqliteTransaction transaction, Guid id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Type FROM Accounts WHERE Id = $id AND IsActive = 1";
        command.Parameters.AddWithValue("$id", id.ToString());
        var result = command.ExecuteScalar();
        if (result is null) throw new InvalidOperationException("An account is no longer available.");
        return Convert.ToInt32(result) == (int)AccountType.Cash;
    }

    private static void EnsureCurrencyActive(SqliteConnection connection, SqliteTransaction transaction, string code)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT IsActive FROM Currencies WHERE Code = $code";
        command.Parameters.AddWithValue("$code", code);
        if (Convert.ToInt32(command.ExecuteScalar()) != 1)
            throw new InvalidOperationException("The selected currency is not active.");
    }

    public Guid Save(Guid? accountId, string name, AccountType type)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("Introduce un nombre para la cuenta.");
        if (!Enum.IsDefined(type)) throw new ArgumentException("Choose a valid account type.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var id = accountId ?? Guid.NewGuid();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (accountId is null)
            command.CommandText = "INSERT INTO Accounts (Id, Name, Type) VALUES ($id, $name, $type)";
        else
        {
            command.CommandText = "UPDATE Accounts SET Name = $name, Type = $type WHERE Id = $id AND IsActive = 1";
            using var check = connection.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = "SELECT Type FROM Accounts WHERE Id = $id AND IsActive = 1";
            check.Parameters.AddWithValue("$id", id.ToString());
            var previous = check.ExecuteScalar();
            if (previous is null) throw new InvalidOperationException("The account is no longer available.");
            if (Convert.ToInt32(previous) != (int)type && HasMovements(connection, transaction, id))
                throw new InvalidOperationException("An account with movements cannot change between cash and virtual.");
        }
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$type", (int)type);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("The account could not be saved.");
        transaction.Commit();
        return id;
    }

    public void Delete(Guid id)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using (var rentCheck = connection.CreateCommand())
        {
            rentCheck.Transaction = transaction;
            rentCheck.CommandText = "SELECT 1 FROM VehicleRentSettings WHERE AccountId = $id";
            rentCheck.Parameters.AddWithValue("$id", id.ToString());
            if (rentCheck.ExecuteScalar() is not null)
                throw new InvalidOperationException("Choose a different vehicle rent account in Settings before deleting this account.");
        }
        if (GetBalances(connection, id, transaction).Any(b => b.Amount != 0))
            throw new InvalidOperationException("Move or withdraw the remaining balances before deleting this account.");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE Accounts SET IsActive = 0 WHERE Id = $id AND IsActive = 1";
        command.Parameters.AddWithValue("$id", id.ToString());
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("The account is no longer available.");
        transaction.Commit();
    }

    public void Record(Guid accountId, string currency, decimal amount, bool isIncome,
        IReadOnlyDictionary<decimal, int> banknotes, string? description)
    {
        if (amount <= 0) throw new ArgumentException("Enter an amount greater than zero.");
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Choose a currency.");
        if (banknotes.Any(n => n.Key <= 0 || n.Value <= 0)) throw new ArgumentException("Banknote values and quantities must be positive.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        EnsureCurrencyActive(connection, transaction, currency);
        bool isCash;
        using (var account = connection.CreateCommand())
        {
            account.Transaction = transaction;
            account.CommandText = "SELECT Type FROM Accounts WHERE Id = $id AND IsActive = 1";
            account.Parameters.AddWithValue("$id", accountId.ToString());
            var result = account.ExecuteScalar();
            if (result is null) throw new InvalidOperationException("The account is no longer available.");
            isCash = Convert.ToInt32(result) == (int)AccountType.Cash;
        }
        if (isCash && banknotes.Count > 0 && banknotes.Sum(n => n.Key * n.Value) != amount)
            throw new InvalidOperationException("Banknotes must add up to the cash amount.");
        if (!isCash && banknotes.Count != 0)
            throw new InvalidOperationException("Virtual account movements cannot include banknotes.");
        if (!isIncome)
        {
            var balance = GetBalances(connection, accountId, transaction).FirstOrDefault(b => b.Currency == currency)?.Amount ?? 0m;
            if (balance < amount) throw new InvalidOperationException("The account has insufficient funds in this currency.");
            if (isCash)
            {
                foreach (var note in banknotes)
                {
                    var available = GetBanknoteQuantity(connection, transaction, currency, note.Key);
                    if (available < note.Value)
                        throw new InvalidOperationException($"Only {available} banknotes of {note.Key} {currency} are available.");
                }
            }
        }
        var movementId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("O");
        Execute(connection, transaction, """
            INSERT INTO Movements (Id, AccountId, CurrencyCode, Amount, Type, OccurredAt, RecordedAt, Description)
            VALUES ($id, $account, $currency, $amount, $type, $at, $at, $description)
            """, ("$id", movementId.ToString()), ("$account", accountId.ToString()),
            ("$currency", currency), ("$amount", (isIncome ? amount : -amount).ToString(CultureInfo.InvariantCulture)),
            ("$type", isIncome ? (int)MovementType.Income : (int)MovementType.Expense), ("$at", now),
            ("$description", string.IsNullOrWhiteSpace(description) ? (isIncome ? "Manual deposit" : "Manual withdrawal") : description.Trim()));
        foreach (var note in banknotes)
        {
            var denominationId = GetActiveDenomination(connection, transaction, currency, note.Key);
            Execute(connection, transaction, "INSERT INTO BanknoteMovementDetails (Id, MovementId, DenominationId, Quantity) VALUES ($id, $movement, $denomination, $quantity)",
                ("$id", Guid.NewGuid().ToString()), ("$movement", movementId.ToString()),
                ("$denomination", denominationId), ("$quantity", note.Value));
        }
        transaction.Commit();
    }

    public void RecordCashDeposit(Guid accountId, decimal expectedCup,
        IReadOnlyDictionary<string, IReadOnlyDictionary<decimal, int>> counts, string? description)
    {
        if (expectedCup <= 0 || counts.Count == 0)
            throw new ArgumentException("Enter a positive amount and at least one banknote.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        if (!GetAccountType(connection, transaction, accountId))
            throw new InvalidOperationException("Choose a cash account for banknote deposits.");
        var amounts = new Dictionary<string, decimal>();
        foreach (var (currency, notes) in counts)
        {
            EnsureCurrencyActive(connection, transaction, currency);
            if (notes.Count == 0 || notes.Any(note => note.Key <= 0 || note.Value <= 0))
                throw new InvalidOperationException("Banknote values and quantities must be positive.");
            amounts.Add(currency, notes.Sum(note => note.Key * note.Value));
        }
        if (CashValuationRepository.ToCup(connection, transaction, amounts) != expectedCup)
            throw new InvalidOperationException("The counted banknotes do not match the expected CUP value. Check the exchange rates and count again.");
        var now = DateTimeOffset.UtcNow.ToString("O");
        var operationId = Guid.NewGuid().ToString();
        foreach (var (currency, notes) in counts)
        {
            var movementId = Guid.NewGuid().ToString();
            Execute(connection, transaction, """
                INSERT INTO Movements (Id, AccountId, CurrencyCode, Amount, Type, OccurredAt, RecordedAt, OperationId, Description)
                VALUES ($id, $account, $currency, $amount, 0, $at, $at, $operation, $description)
                """, ("$id", movementId), ("$account", accountId.ToString()), ("$currency", currency),
                ("$amount", amounts[currency].ToString(CultureInfo.InvariantCulture)), ("$at", now),
                ("$operation", operationId),
                ("$description", string.IsNullOrWhiteSpace(description) ? "Manual cash deposit" : description.Trim()));
            foreach (var note in notes)
            {
                var denominationId = GetActiveDenomination(connection, transaction, currency, note.Key);
                Execute(connection, transaction, "INSERT INTO BanknoteMovementDetails (Id, MovementId, DenominationId, Quantity) VALUES ($id, $movement, $denomination, $quantity)",
                    ("$id", Guid.NewGuid().ToString()), ("$movement", movementId),
                    ("$denomination", denominationId), ("$quantity", note.Value));
            }
        }
        transaction.Commit();
    }

    private static bool HasMovements(SqliteConnection connection, SqliteTransaction transaction, Guid id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Movements WHERE AccountId = $id)";
        command.Parameters.AddWithValue("$id", id.ToString());
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    private static IReadOnlyList<AccountBalance> GetBalances(SqliteConnection connection, Guid id, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT CurrencyCode, Amount FROM Movements WHERE AccountId = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        using var reader = command.ExecuteReader();
        var balances = new Dictionary<string, decimal>();
        while (reader.Read())
        {
            var currency = reader.GetString(0);
            balances[currency] = balances.GetValueOrDefault(currency) + decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
        }
        return balances.Where(b => b.Value != 0).OrderBy(b => b.Key)
            .Select(b => new AccountBalance(b.Key, b.Value)).ToList();
    }

    internal static int GetBanknoteQuantity(SqliteConnection connection, SqliteTransaction transaction, string currency, decimal value)
        => CashRepository.PhysicalQuantity(connection, transaction, currency, value);

    internal static string GetActiveDenomination(SqliteConnection connection, SqliteTransaction transaction, string currency, decimal value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id, Value FROM Denominations WHERE CurrencyCode = $currency AND IsActive = 1";
        command.Parameters.AddWithValue("$currency", currency);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture) == value)
                return reader.GetString(0);
        throw new InvalidOperationException($"The {value} {currency} denomination is not active. Add it in Settings first.");
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
