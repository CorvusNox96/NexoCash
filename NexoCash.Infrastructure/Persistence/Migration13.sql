INSERT INTO Denominations (Id, CurrencyCode, Value)
SELECT lower(hex(randomblob(16))), 'CUP', values_to_add.Value
FROM (SELECT '5' AS Value UNION ALL SELECT '10' UNION ALL SELECT '20') AS values_to_add
WHERE NOT EXISTS (
    SELECT 1 FROM Denominations d
    WHERE d.CurrencyCode = 'CUP' AND d.Value = values_to_add.Value
);

UPDATE Denominations SET IsActive = 1
WHERE CurrencyCode = 'CUP' AND Value IN ('5', '10', '20');

UPDATE Denominations SET IsActive = 0
WHERE CurrencyCode = 'USD' AND Value = '2'
  AND NOT EXISTS (SELECT 1 FROM BanknoteMovementDetails WHERE DenominationId = Denominations.Id)
  AND NOT EXISTS (SELECT 1 FROM ServiceConsolidationBanknotes WHERE DenominationId = Denominations.Id)
  AND NOT EXISTS (SELECT 1 FROM CashInventoryCorrections WHERE DenominationId = Denominations.Id);

PRAGMA user_version = 13;
