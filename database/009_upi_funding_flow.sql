/* Correct UPI behavior: hosted funding/payment order, not outbound VPA transfer. */
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

UPDATE catalog.Services
SET Name = 'UPI Add Money',
    Description = 'Hosted UPI/payment order for wallet funding; provider payment method remains unverified and LIVE is disabled.',
    IsChargeable = 0,
    IsCommissionable = 0,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE Code = 'upi_transfer';

-- Remove the provisional outbound-VPA wallet-debit rule from migration 008.
DROP PROCEDURE IF EXISTS finance.PostUpiTransferWalletDebit;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('finance.ServiceTransactions') AND name = 'UX_ServiceTransactions_ProviderReference')
    CREATE UNIQUE INDEX UX_ServiceTransactions_ProviderReference ON finance.ServiceTransactions(ProviderId, ProviderReference) WHERE ProviderReference IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM finance.LedgerAccounts WHERE Code = 'CLEARING:UPI_FUNDING')
    INSERT finance.LedgerAccounts(Code, Name, AccountType, NormalBalance, Currency, IsActive, CreatedAtUtc)
    VALUES('CLEARING:UPI_FUNDING', 'UPI funding clearing', 'CLEARING', 'D', 'INR', 1, SYSUTCDATETIME());
GO

CREATE OR ALTER PROCEDURE finance.ApplyUpiFundingProviderResult
    @ProviderId uniqueidentifier,
    @ServiceTransactionId uniqueidentifier,
    @UserId uniqueidentifier,
    @ExternalEventId nvarchar(180),
    @RawStatus nvarchar(100),
    @NormalizedStatus varchar(20),
    @ProviderReference nvarchar(180) = NULL,
    @FailureMessage nvarchar(500) = NULL,
    @PayloadHash char(64),
    @Reference nvarchar(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @NormalizedStatus NOT IN ('PENDING','SUCCEEDED','FAILED','CANCELLED') THROW 51040, 'Invalid UPI funding status.', 1;
    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @lockResult int;
        DECLARE @LockResource nvarchar(150) = CONCAT('UPI-FUNDING:', CONVERT(nvarchar(36), @ServiceTransactionId));
        EXEC @lockResult = sp_getapplock @Resource = @LockResource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @lockResult < 0 THROW 51041, 'Could not acquire UPI funding transaction lock.', 1;
        IF EXISTS (SELECT 1 FROM integration.ProviderEvents WITH (UPDLOCK, HOLDLOCK) WHERE ProviderId = @ProviderId AND ExternalEventId = @ExternalEventId AND ProcessingStatus = 'PROCESSED') BEGIN COMMIT; RETURN; END;

        DECLARE @CurrentStatus varchar(20), @Amount decimal(19,4), @OrganizationUnitId uniqueidentifier, @CurrentProviderReference nvarchar(180);
        SELECT @CurrentStatus = Status, @Amount = Amount, @OrganizationUnitId = OrganizationUnitId, @CurrentProviderReference = ProviderReference
        FROM finance.ServiceTransactions WITH (UPDLOCK, HOLDLOCK)
        WHERE ServiceTransactionId = @ServiceTransactionId AND UserId = @UserId;
        IF @Amount IS NULL THROW 51042, 'UPI funding transaction was not found.', 1;
        IF @CurrentProviderReference IS NOT NULL AND @ProviderReference IS NOT NULL AND @CurrentProviderReference <> @ProviderReference THROW 51043, 'Provider reference mismatch.', 1;

        INSERT integration.ProviderEvents(ProviderId, ExternalEventId, ServiceTransactionId, EventType, PayloadHash, RawStatus, ProcessingStatus, ReceivedAtUtc)
        VALUES(@ProviderId, @ExternalEventId, @ServiceTransactionId, 'UPI_FUNDING_STATUS', @PayloadHash, @RawStatus, 'RECEIVED', SYSUTCDATETIME());

        IF @CurrentStatus IN ('SUCCEEDED','FAILED','CANCELLED')
        BEGIN
            UPDATE integration.ProviderEvents SET ProcessingStatus = 'IGNORED', FailureMessage = 'Transaction is already terminal.', ProcessedAtUtc = SYSUTCDATETIME()
            WHERE ProviderId = @ProviderId AND ExternalEventId = @ExternalEventId;
            COMMIT; RETURN;
        END;

        IF @NormalizedStatus = 'PENDING'
        BEGIN
            UPDATE finance.ServiceTransactions SET Status = 'PENDING', ProviderStatus = @RawStatus, ProviderReference = COALESCE(@ProviderReference, ProviderReference), FailureMessage = NULL, ResponseAtUtc = SYSUTCDATETIME(), UpdatedAtUtc = SYSUTCDATETIME()
            WHERE ServiceTransactionId = @ServiceTransactionId;
        END
        ELSE IF @NormalizedStatus IN ('FAILED','CANCELLED')
        BEGIN
            UPDATE finance.ServiceTransactions SET Status = @NormalizedStatus, ProviderStatus = @RawStatus, ProviderReference = COALESCE(@ProviderReference, ProviderReference), FailureMessage = @FailureMessage, ResponseAtUtc = SYSUTCDATETIME(), UpdatedAtUtc = SYSUTCDATETIME(), CompletedAtUtc = SYSUTCDATETIME()
            WHERE ServiceTransactionId = @ServiceTransactionId;
        END
        ELSE
        BEGIN
            DECLARE @Wallet uniqueidentifier = (SELECT LedgerAccountId FROM finance.LedgerAccounts WITH (UPDLOCK, HOLDLOCK) WHERE UserId = @UserId AND Code = CONCAT('USER:', CONVERT(nvarchar(36), @UserId), ':WALLET') AND IsActive = 1);
            DECLARE @Clearing uniqueidentifier = (SELECT LedgerAccountId FROM finance.LedgerAccounts WITH (UPDLOCK, HOLDLOCK) WHERE Code = 'CLEARING:UPI_FUNDING' AND IsActive = 1);
            IF @Wallet IS NULL OR @Clearing IS NULL THROW 51044, 'Wallet accounts are not configured.', 1;
            IF NOT EXISTS (SELECT 1 FROM finance.JournalTransactions WITH (UPDLOCK, HOLDLOCK) WHERE IdempotencyKey = CONCAT('UPI-FUNDING-CREDIT:', CONVERT(nvarchar(36), @ServiceTransactionId)))
            BEGIN
                DECLARE @JournalTransactionId uniqueidentifier = NEWID();
                INSERT finance.JournalTransactions(JournalTransactionId, ServiceTransactionId, IdempotencyKey, Reference, Description, Status, CreatedByUserId, CreatedAtUtc, PostedAtUtc)
                VALUES(@JournalTransactionId, @ServiceTransactionId, CONCAT('UPI-FUNDING-CREDIT:', CONVERT(nvarchar(36), @ServiceTransactionId)), CONCAT('UPI-CREDIT:', @Reference), 'UPI Add Money wallet credit', 'POSTED', @UserId, SYSUTCDATETIME(), SYSUTCDATETIME());
                INSERT finance.JournalEntries(JournalTransactionId, LedgerAccountId, DebitAmount, CreditAmount, Memo)
                VALUES(@JournalTransactionId, @Clearing, @Amount, 0, 'UPI funding clearing debit'), (@JournalTransactionId, @Wallet, 0, @Amount, 'UPI funding wallet credit');
                UPDATE finance.LedgerAccounts SET CurrentBalance = CurrentBalance + @Amount WHERE LedgerAccountId = @Clearing;
                UPDATE finance.LedgerAccounts SET CurrentBalance = CurrentBalance + @Amount WHERE LedgerAccountId = @Wallet;
            END;
            UPDATE finance.ServiceTransactions SET Status = 'SUCCEEDED', ProviderStatus = @RawStatus, ProviderReference = COALESCE(@ProviderReference, ProviderReference), FailureMessage = NULL, CreditAmount = @Amount, ResponseAtUtc = SYSUTCDATETIME(), UpdatedAtUtc = SYSUTCDATETIME(), CompletedAtUtc = SYSUTCDATETIME()
            WHERE ServiceTransactionId = @ServiceTransactionId;
        END;

        UPDATE integration.ProviderEvents SET ProcessingStatus = 'PROCESSED', ProcessedAtUtc = SYSUTCDATETIME()
        WHERE ProviderId = @ProviderId AND ExternalEventId = @ExternalEventId;
        INSERT audit.AuditLogs(ActorUserId, OrganizationUnitId, Action, EntityType, EntityId, CorrelationId, DetailsJson, OccurredAtUtc)
        VALUES(NULL, @OrganizationUnitId, 'UPI_FUNDING_PROVIDER_RESULT', 'ServiceTransaction', CONVERT(nvarchar(100), @ServiceTransactionId), NEWID(), (SELECT @RawStatus RawStatus, @NormalizedStatus NormalizedStatus, @ExternalEventId ExternalEventId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), SYSUTCDATETIME());
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK;
        THROW;
    END CATCH;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNo = 9)
    INSERT dbo.SchemaVersions(VersionNo, Description) VALUES(9, N'UPI Add Money hosted funding order and atomic provider-result wallet credit');
GO
