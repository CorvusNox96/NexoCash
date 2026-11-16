CREATE TABLE IF NOT EXISTS DatabaseProfile (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    StartDate TEXT NOT NULL
);

PRAGMA user_version = 8;
