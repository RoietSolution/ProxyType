/* Wallet to Wallet: atomic internal transfer. Apply after 004_recharge.sql. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
IF COL_LENGTH(N'finance.ServiceTransactions', N'CounterpartyUserId') IS NULL
    ALTER TABLE finance.ServiceTransactions ADD CounterpartyUserId uniqueidentifier NULL;
IF COL_LENGTH(N'finance.ServiceTransactions', N'CounterpartyOrganizationUnitId') IS NULL
    ALTER TABLE finance.ServiceTransactions ADD CounterpartyOrganizationUnitId uniqueidentifier NULL;
GO

DECLARE @Service uniqueidentifier = (SELECT ServiceId FROM catalog.Services WHERE Code = 'wallet_transfer');
IF @Service IS NULL
    THROW 51040, 'wallet_transfer service is not present. Apply 001/003 first.', 1;
UPDATE catalog.Services SET Name = N'Wallet to Wallet', IsActive = 1, IsChargeable = 0, IsCommissionable = 0, UpdatedAtUtc = SYSUTCDATETIME() WHERE ServiceId = @Service;
DECLARE @Platform uniqueidentifier = (SELECT OrganizationUnitId FROM org.OrganizationUnits WHERE Code = 'PLATFORM');
IF @Platform IS NOT NULL AND NOT EXISTS (SELECT 1 FROM catalog.OrganizationServicePermissions WHERE OrganizationUnitId = @Platform AND ServiceId = @Service AND EffectiveToUtc IS NULL)
    INSERT catalog.OrganizationServicePermissions(OrganizationUnitId, ServiceId, Effect, Reason) VALUES(@Platform, @Service, 'ALLOW', N'Wallet to Wallet implementation deployed');
GO

CREATE OR ALTER PROCEDURE finance.PostWalletToWalletTransfer
    @JournalTransactionId uniqueidentifier,
    @ServiceTransactionId uniqueidentifier,
    @IdempotencyKey nvarchar(120),
    @Reference nvarchar(100),
    @SenderUserId uniqueidentifier,
    @ReceiverUserId uniqueidentifier,
    @Amount decimal(19,4),
    @ChargeAmount decimal(19,4) = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @SenderUserId = @ReceiverUserId THROW 51041, 'Self-transfer is not allowed.', 1;
    IF @Amount < 10 OR @ChargeAmount < 0 THROW 51042, 'Invalid wallet transfer amount.', 1;
    BEGIN TRY
        BEGIN TRAN;
        DECLARE @lock int;
        EXEC @lock = sp_getapplock @Resource = @IdempotencyKey, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @lock < 0 THROW 51043, 'Could not acquire transfer idempotency lock.', 1;
        IF EXISTS (SELECT 1 FROM finance.JournalTransactions WHERE IdempotencyKey = @IdempotencyKey AND Status = 'POSTED')
        BEGIN COMMIT; RETURN; END;
        DECLARE @SenderWallet uniqueidentifier = (SELECT LedgerAccountId FROM finance.LedgerAccounts WHERE UserId = @SenderUserId AND Code = CONCAT('USER:', CONVERT(varchar(36), @SenderUserId), ':WALLET') AND IsActive = 1);
        DECLARE @ReceiverWallet uniqueidentifier = (SELECT LedgerAccountId FROM finance.LedgerAccounts WHERE UserId = @ReceiverUserId AND Code = CONCAT('USER:', CONVERT(varchar(36), @ReceiverUserId), ':WALLET') AND IsActive = 1);
        SELECT LedgerAccountId FROM finance.LedgerAccounts WITH (UPDLOCK, HOLDLOCK)
        WHERE LedgerAccountId IN (@SenderWallet, @ReceiverWallet) ORDER BY LedgerAccountId;
        IF @SenderWallet IS NULL OR @ReceiverWallet IS NULL THROW 51044, 'Wallet account not found.', 1;
        IF @Amount + @ChargeAmount > (SELECT CurrentBalance FROM finance.LedgerAccounts WHERE LedgerAccountId = @SenderWallet) THROW 51022, 'Insufficient wallet balance.', 1;
        INSERT finance.JournalTransactions(JournalTransactionId, ServiceTransactionId, IdempotencyKey, Reference, Description, Status, CreatedByUserId, CreatedAtUtc, PostedAtUtc)
        VALUES(@JournalTransactionId, @ServiceTransactionId, @IdempotencyKey, @Reference, N'Wallet to Wallet transfer', 'POSTED', @SenderUserId, SYSUTCDATETIME(), SYSUTCDATETIME());
        INSERT finance.JournalEntries(JournalTransactionId, LedgerAccountId, DebitAmount, CreditAmount, Memo)
        VALUES(@JournalTransactionId, @SenderWallet, @Amount + @ChargeAmount, 0, N'Wallet transfer sender debit'),
              (@JournalTransactionId, @ReceiverWallet, 0, @Amount, N'Wallet transfer receiver credit');
        UPDATE finance.LedgerAccounts SET CurrentBalance = CurrentBalance - @Amount - @ChargeAmount WHERE LedgerAccountId = @SenderWallet;
        UPDATE finance.LedgerAccounts SET CurrentBalance = CurrentBalance + @Amount WHERE LedgerAccountId = @ReceiverWallet;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK;
        THROW;
    END CATCH;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNo = 5)
    INSERT dbo.SchemaVersions(VersionNo, Description) VALUES(5, N'Atomic Wallet to Wallet transfer, counterparty tracking and idempotent ledger posting');
