CREATE TABLE Accounts_new (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Type INTEGER NOT NULL CHECK (Type IN (0, 1)),
    IsActive INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1))
);
INSERT INTO Accounts_new (Id, Name, Type, IsActive)
SELECT Id, Name, Type, IsActive FROM Accounts;
DROP TABLE Accounts;
ALTER TABLE Accounts_new RENAME TO Accounts;

CREATE TABLE ServiceDirectWithdrawals_new (
    Id TEXT PRIMARY KEY,
    ConsolidationId TEXT NOT NULL REFERENCES ServiceConsolidations(Id),
    CurrencyCode TEXT NOT NULL REFERENCES Currencies(Code),
    PaymentMethod INTEGER NOT NULL CHECK (PaymentMethod IN (0, 1)),
    Amount TEXT NOT NULL,
    RecordedAt TEXT NOT NULL
);
INSERT INTO ServiceDirectWithdrawals_new
    (Id, ConsolidationId, CurrencyCode, PaymentMethod, Amount, RecordedAt)
SELECT Id, ConsolidationId, CurrencyCode, PaymentMethod, Amount, RecordedAt
FROM ServiceDirectWithdrawals;
DROP TABLE ServiceDirectWithdrawals;
ALTER TABLE ServiceDirectWithdrawals_new RENAME TO ServiceDirectWithdrawals;
CREATE INDEX IX_ServiceDirectWithdrawals_Consolidation
    ON ServiceDirectWithdrawals(ConsolidationId);

DROP TABLE Owners;
PRAGMA user_version = 10;
