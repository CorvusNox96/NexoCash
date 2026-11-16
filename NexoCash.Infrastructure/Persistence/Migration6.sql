CREATE TABLE IF NOT EXISTS VehicleRentSettings (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    AccountId TEXT NOT NULL REFERENCES Accounts(Id),
    DailyCupAmount TEXT NOT NULL,
    StartedOn TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS VehicleRentMovementDates (
    MovementId TEXT PRIMARY KEY REFERENCES Movements(Id),
    WorkDate TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_VehicleRentMovementDates_Date ON VehicleRentMovementDates(WorkDate);

CREATE TABLE IF NOT EXISTS VehicleRentSettlements (
    Id TEXT PRIMARY KEY,
    WeekStart TEXT NOT NULL,
    ThroughDate TEXT NOT NULL,
    MovementId TEXT NOT NULL UNIQUE REFERENCES Movements(Id),
    AmountCup TEXT NOT NULL,
    SettledAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_VehicleRentSettlements_Week ON VehicleRentSettlements(WeekStart);

PRAGMA user_version = 6;
