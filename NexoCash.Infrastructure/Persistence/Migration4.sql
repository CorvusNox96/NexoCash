CREATE TABLE ServiceConsolidationBanknotes (
    ConsolidationId TEXT NOT NULL REFERENCES ServiceConsolidations(Id),
    DenominationId TEXT NOT NULL REFERENCES Denominations(Id),
    Quantity INTEGER NOT NULL CHECK (Quantity > 0),
    PRIMARY KEY (ConsolidationId, DenominationId)
);
PRAGMA user_version = 4;
