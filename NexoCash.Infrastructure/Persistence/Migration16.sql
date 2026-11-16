ALTER TABLE AgencyFundMovements ADD COLUMN TransactionNumber TEXT;
ALTER TABLE AgencyFundMovements ADD COLUMN SurchargePercent TEXT;
CREATE UNIQUE INDEX IX_AgencyFund_TransactionNumber ON AgencyFundMovements(AgencyId, TransactionNumber) WHERE TransactionNumber IS NOT NULL;
PRAGMA user_version = 16;
