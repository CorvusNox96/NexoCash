CREATE TABLE Services_new (
    Id TEXT PRIMARY KEY,
    WorkdayId TEXT NOT NULL REFERENCES Workdays(Id),
    Type INTEGER NOT NULL CHECK (Type IN (0, 1, 2, 3)),
    Status INTEGER NOT NULL CHECK (Status IN (0, 1, 2)),
    ScheduledTime TEXT NOT NULL,
    OriginAddress TEXT NOT NULL,
    DestinationAddress TEXT NOT NULL,
    PaymentMethod INTEGER NOT NULL CHECK (PaymentMethod IN (0, 1)),
    AgencyId TEXT REFERENCES Agencies(Id),
    CustomerId TEXT REFERENCES Customers(Id),
    CurrencyCode TEXT NOT NULL REFERENCES Currencies(Code),
    ExpectedPrice TEXT,
    CollectedAmount TEXT,
    AppliedCommissionPercent TEXT,
    CommissionAmount TEXT,
    CreatedAt TEXT NOT NULL,
    CompletedAt TEXT,
    Description TEXT,
    CHECK ((Type = 0 AND AgencyId IS NOT NULL AND CustomerId IS NULL)
        OR (Type = 1 AND CustomerId IS NOT NULL AND AgencyId IS NULL)
        OR (Type IN (2, 3) AND AgencyId IS NULL AND CustomerId IS NULL))
);

INSERT INTO Services_new (
    Id, WorkdayId, Type, Status, ScheduledTime, OriginAddress,
    DestinationAddress, PaymentMethod, AgencyId, CustomerId,
    CurrencyCode, ExpectedPrice, CollectedAmount, AppliedCommissionPercent,
    CommissionAmount, CreatedAt, CompletedAt, Description)
SELECT Id, WorkdayId, Type, Status, '00:00', '', '', 0, AgencyId, CustomerId,
       CurrencyCode, ExpectedPrice, CollectedAmount, AppliedCommissionPercent,
       CommissionAmount, CreatedAt, CompletedAt, Description
FROM Services;

DROP TABLE Services;
ALTER TABLE Services_new RENAME TO Services;
CREATE INDEX IX_Services_Workday ON Services(WorkdayId);
CREATE INDEX IX_Services_Agency ON Services(AgencyId);
PRAGMA user_version = 2;
