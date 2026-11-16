CREATE TABLE CashInventoryCorrections (
    DenominationId TEXT PRIMARY KEY REFERENCES Denominations(Id),
    QuantityDelta INTEGER NOT NULL DEFAULT 0
);
DROP TABLE IF EXISTS CashReconciliations;
DROP TABLE IF EXISTS CashCountDetails;
DROP TABLE IF EXISTS CashCounts;
PRAGMA user_version = 12;
