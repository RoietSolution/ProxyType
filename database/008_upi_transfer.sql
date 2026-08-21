/* UPI Transfer: new outbound capability. Legacy UPI evidence is wallet-funding only;
   outbound provider, pricing, limits, callbacks and reversal rules remain UNKNOWN. */
USE [proxytype_DB];
GO
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

DECLARE @Category uniqueidentifier = (SELECT ServiceCategoryId FROM catalog.ServiceCategories WHERE Code = 'TRANSFER');
IF NOT EXISTS (SELECT 1 FROM catalog.Services WHERE Code = 'upi_transfer')
    INSERT catalog.Services(ServiceCategoryId, Code, Name, Description, IsActive, IsChargeable, IsCommissionable, RequiresKyc, SortOrder)
    VALUES(@Category, 'upi_transfer', 'UPI Transfer', 'Outbound VPA transfer using the deterministic MOCK provider; LIVE disabled pending contract confirmation.', 1, 0, 0, 0, 60);

IF NOT EXISTS (SELECT 1 FROM catalog.Providers WHERE Code = 'upi_transfer_mock')
    INSERT catalog.Providers(Code, Name, Environment, IsEnabled, TimeoutSeconds)
    VALUES('upi_transfer_mock', 'UPI Transfer Mock Provider', 'SANDBOX', 1, 5);

INSERT catalog.ServiceProviders(ServiceId, ProviderId, Priority, IsEnabled, ConfigurationJson)
SELECT s.ServiceId, p.ProviderId, 1, 1, N'{"mode":"MOCK"}'
FROM catalog.Services s CROSS JOIN catalog.Providers p
WHERE s.Code = 'upi_transfer' AND p.Code = 'upi_transfer_mock'
  AND NOT EXISTS (SELECT 1 FROM catalog.ServiceProviders r WHERE r.ServiceId = s.ServiceId AND r.ProviderId = p.ProviderId);

DECLARE @Platform uniqueidentifier = (SELECT OrganizationUnitId FROM org.OrganizationUnits WHERE Code = 'PLATFORM');
DECLARE @Service uniqueidentifier = (SELECT ServiceId FROM catalog.Services WHERE Code = 'upi_transfer');
IF @Platform IS NOT NULL AND NOT EXISTS (SELECT 1 FROM catalog.OrganizationServicePermissions WHERE OrganizationUnitId = @Platform AND ServiceId = @Service AND EffectiveToUtc IS NULL)
    INSERT catalog.OrganizationServicePermissions(OrganizationUnitId, ServiceId, Effect, Reason)
    VALUES(@Platform, @Service, 'ALLOW', 'UPI Transfer MOCK grant; outbound live contract and pricing require confirmation.');

IF NOT EXISTS (SELECT 1 FROM finance.LedgerAccounts WHERE Code = 'CLEARING:UPI_TRANSFER')
    INSERT finance.LedgerAccounts(Code, Name, AccountType, NormalBalance, Currency, IsActive, CreatedAtUtc)
    VALUES('CLEARING:UPI_TRANSFER', 'UPI Transfer clearing', 'CLEARING', 'C', 'INR', 1, SYSUTCDATETIME());

INSERT finance.LedgerAccounts(UserId, Code, Name, AccountType, NormalBalance, Currency, IsActive, CreatedAtUtc)
SELECT u.UserId, CONCAT('USER:', CONVERT(nvarchar(36), u.UserId), ':WALLET'), CONCAT(u.Username, ' wallet'), 'LIABILITY', 'C', 'INR', 1, SYSUTCDATETIME()
FROM auth.Users u
WHERE NOT EXISTS (SELECT 1 FROM finance.LedgerAccounts a WHERE a.UserId = u.UserId AND a.Code = CONCAT('USER:', CONVERT(nvarchar(36), u.UserId), ':WALLET'));
GO

CREATE OR ALTER PROCEDURE finance.PostUpiTransferWalletDebit
    @JournalTransactionId uniqueidentifier, @ServiceTransactionId uniqueidentifier,
    @IdempotencyKey nvarchar(120), @Reference nvarchar(100), @UserId uniqueidentifier,
    @Amount decimal(19,4)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF @Amount <= 0 THROW 51020, 'Debit amount must be positive.', 1;
    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @lockResult int;
        EXEC @lockResult = sp_getapplock @Resource = @IdempotencyKey, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @lockResult < 0 THROW 51023, 'Could not acquire UPI transfer idempotency lock.', 1;
        IF EXISTS (SELECT 1 FROM finance.JournalTransactions WITH (UPDLOCK, HOLDLOCK) WHERE IdempotencyKey = @IdempotencyKey AND Status = 'POSTED') BEGIN COMMIT; RETURN; END;
        DECLARE @Wallet uniqueidentifier = (SELECT LedgerAccountId FROM finance.LedgerAccounts WITH (UPDLOCK, HOLDLOCK) WHERE UserId = @UserId AND Code = CONCAT('USER:', CONVERT(nvarchar(36), @UserId), ':WALLET') AND IsActive = 1);
        DECLARE @Clearing uniqueidentifier = (SELECT LedgerAccountId FROM finance.LedgerAccounts WITH (UPDLOCK, HOLDLOCK) WHERE Code = 'CLEARING:UPI_TRANSFER' AND IsActive = 1);
        IF @Wallet IS NULL OR @Clearing IS NULL THROW 51021, 'Wallet accounts are not configured.', 1;
        IF (SELECT CurrentBalance FROM finance.LedgerAccounts WITH (UPDLOCK, HOLDLOCK) WHERE LedgerAccountId = @Wallet) < @Amount THROW 51022, 'Insufficient wallet balance.', 1;
        INSERT finance.JournalTransactions(JournalTransactionId, ServiceTransactionId, IdempotencyKey, Reference, Description, Status, CreatedByUserId, CreatedAtUtc, PostedAtUtc)
        VALUES(@JournalTransactionId, @ServiceTransactionId, @IdempotencyKey, @Reference, 'UPI Transfer wallet debit', 'POSTED', @UserId, SYSUTCDATETIME(), SYSUTCDATETIME());
        INSERT finance.JournalEntries(JournalTransactionId, LedgerAccountId, DebitAmount, CreditAmount, Memo)
        VALUES(@JournalTransactionId, @Wallet, @Amount, 0, 'UPI transfer principal debit'), (@JournalTransactionId, @Clearing, 0, @Amount, 'UPI transfer clearing credit');
        UPDATE finance.LedgerAccounts SET CurrentBalance = CurrentBalance - @Amount WHERE LedgerAccountId = @Wallet;
        UPDATE finance.LedgerAccounts SET CurrentBalance = CurrentBalance + @Amount WHERE LedgerAccountId = @Clearing;
        COMMIT;
    END TRY
    BEGIN CATCH IF XACT_STATE() <> 0 ROLLBACK; THROW; END CATCH;
END;
GO
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNo = 8)
    INSERT dbo.SchemaVersions(VersionNo, Description) VALUES(8, N'UPI Transfer MOCK provider route and atomic wallet debit');
GO
