CREATE TABLE IF NOT EXISTS Currencies (
    Code TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    IsPrimaryCurrency INTEGER NOT NULL DEFAULT 0 CHECK (IsPrimaryCurrency IN (0, 1)),
    IsActive INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1))
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_Currencies_Primary ON Currencies(IsPrimaryCurrency) WHERE IsPrimaryCurrency = 1;

CREATE TABLE IF NOT EXISTS Accounts (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Type INTEGER NOT NULL CHECK (Type IN (0, 1)),
    IsActive INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1))
);

CREATE TABLE IF NOT EXISTS Workdays (
    Id TEXT PRIMARY KEY,
    Date TEXT NOT NULL UNIQUE,
    Status INTEGER NOT NULL CHECK (Status IN (0, 1)),
    WasWorked INTEGER CHECK (WasWorked IN (0, 1)),
    NoWorkReason TEXT,
    ClosedAt TEXT
);

CREATE TABLE IF NOT EXISTS Agencies (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    AdministrationNumber TEXT,
    Photo BLOB,
    CurrentCommissionPercent TEXT,
    IsActive INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1))
);

CREATE TABLE IF NOT EXISTS Services (
    Id TEXT PRIMARY KEY,
    WorkdayId TEXT NOT NULL REFERENCES Workdays(Id),
    Type INTEGER NOT NULL CHECK (Type IN (0, 2, 3)),
    Status INTEGER NOT NULL CHECK (Status IN (0, 2)),
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
CREATE INDEX IF NOT EXISTS IX_Services_Workday ON Services(WorkdayId);
CREATE INDEX IF NOT EXISTS IX_Services_Agency ON Services(AgencyId);

CREATE TABLE IF NOT EXISTS ServiceConsolidations (
    Id TEXT PRIMARY KEY,
    ConsolidatedAt TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS ServiceConsolidationItems (
    ConsolidationId TEXT NOT NULL REFERENCES ServiceConsolidations(Id),
    ServiceId TEXT NOT NULL UNIQUE REFERENCES Services(Id),
    PRIMARY KEY (ConsolidationId, ServiceId)
);
CREATE TABLE IF NOT EXISTS ServiceConsolidationBanknotes (
    ConsolidationId TEXT NOT NULL REFERENCES ServiceConsolidations(Id),
    DenominationId TEXT NOT NULL REFERENCES Denominations(Id),
    Quantity INTEGER NOT NULL CHECK (Quantity > 0),
    PRIMARY KEY (ConsolidationId, DenominationId)
);

CREATE TABLE IF NOT EXISTS Closes (
    Id TEXT PRIMARY KEY,
    WorkdayId TEXT NOT NULL UNIQUE REFERENCES Workdays(Id),
    ClosedAt TEXT NOT NULL,
    Notes TEXT
);

CREATE TABLE IF NOT EXISTS Movements (
    Id TEXT PRIMARY KEY,
    AccountId TEXT NOT NULL REFERENCES Accounts(Id),
    CurrencyCode TEXT NOT NULL REFERENCES Currencies(Code),
    Amount TEXT NOT NULL,
    Type INTEGER NOT NULL CHECK (Type BETWEEN 0 AND 4),
    OccurredAt TEXT NOT NULL,
    RecordedAt TEXT NOT NULL,
    ServiceId TEXT REFERENCES Services(Id),
    OperationId TEXT,
    Description TEXT
);
CREATE INDEX IF NOT EXISTS IX_Movements_Account_Currency ON Movements(AccountId, CurrencyCode);
CREATE INDEX IF NOT EXISTS IX_Movements_Operation ON Movements(OperationId);

CREATE TABLE IF NOT EXISTS AgencyFundMovements (
    Id TEXT PRIMARY KEY,
    AgencyId TEXT NOT NULL REFERENCES Agencies(Id),
    ServiceId TEXT REFERENCES Services(Id),
    AccountMovementId TEXT REFERENCES Movements(Id),
    TransactionNumber TEXT,
    SurchargePercent TEXT,
    CurrencyCode TEXT NOT NULL REFERENCES Currencies(Code),
    Amount TEXT NOT NULL,
    Type INTEGER NOT NULL CHECK (Type BETWEEN 0 AND 2),
    OccurredAt TEXT NOT NULL,
    Description TEXT
);
CREATE INDEX IF NOT EXISTS IX_AgencyFund_Agency_Currency ON AgencyFundMovements(AgencyId, CurrencyCode);
CREATE UNIQUE INDEX IF NOT EXISTS IX_AgencyFund_TransactionNumber ON AgencyFundMovements(AgencyId, TransactionNumber) WHERE TransactionNumber IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS IX_AgencyFund_ServiceFee
    ON AgencyFundMovements(ServiceId) WHERE Type = 1 AND ServiceId IS NOT NULL;

CREATE TABLE IF NOT EXISTS CloseCurrencyTotals (
    Id TEXT PRIMARY KEY,
    CloseId TEXT NOT NULL REFERENCES Closes(Id),
    CurrencyCode TEXT NOT NULL REFERENCES Currencies(Code),
    TotalIncome TEXT NOT NULL,
    TotalExpenses TEXT NOT NULL,
    NetResult TEXT NOT NULL,
    UNIQUE (CloseId, CurrencyCode)
);

CREATE TABLE IF NOT EXISTS CloseDistributions (
    Id TEXT PRIMARY KEY,
    CloseId TEXT NOT NULL REFERENCES Closes(Id),
    AccountId TEXT NOT NULL REFERENCES Accounts(Id),
    CurrencyCode TEXT NOT NULL REFERENCES Currencies(Code),
    Amount TEXT NOT NULL,
    OperationId TEXT,
    Concept TEXT
);
CREATE INDEX IF NOT EXISTS IX_CloseDistributions_Close ON CloseDistributions(CloseId);

CREATE TABLE IF NOT EXISTS ExchangeRates (
    Id TEXT PRIMARY KEY,
    FromCurrencyCode TEXT NOT NULL REFERENCES Currencies(Code),
    ToCurrencyCode TEXT NOT NULL REFERENCES Currencies(Code),
    Value TEXT NOT NULL,
    EffectiveFrom TEXT NOT NULL,
    CHECK (FromCurrencyCode <> ToCurrencyCode)
);
CREATE INDEX IF NOT EXISTS IX_ExchangeRates_Pair_Date
    ON ExchangeRates(FromCurrencyCode, ToCurrencyCode, EffectiveFrom DESC);

CREATE TABLE IF NOT EXISTS CurrencyExchanges (
    Id TEXT PRIMARY KEY,
    OutgoingMovementId TEXT NOT NULL UNIQUE REFERENCES Movements(Id),
    IncomingMovementId TEXT NOT NULL UNIQUE REFERENCES Movements(Id),
    AppliedRate TEXT NOT NULL,
    OccurredAt TEXT NOT NULL,
    Description TEXT,
    CHECK (OutgoingMovementId <> IncomingMovementId)
);

CREATE TABLE IF NOT EXISTS Denominations (
    Id TEXT PRIMARY KEY,
    CurrencyCode TEXT NOT NULL REFERENCES Currencies(Code),
    Value TEXT NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1)),
    UNIQUE (CurrencyCode, Value)
);

CREATE TABLE IF NOT EXISTS BanknoteMovementDetails (
    Id TEXT PRIMARY KEY,
    MovementId TEXT NOT NULL REFERENCES Movements(Id),
    DenominationId TEXT NOT NULL REFERENCES Denominations(Id),
    Quantity INTEGER NOT NULL CHECK (Quantity > 0),
    UNIQUE (MovementId, DenominationId)
);

CREATE TABLE IF NOT EXISTS CashInventoryCorrections (
    DenominationId TEXT PRIMARY KEY REFERENCES Denominations(Id),
    QuantityDelta INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS DistributionRules (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    DestinationAccountId TEXT NOT NULL REFERENCES Accounts(Id),
    Type INTEGER NOT NULL CHECK (Type IN (0, 1)),
    Priority INTEGER NOT NULL,
    Value TEXT NOT NULL,
    CurrencyCode TEXT REFERENCES Currencies(Code),
    AppliesOnSundays INTEGER NOT NULL CHECK (AppliesOnSundays IN (0, 1)),
    AppliesOnNonWorkingDays INTEGER NOT NULL CHECK (AppliesOnNonWorkingDays IN (0, 1)),
    IsActive INTEGER NOT NULL CHECK (IsActive IN (0, 1)),
    CHECK ((Type = 0 AND CurrencyCode IS NULL) OR (Type = 1 AND CurrencyCode IS NOT NULL))
);

CREATE TABLE IF NOT EXISTS VehicleRentSettings (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    AccountId TEXT NOT NULL REFERENCES Accounts(Id),
    DailyCupAmount TEXT NOT NULL,
    StartedOn TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS VehicleRentSettlements (
    Id TEXT PRIMARY KEY,
    WeekStart TEXT NOT NULL,
    ThroughDate TEXT NOT NULL,
    MovementId TEXT NOT NULL UNIQUE REFERENCES Movements(Id),
    AmountCup TEXT NOT NULL,
    SettledAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_VehicleRentSettlements_Week ON VehicleRentSettlements(WeekStart);

INSERT OR IGNORE INTO Currencies (Code, Name, IsPrimaryCurrency)
VALUES ('CUP', 'Cuban peso', 1);
INSERT OR IGNORE INTO Currencies (Code, Name) VALUES
    ('USD', 'US dollar'), ('MLC', 'MLC'), ('EUR', 'Euro');
INSERT OR IGNORE INTO Denominations (Id, CurrencyCode, Value) VALUES
    (lower(hex(randomblob(16))), 'CUP', '5'),
    (lower(hex(randomblob(16))), 'CUP', '10'),
    (lower(hex(randomblob(16))), 'CUP', '20'),
    (lower(hex(randomblob(16))), 'CUP', '50'),
    (lower(hex(randomblob(16))), 'CUP', '100'),
    (lower(hex(randomblob(16))), 'CUP', '200'),
    (lower(hex(randomblob(16))), 'CUP', '500'),
    (lower(hex(randomblob(16))), 'CUP', '1000'),
    (lower(hex(randomblob(16))), 'CUP', '2000'),
    (lower(hex(randomblob(16))), 'CUP', '5000'),
    (lower(hex(randomblob(16))), 'USD', '1'),
    (lower(hex(randomblob(16))), 'USD', '5'),
    (lower(hex(randomblob(16))), 'USD', '10'),
    (lower(hex(randomblob(16))), 'USD', '20'),
    (lower(hex(randomblob(16))), 'USD', '50'),
    (lower(hex(randomblob(16))), 'USD', '100'),
    (lower(hex(randomblob(16))), 'EUR', '5'),
    (lower(hex(randomblob(16))), 'EUR', '10'),
    (lower(hex(randomblob(16))), 'EUR', '20'),
    (lower(hex(randomblob(16))), 'EUR', '50'),
    (lower(hex(randomblob(16))), 'EUR', '100'),
    (lower(hex(randomblob(16))), 'EUR', '200'),
    (lower(hex(randomblob(16))), 'EUR', '500');

CREATE TABLE IF NOT EXISTS VehicleRentDayConsolidations (
    Date TEXT PRIMARY KEY,
    WeekStart TEXT NOT NULL,
    AmountCup TEXT NOT NULL,
    MovementId TEXT REFERENCES Movements(Id),
    ConsolidatedAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_VehicleRentDayConsolidations_Week ON VehicleRentDayConsolidations(WeekStart);

CREATE TABLE IF NOT EXISTS DatabaseProfile (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    StartDate TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS ServiceDirectWithdrawals (
    Id TEXT PRIMARY KEY,
    ConsolidationId TEXT NOT NULL REFERENCES ServiceConsolidations(Id),
    CurrencyCode TEXT NOT NULL REFERENCES Currencies(Code),
    PaymentMethod INTEGER NOT NULL CHECK (PaymentMethod IN (0, 1)),
    Amount TEXT NOT NULL,
    RecordedAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_ServiceDirectWithdrawals_Consolidation ON ServiceDirectWithdrawals(ConsolidationId);

PRAGMA user_version = 16;
