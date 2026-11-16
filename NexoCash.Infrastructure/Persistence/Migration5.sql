ALTER TABLE Currencies ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1));
INSERT OR IGNORE INTO Currencies (Code, Name) VALUES
    ('USD', 'US dollar'), ('MLC', 'MLC'), ('EUR', 'Euro');
INSERT OR IGNORE INTO Denominations (Id, CurrencyCode, Value) VALUES
    (lower(hex(randomblob(16))), 'CUP', '50'),
    (lower(hex(randomblob(16))), 'CUP', '100'),
    (lower(hex(randomblob(16))), 'CUP', '200'),
    (lower(hex(randomblob(16))), 'CUP', '500'),
    (lower(hex(randomblob(16))), 'CUP', '1000'),
    (lower(hex(randomblob(16))), 'CUP', '2000'),
    (lower(hex(randomblob(16))), 'CUP', '5000'),
    (lower(hex(randomblob(16))), 'USD', '1'),
    (lower(hex(randomblob(16))), 'USD', '2'),
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
PRAGMA user_version = 5;
