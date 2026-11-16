CREATE TABLE ServiceConsolidations (
    Id TEXT PRIMARY KEY,
    ConsolidatedAt TEXT NOT NULL
);
CREATE TABLE ServiceConsolidationItems (
    ConsolidationId TEXT NOT NULL REFERENCES ServiceConsolidations(Id),
    ServiceId TEXT NOT NULL UNIQUE REFERENCES Services(Id),
    PRIMARY KEY (ConsolidationId, ServiceId)
);
PRAGMA user_version = 3;
