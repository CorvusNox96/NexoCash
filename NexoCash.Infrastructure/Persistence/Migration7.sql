CREATE TABLE IF NOT EXISTS VehicleRentDayConsolidations (
    Date TEXT PRIMARY KEY,
    WeekStart TEXT NOT NULL,
    AmountCup TEXT NOT NULL,
    MovementId TEXT REFERENCES Movements(Id),
    ConsolidatedAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_VehicleRentDayConsolidations_Week ON VehicleRentDayConsolidations(WeekStart);

PRAGMA user_version = 7;
