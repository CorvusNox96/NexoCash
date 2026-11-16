DROP TABLE IF EXISTS VehicleRentMovementDates;
DROP INDEX IF EXISTS IX_Movements_Workday;
ALTER TABLE Movements DROP COLUMN WorkdayId;
PRAGMA user_version = 11;
