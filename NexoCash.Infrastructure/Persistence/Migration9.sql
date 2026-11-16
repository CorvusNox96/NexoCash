CREATE TABLE IF NOT EXISTS ServiceDirectWithdrawals (
    Id TEXT PRIMARY KEY,
    ConsolidationId TEXT NOT NULL REFERENCES ServiceConsolidations(Id),
    OwnerId TEXT NOT NULL REFERENCES Owners(Id),
    CurrencyCode TEXT NOT NULL REFERENCES Currencies(Code),
    PaymentMethod INTEGER NOT NULL CHECK (PaymentMethod IN (0, 1)),
    Amount TEXT NOT NULL,
    RecordedAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_ServiceDirectWithdrawals_Consolidation ON ServiceDirectWithdrawals(ConsolidationId);

PRAGMA user_version = 9;
