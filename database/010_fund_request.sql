/* Fund Request: manual NEFT proof submission with authorized approval and atomic wallet credit. */
USE [proxytype_DB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

UPDATE catalog.Services
SET Name = 'Fund Request',
    Description = 'Manual NEFT wallet funding request requiring authorized review; proof and reference required.',
    IsChargeable = 0,
    IsCommissionable = 0,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE Code = 'fund_request';

IF COL_LENGTH('finance.FundRequests', 'ServiceTransactionId') IS NULL
    ALTER TABLE finance.FundRequests ADD ServiceTransactionId uniqueidentifier NULL;
IF COL_LENGTH('finance.FundRequests', 'ProofContent') IS NULL
    ALTER TABLE finance.FundRequests ADD ProofContent varbinary(max) NULL;
IF COL_LENGTH('finance.FundRequests', 'ProofContentType') IS NULL
    ALTER TABLE finance.FundRequests ADD ProofContentType varchar(100) NULL;
IF COL_LENGTH('finance.FundRequests', 'ProofFileName') IS NULL
    ALTER TABLE finance.FundRequests ADD ProofFileName nvarchar(255) NULL;
IF COL_LENGTH('finance.FundRequests', 'UpdatedAtUtc') IS NULL
    ALTER TABLE finance.FundRequests ADD UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_FundRequests_Updated DEFAULT SYSUTCDATETIME();

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_FundRequests_ServiceTransaction')
    ALTER TABLE finance.FundRequests ADD CONSTRAINT FK_FundRequests_ServiceTransaction
        FOREIGN KEY (ServiceTransactionId) REFERENCES finance.ServiceTransactions(ServiceTransactionId);

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_FundRequests_Mode')
    ALTER TABLE finance.FundRequests DROP CONSTRAINT CK_FundRequests_Mode;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_FundRequests_Mode_Neft')
    ALTER TABLE finance.FundRequests ADD CONSTRAINT CK_FundRequests_Mode_Neft CHECK (PaymentMode = 'NEFT');
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('finance.FundRequests') AND name = 'UX_FundRequests_ExternalReference')
    CREATE UNIQUE INDEX UX_FundRequests_ExternalReference ON finance.FundRequests(ExternalReference) WHERE ExternalReference IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('finance.FundRequests') AND name = 'UX_FundRequests_ServiceTransaction')
    CREATE UNIQUE INDEX UX_FundRequests_ServiceTransaction ON finance.FundRequests(ServiceTransactionId) WHERE ServiceTransactionId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('finance.FundRequests') AND name = 'IX_FundRequests_RequesterCreated')
    CREATE INDEX IX_FundRequests_RequesterCreated ON finance.FundRequests(RequestedByUserId, CreatedAtUtc DESC);

IF NOT EXISTS (SELECT 1 FROM finance.LedgerAccounts WHERE Code = 'CLEARING:FUND_REQUEST')
    INSERT finance.LedgerAccounts(Code, Name, AccountType, NormalBalance, Currency, IsActive, CreatedAtUtc)
    VALUES('CLEARING:FUND_REQUEST', 'Fund Request bank clearing', 'ASSET', 'D', 'INR', 1, SYSUTCDATETIME());
GO

CREATE OR ALTER PROCEDURE finance.ReviewFundRequest
    @FundRequestId uniqueidentifier,
    @ReviewerUserId uniqueidentifier,
    @Decision varchar(20),
    @ReviewReason nvarchar(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @Decision = UPPER(LTRIM(RTRIM(@Decision)));
    IF @Decision NOT IN ('APPROVED', 'REJECTED') THROW 51060, 'Fund Request decision is invalid.', 1;

    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    DECLARE @lockResource nvarchar(255) = CONCAT('FUND-REQUEST:', CONVERT(nvarchar(36), @FundRequestId));
    EXEC @lockResult = sp_getapplock
        @Resource = @lockResource,
        @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
    IF @lockResult < 0 THROW 51061, 'Fund Request is busy. Please retry.', 1;

    DECLARE @CurrentStatus varchar(20), @Amount decimal(19,4), @OrganizationUnitId uniqueidentifier,
            @RequestedByUserId uniqueidentifier, @ServiceTransactionId uniqueidentifier,
            @TransactionStatus varchar(20), @Wallet uniqueidentifier, @Clearing uniqueidentifier,
            @JournalId uniqueidentifier = NULL;
    SELECT @CurrentStatus = Status, @Amount = Amount, @OrganizationUnitId = OrganizationUnitId,
           @RequestedByUserId = RequestedByUserId, @ServiceTransactionId = ServiceTransactionId
    FROM finance.FundRequests WITH (UPDLOCK, HOLDLOCK)
    WHERE FundRequestId = @FundRequestId;
    IF @CurrentStatus IS NULL THROW 51062, 'Fund Request was not found.', 1;

    IF @CurrentStatus <> 'PENDING'
    BEGIN
        IF @CurrentStatus = @Decision BEGIN COMMIT TRANSACTION; RETURN; END;
        THROW 51063, 'Fund Request has already been reviewed.', 1;
    END;
    IF @ServiceTransactionId IS NULL THROW 51064, 'Fund Request transaction link is missing.', 1;

    SELECT @TransactionStatus = Status
    FROM finance.ServiceTransactions WITH (UPDLOCK, HOLDLOCK)
    WHERE ServiceTransactionId = @ServiceTransactionId;
    IF @TransactionStatus IS NULL THROW 51065, 'Fund Request transaction was not found.', 1;

    IF @Decision = 'APPROVED'
    BEGIN
        SELECT @Wallet = LedgerAccountId
        FROM finance.LedgerAccounts WITH (UPDLOCK, HOLDLOCK)
        WHERE UserId = @RequestedByUserId AND Code = CONCAT('USER:', CONVERT(nvarchar(36), @RequestedByUserId), ':WALLET') AND IsActive = 1;
        SELECT @Clearing = LedgerAccountId
        FROM finance.LedgerAccounts WITH (UPDLOCK, HOLDLOCK)
        WHERE Code = 'CLEARING:FUND_REQUEST' AND IsActive = 1;
        IF @Wallet IS NULL THROW 51066, 'Requester wallet is not initialized.', 1;
        IF @Clearing IS NULL THROW 51067, 'Fund Request clearing account is not initialized.', 1;

        SELECT @JournalId = JournalTransactionId
        FROM finance.JournalTransactions WITH (UPDLOCK, HOLDLOCK)
        WHERE IdempotencyKey = CONCAT('FUND-REQUEST-CREDIT:', CONVERT(nvarchar(36), @FundRequestId));
        IF @JournalId IS NULL
        BEGIN
            SET @JournalId = NEWID();
            INSERT finance.JournalTransactions
            (JournalTransactionId, ServiceTransactionId, IdempotencyKey, Reference, Description, Status, CreatedByUserId, CreatedAtUtc, PostedAtUtc)
            VALUES
            (@JournalId, @ServiceTransactionId, CONCAT('FUND-REQUEST-CREDIT:', CONVERT(nvarchar(36), @FundRequestId)),
             CONCAT('FUND-', CONVERT(nvarchar(36), @FundRequestId)), 'Fund Request wallet credit', 'POSTED', @ReviewerUserId, SYSUTCDATETIME(), SYSUTCDATETIME());
            INSERT finance.JournalEntries(JournalTransactionId, LedgerAccountId, DebitAmount, CreditAmount, Memo)
            VALUES(@JournalId, @Clearing, @Amount, 0, 'NEFT funding received'),
                  (@JournalId, @Wallet, 0, @Amount, 'Fund Request wallet credit');
            UPDATE finance.LedgerAccounts SET CurrentBalance = CurrentBalance + @Amount WHERE LedgerAccountId = @Clearing;
            UPDATE finance.LedgerAccounts SET CurrentBalance = CurrentBalance + @Amount WHERE LedgerAccountId = @Wallet;
        END;

        UPDATE finance.ServiceTransactions
        SET Status = 'SUCCEEDED', CreditAmount = @Amount, FailureCode = NULL, FailureMessage = NULL,
            ResponseAtUtc = SYSUTCDATETIME(), CompletedAtUtc = SYSUTCDATETIME(), UpdatedAtUtc = SYSUTCDATETIME()
        WHERE ServiceTransactionId = @ServiceTransactionId;
    END
    ELSE
    BEGIN
        UPDATE finance.ServiceTransactions
        SET Status = 'FAILED', CreditAmount = 0, FailureCode = 'FUND_REQUEST_REJECTED',
            FailureMessage = COALESCE(NULLIF(@ReviewReason, ''), 'Fund Request rejected.'),
            ResponseAtUtc = SYSUTCDATETIME(), CompletedAtUtc = SYSUTCDATETIME(), UpdatedAtUtc = SYSUTCDATETIME()
        WHERE ServiceTransactionId = @ServiceTransactionId;
    END;

    UPDATE finance.FundRequests
    SET Status = @Decision, ReviewedByUserId = @ReviewerUserId, ReviewedAtUtc = SYSUTCDATETIME(),
        ReviewReason = NULLIF(@ReviewReason, ''), UpdatedAtUtc = SYSUTCDATETIME()
    WHERE FundRequestId = @FundRequestId;

    UPDATE finance.Receipts
    SET JournalTransactionId = COALESCE(@JournalId, JournalTransactionId),
        SnapshotJson = (SELECT @FundRequestId FundRequestId, @ServiceTransactionId ServiceTransactionId,
                               @Decision Status, @Amount Amount, 'NEFT' PaymentMode,
                               @ReviewerUserId ReviewedByUserId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
    WHERE ServiceTransactionId = @ServiceTransactionId;

    INSERT audit.AuditLogs(ActorUserId, OrganizationUnitId, Action, EntityType, EntityId, CorrelationId, DetailsJson, OccurredAtUtc)
    VALUES(@ReviewerUserId, @OrganizationUnitId, CONCAT('FUND_REQUEST_', @Decision), 'FundRequest', CONVERT(nvarchar(36), @FundRequestId), NEWID(),
           (SELECT @Decision Decision, @Amount Amount, @ServiceTransactionId ServiceTransactionId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), SYSUTCDATETIME());
    COMMIT TRANSACTION;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNo = 10)
    INSERT dbo.SchemaVersions(VersionNo, Description) VALUES(10, N'Fund Request manual NEFT proof workflow and atomic approval credit');
GO
