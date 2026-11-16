using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;

namespace NexoCash.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    private static string? _activePath;
    private static DateOnly? _startDate;
    public static string LegacyPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexoCash", "nexocash.db");
    private static string SelectionPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NexoCash", "selected-database.json");
    private static string LegacySelectionPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexoCash", "selected-database.json");
    public static string DatabasePath => _activePath ?? throw new InvalidOperationException("No hay una base de datos activa.");
    public static DateOnly StartDate => _startDate ?? throw new InvalidOperationException("La base de datos no tiene fecha de inicio.");

    public static string? LoadSelectedPath()
    {
        return ReadSelection(SelectionPath) ?? ReadSelection(LegacySelectionPath);
    }

    private static string? ReadSelection(string selectionPath)
    {
        if (!File.Exists(selectionPath)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(selectionPath));
            var path = document.RootElement.GetProperty("Path").GetString();
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }

    public static void Activate(string path)
    {
        var start = GetStartDate(path) ?? throw new InvalidOperationException("Elige una fecha de inicio para esta base de datos.");
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(SelectionPath)!);
        var temporary = SelectionPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new Dictionary<string, string> { ["Path"] = fullPath }));
        File.Move(temporary, SelectionPath, true);
        _activePath = fullPath;
        _startDate = start;
    }

    public static void Initialize(string path)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var existed = File.Exists(path);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt32(versionCommand.ExecuteScalar());
        if (existed && version == 0)
        {
            using var tables = connection.CreateCommand();
            tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
            if (Convert.ToInt32(tables.ExecuteScalar()) > 0)
                throw new InvalidOperationException("El archivo no es una base de datos de NexoCash compatible.");
        }
        if (version == 16) return;
        if (version is not (0 or 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15))
            throw new InvalidOperationException(
                $"Database version {version} is not supported by this application.");

        if (version is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14)
        {
            using var foreignKeys = connection.CreateCommand();
            foreignKeys.CommandText = "PRAGMA foreign_keys = OFF";
            foreignKeys.ExecuteNonQuery();
        }

        try
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            if (version == 0)
            {
                command.CommandText = ReadSchema("Schema.sql");
                command.ExecuteNonQuery();
            }
            else
                for (var nextVersion = version + 1; nextVersion <= 16; nextVersion++)
                {
                    command.CommandText = ReadSchema($"Migration{nextVersion}.sql");
                    command.ExecuteNonQuery();
                }
            if (version != 0)
            {
                command.CommandText = "PRAGMA foreign_key_check";
                using var violations = command.ExecuteReader();
                if (violations.Read())
                    throw new InvalidOperationException("La migración encontró referencias inválidas en la base de datos.");
            }
            transaction.Commit();
        }
        finally
        {
            if (version is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14)
            {
                using var foreignKeys = connection.CreateCommand();
                foreignKeys.CommandText = "PRAGMA foreign_keys = ON";
                foreignKeys.ExecuteNonQuery();
            }
        }
    }

    public static DateOnly? GetStartDate(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'DatabaseProfile'";
        if (check.ExecuteScalar() is null) return null;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT StartDate FROM DatabaseProfile WHERE Id = 1";
        return command.ExecuteScalar() is string value
            ? DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }

    public static void SetStartDate(string path, DateOnly startDate)
    {
        if (startDate > DateOnly.FromDateTime(DateTime.Today))
            throw new InvalidOperationException("La fecha de inicio no puede ser futura.");
        if (GetStartDate(path) is not null)
            throw new InvalidOperationException("La fecha de inicio de esta base de datos ya está fijada.");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var earliest = connection.CreateCommand();
        earliest.Transaction = transaction;
        earliest.CommandText = """
            SELECT MIN(DateValue) FROM (
                SELECT MIN(Date) AS DateValue FROM Workdays
                UNION ALL SELECT MIN(substr(OccurredAt, 1, 10)) FROM Movements
                UNION ALL SELECT MIN(substr(OccurredAt, 1, 10)) FROM AgencyFundMovements
            )
            """;
        if (earliest.ExecuteScalar() is string first &&
            startDate > DateOnly.ParseExact(first, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            throw new InvalidOperationException($"Ya hay registros desde el {first}. Elige esa fecha o una anterior.");
        using var save = connection.CreateCommand();
        save.Transaction = transaction;
        save.CommandText = "INSERT INTO DatabaseProfile (Id, StartDate) VALUES (1, $date)";
        save.Parameters.AddWithValue("$date", startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        save.ExecuteNonQuery();
        transaction.Commit();
    }

    private static string ReadSchema(string fileName)
    {
        var assembly = typeof(DatabaseInitializer).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            $"NexoCash.Infrastructure.Persistence.{fileName}")
            ?? throw new InvalidOperationException("The embedded SQLite schema was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
