CREATE TABLE Services_new (
    Id TEXT PRIMARY KEY,
    WorkdayId TEXT NOT NULL REFERENCES Workdays(Id),
    Type INTEGER NOT NULL CHECK (Type IN (0, 2, 3)),
    Status INTEGER NOT NULL CHECK (Status IN (0, 1, 2)),
    PaymentMethod INTEGER NOT NULL CHECK (PaymentMethod IN (0, 1)),
    AgencyId TEXT REFERENCES Agencies(Id),
    CurrencyCode TEXT NOT NULL REFERENCES Currencies(Code),
    ExpectedPrice TEXT,
    CollectedAmount TEXT,
    AppliedCommissionPercent TEXT,
    CommissionAmount TEXT,
    CreatedAt TEXT NOT NULL,
    CompletedAt TEXT,
    Description TEXT,
    CHECK ((Type = 0 AND AgencyId IS NOT NULL)
        OR (Type IN (2, 3) AND AgencyId IS NULL))
);

INSERT INTO Services_new (
    Id, WorkdayId, Type, Status, PaymentMethod, AgencyId, CurrencyCode,
    ExpectedPrice, CollectedAmount, AppliedCommissionPercent, CommissionAmount,
    CreatedAt, CompletedAt, Description)
SELECT Id, WorkdayId, CASE WHEN Type = 1 THEN 3 ELSE Type END, Status,
       PaymentMethod, CASE WHEN Type = 0 THEN AgencyId ELSE NULL END, CurrencyCode,
       ExpectedPrice, CollectedAmount, AppliedCommissionPercent, CommissionAmount,
       CreatedAt, CompletedAt, Description
FROM Services;

DROP TABLE Services;
ALTER TABLE Services_new RENAME TO Services;
CREATE INDEX IX_Services_Workday ON Services(WorkdayId);
CREATE INDEX IX_Services_Agency ON Services(AgencyId);
DROP TABLE Customers;
PRAGMA user_version = 14;
