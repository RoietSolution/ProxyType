USE [proxytype_DB];
GO

IF COL_LENGTH(N'finance.ServiceTransactions', N'TaxAmount') IS NULL
    ALTER TABLE finance.ServiceTransactions ADD TaxAmount decimal(19,4) NOT NULL CONSTRAINT DF_ServiceTransactions_Tax DEFAULT 0;
IF COL_LENGTH(N'finance.ServiceTransactions', N'DebitAmount') IS NULL
    ALTER TABLE finance.ServiceTransactions ADD DebitAmount decimal(19,4) NOT NULL CONSTRAINT DF_ServiceTransactions_Debit DEFAULT 0;
IF COL_LENGTH(N'finance.ServiceTransactions', N'CreditAmount') IS NULL
    ALTER TABLE finance.ServiceTransactions ADD CreditAmount decimal(19,4) NOT NULL CONSTRAINT DF_ServiceTransactions_Credit DEFAULT 0;
IF COL_LENGTH(N'finance.ServiceTransactions', N'Remarks') IS NULL
    ALTER TABLE finance.ServiceTransactions ADD Remarks nvarchar(1000) NULL;
IF COL_LENGTH(N'finance.ServiceTransactions', N'RequestAtUtc') IS NULL
    ALTER TABLE finance.ServiceTransactions ADD RequestAtUtc datetime2(3) NOT NULL CONSTRAINT DF_ServiceTransactions_RequestAt DEFAULT SYSUTCDATETIME();
IF COL_LENGTH(N'finance.ServiceTransactions', N'ResponseAtUtc') IS NULL
    ALTER TABLE finance.ServiceTransactions ADD ResponseAtUtc datetime2(3) NULL;
GO

DECLARE @Transfer uniqueidentifier = (SELECT ServiceCategoryId FROM catalog.ServiceCategories WHERE Code = 'TRANSFER');
IF NOT EXISTS (SELECT 1 FROM catalog.Services WHERE Code = 'fino_dmt')
BEGIN
    INSERT catalog.Services(ServiceCategoryId, Code, Name, Description, IsActive, IsChargeable, IsCommissionable, RequiresKyc, SortOrder)
    VALUES(@Transfer, 'fino_dmt', 'Fino DMT', 'Domestic money transfer workflow; provider adapter remains disabled until verified.', 1, 1, 0, 1, 5);
END;

IF NOT EXISTS (SELECT 1 FROM catalog.Providers WHERE Code = 'fino_dmt_mock')
    INSERT catalog.Providers(Code, Name, Environment, IsEnabled, TimeoutSeconds)
    VALUES('fino_dmt_mock', 'Fino DMT Mock Provider', 'SANDBOX', 1, 5);

INSERT catalog.ServiceProviders(ServiceId, ProviderId, Priority, IsEnabled, ConfigurationJson)
SELECT s.ServiceId, p.ProviderId, 1, 1, N'{"mode":"MOCK"}'
FROM catalog.Services s CROSS JOIN catalog.Providers p
WHERE s.Code = 'fino_dmt' AND p.Code = 'fino_dmt_mock'
  AND NOT EXISTS (SELECT 1 FROM catalog.ServiceProviders r WHERE r.ServiceId = s.ServiceId AND r.ProviderId = p.ProviderId);

DECLARE @Platform uniqueidentifier = (SELECT OrganizationUnitId FROM org.OrganizationUnits WHERE Code = 'PLATFORM');
DECLARE @Service uniqueidentifier = (SELECT ServiceId FROM catalog.Services WHERE Code = 'fino_dmt');
IF NOT EXISTS (SELECT 1 FROM catalog.OrganizationServicePermissions WHERE OrganizationUnitId = @Platform AND ServiceId = @Service AND EffectiveToUtc IS NULL)
    INSERT catalog.OrganizationServicePermissions(OrganizationUnitId, ServiceId, Effect, Reason)
    VALUES(@Platform, @Service, 'ALLOW', 'Fino DMT catalog grant; access still requires active hierarchy permission.');

IF NOT EXISTS (SELECT 1 FROM finance.LedgerAccounts WHERE Code = 'CLEARING:FINO_DMT')
    INSERT finance.LedgerAccounts(Code, Name, AccountType, NormalBalance, Currency)
    VALUES('CLEARING:FINO_DMT', 'Fino DMT clearing', 'CLEARING', 'C', 'INR');

INSERT finance.LedgerAccounts(UserId, Code, Name, AccountType, NormalBalance, Currency)
SELECT u.UserId, CONCAT('USER:', CONVERT(nvarchar(36), u.UserId), ':WALLET'), CONCAT(u.Username, ' wallet'), 'LIABILITY', 'C', 'INR'
FROM auth.Users u
WHERE NOT EXISTS
(
    SELECT 1 FROM finance.LedgerAccounts a WHERE a.UserId = u.UserId AND a.Code = CONCAT('USER:', CONVERT(nvarchar(36), u.UserId), ':WALLET')
);
GO

CREATE OR ALTER PROCEDURE finance.PostWalletDebit
    @JournalTransactionId uniqueidentifier,
    @ServiceTransactionId uniqueidentifier,
    @IdempotencyKey nvarchar(120),
    @Reference nvarchar(100),
    @UserId uniqueidentifier,
    @Amount decimal(19,4)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @Amount <= 0 THROW 51020, 'Debit amount must be positive.', 1;
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM finance.JournalTransactions WITH (UPDLOCK, HOLDLOCK) WHERE IdempotencyKey = @IdempotencyKey)
    BEGIN
        COMMIT TRANSACTION;
        RETURN;
    END;

    DECLARE @Wallet uniqueidentifier = (SELECT LedgerAccountId FROM finance.LedgerAccounts WITH (UPDLOCK, HOLDLOCK)
        WHERE UserId = @UserId AND Code = CONCAT('USER:', CONVERT(nvarchar(36), @UserId), ':WALLET') AND IsActive = 1);
    DECLARE @Clearing uniqueidentifier = (SELECT LedgerAccountId FROM finance.LedgerAccounts WITH (UPDLOCK, HOLDLOCK)
        WHERE Code = 'CLEARING:FINO_DMT' AND IsActive = 1);
    IF @Wallet IS NULL OR @Clearing IS NULL THROW 51021, 'Wallet accounts are not configured.', 1;
    IF (SELECT CurrentBalance FROM finance.LedgerAccounts WITH (UPDLOCK, HOLDLOCK) WHERE LedgerAccountId = @Wallet) < @Amount
        THROW 51022, 'Insufficient wallet balance.', 1;

    INSERT finance.JournalTransactions(JournalTransactionId, ServiceTransactionId, IdempotencyKey, Reference, Description, Status, CreatedByUserId, PostedAtUtc)
    VALUES(@JournalTransactionId, @ServiceTransactionId, @IdempotencyKey, @Reference, 'Fino DMT wallet debit', 'POSTED', @UserId, SYSUTCDATETIME());
    INSERT finance.JournalEntries(JournalTransactionId, LedgerAccountId, DebitAmount, CreditAmount, Memo)
    VALUES(@JournalTransactionId, @Wallet, @Amount, 0, 'Fino DMT principal debit'),
          (@JournalTransactionId, @Clearing, 0, @Amount, 'Fino DMT clearing credit');
    UPDATE finance.LedgerAccounts SET CurrentBalance = CurrentBalance - @Amount WHERE LedgerAccountId = @Wallet;
    UPDATE finance.LedgerAccounts SET CurrentBalance = CurrentBalance + @Amount WHERE LedgerAccountId = @Clearing;
    COMMIT TRANSACTION;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNo = 2)
    INSERT dbo.SchemaVersions(VersionNo, Description) VALUES(2, N'Fino DMT mock provider route, transaction fields, wallet debit procedure and ledger seed');
GO
