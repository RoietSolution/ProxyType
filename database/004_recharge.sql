/* Recharge service deployment. Provider mode is MOCK until a verified live contract exists. */
SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM catalog.Providers WHERE Code = 'recharge_mock')
    INSERT catalog.Providers(Code, Name, Environment, IsEnabled, TimeoutSeconds)
    VALUES('recharge_mock', 'Recharge Mock Provider', 'SANDBOX', 1, 30);

DECLARE @Service uniqueidentifier = (SELECT ServiceId FROM catalog.Services WHERE Code = 'recharge_v1');
DECLARE @Provider uniqueidentifier = (SELECT ProviderId FROM catalog.Providers WHERE Code = 'recharge_mock');
IF @Service IS NOT NULL AND @Provider IS NOT NULL AND NOT EXISTS
(
    SELECT 1 FROM catalog.ServiceProviders WHERE ServiceId = @Service AND ProviderId = @Provider
)
    INSERT catalog.ServiceProviders(ServiceId, ProviderId, Priority, IsEnabled)
    VALUES(@Service, @Provider, 10, 1);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID('finance.RechargeOperators'))
BEGIN
    CREATE TABLE finance.RechargeOperators
    (
        RechargeOperatorId uniqueidentifier NOT NULL CONSTRAINT PK_RechargeOperators PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Name nvarchar(150) NOT NULL,
        Label nvarchar(100) NOT NULL,
        Type nvarchar(30) NOT NULL,
        OperatorKey nvarchar(50) NOT NULL,
        CommissionType nvarchar(20) NOT NULL CONSTRAINT CK_RechargeOperators_CommissionType CHECK (CommissionType IN ('FIXED','PERCENTAGE')),
        CommissionValue decimal(18,4) NOT NULL CONSTRAINT DF_RechargeOperators_CommissionValue DEFAULT 0,
        IsActive bit NOT NULL CONSTRAINT DF_RechargeOperators_IsActive DEFAULT 1,
        CONSTRAINT UQ_RechargeOperators_LabelType UNIQUE(Label, Type)
    );
END;

MERGE finance.RechargeOperators AS target
USING (VALUES
    ('Airtel','airtel','MOBILE','3'),('BSNL Special Tariff','bsnl_special','MOBILE','5'),
    ('BSNL Talktime','bsnl_talktime','MOBILE','4'),('BSNL Special LAPU','bsnl_lapu','LAPU','BSNL5'),
    ('Google Play Voucher','google_play','GOOGLE','GGLPV'),('Jio','jio','MOBILE','116'),('Vi','vi','MOBILE','37'),
    ('Airtel Digital TV','airtel_digital','DTH','51'),('Dish TV','dishtv','DTH','53'),
    ('Sun Direct','sun_direct','DTH','54'),('Tata Sky','tata_sky','DTH','55'),('Videocon D2H','videocon_d2h','DTH','56')
) AS source(Name, Label, Type, OperatorKey)
ON target.Label = source.Label AND target.Type = source.Type
WHEN MATCHED THEN UPDATE SET Name = source.Name, OperatorKey = source.OperatorKey, IsActive = 1
WHEN NOT MATCHED THEN INSERT(Name, Label, Type, OperatorKey, CommissionType, CommissionValue, IsActive)
VALUES(source.Name, source.Label, source.Type, source.OperatorKey, 'FIXED', 0, 1);

IF NOT EXISTS (SELECT 1 FROM finance.LedgerAccounts WHERE Code = 'CLEARING:RECHARGE')
    INSERT finance.LedgerAccounts(Code, Name, AccountType, NormalBalance, Currency, IsActive, CreatedAtUtc)
    VALUES('CLEARING:RECHARGE', 'Recharge clearing', 'CLEARING', 'C', 'INR', 1, SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM finance.LedgerAccounts WHERE Code = 'EXPENSE:RECHARGE_COMMISSION')
    INSERT finance.LedgerAccounts(Code, Name, AccountType, NormalBalance, Currency, IsActive, CreatedAtUtc)
    VALUES('EXPENSE:RECHARGE_COMMISSION', 'Recharge commission expense', 'EXPENSE', 'D', 'INR', 1, SYSUTCDATETIME());

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('finance.JournalTransactions') AND name = 'UQ_JournalTransactions_Reversal')
    ALTER TABLE finance.JournalTransactions DROP CONSTRAINT UQ_JournalTransactions_Reversal;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('finance.JournalTransactions') AND name = 'UX_JournalTransactions_Reversal')
    CREATE UNIQUE INDEX UX_JournalTransactions_Reversal ON finance.JournalTransactions(ReversalOfJournalId) WHERE ReversalOfJournalId IS NOT NULL;

DECLARE @Platform uniqueidentifier = (SELECT OrganizationUnitId FROM org.OrganizationUnits WHERE Code = 'PLATFORM');
IF @Platform IS NOT NULL AND @Service IS NOT NULL AND NOT EXISTS
(
    SELECT 1 FROM catalog.OrganizationServicePermissions WHERE OrganizationUnitId = @Platform AND ServiceId = @Service AND EffectiveToUtc IS NULL
)
    INSERT catalog.OrganizationServicePermissions(OrganizationUnitId, ServiceId, Effect, Reason)
    VALUES(@Platform, @Service, 'ALLOW', 'Recharge service grant; provider mode remains MOCK.');

GO

CREATE OR ALTER PROCEDURE finance.PostRechargeWalletDebit
    @JournalTransactionId uniqueidentifier,
    @ServiceTransactionId uniqueidentifier,
    @IdempotencyKey nvarchar(100),
    @Reference nvarchar(100),
    @UserId uniqueidentifier,
    @DebitAmount decimal(19,4),
    @CommissionAmount decimal(19,4)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
    DECLARE @lockResult int;
    EXEC @lockResult = sp_getapplock @Resource = @IdempotencyKey, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
    IF @lockResult < 0 THROW 51023, 'Could not acquire recharge idempotency lock.', 1;
    IF EXISTS (SELECT 1 FROM finance.JournalTransactions WHERE IdempotencyKey = @IdempotencyKey AND Status = 'POSTED')
    BEGIN COMMIT; RETURN; END;

    DECLARE @Wallet uniqueidentifier = (SELECT LedgerAccountId FROM finance.LedgerAccounts WITH (UPDLOCK, HOLDLOCK) WHERE UserId = @UserId AND Code = CONCAT('USER:', CONVERT(varchar(36), @UserId), ':WALLET') AND IsActive = 1);
    DECLARE @Clearing uniqueidentifier = (SELECT LedgerAccountId FROM finance.LedgerAccounts WHERE Code = 'CLEARING:RECHARGE' AND IsActive = 1);
    DECLARE @Commission uniqueidentifier = (SELECT LedgerAccountId FROM finance.LedgerAccounts WHERE Code = 'EXPENSE:RECHARGE_COMMISSION' AND IsActive = 1);
    IF @Wallet IS NULL THROW 51021, 'Wallet account not found.', 1;
    IF @Clearing IS NULL OR @Commission IS NULL THROW 51024, 'Recharge ledger accounts are not configured.', 1;
    IF @DebitAmount < 0 OR @CommissionAmount < 0 THROW 51025, 'Invalid recharge ledger amount.', 1;
    DECLARE @NetDebit decimal(19,4) = @DebitAmount - @CommissionAmount;
    IF @NetDebit > (SELECT CurrentBalance FROM finance.LedgerAccounts WHERE LedgerAccountId = @Wallet) THROW 51022, 'Insufficient wallet balance.', 1;

    INSERT finance.JournalTransactions(JournalTransactionId, ServiceTransactionId, IdempotencyKey, Reference, Description, Status, CreatedByUserId, CreatedAtUtc, PostedAtUtc)
    VALUES(@JournalTransactionId, @ServiceTransactionId, @IdempotencyKey, @Reference, 'Recharge wallet debit', 'POSTED', @UserId, SYSUTCDATETIME(), SYSUTCDATETIME());
    INSERT finance.JournalEntries(JournalTransactionId, LedgerAccountId, DebitAmount, CreditAmount, Memo)
    VALUES(@JournalTransactionId, @Wallet, @DebitAmount, 0, 'Recharge principal debit'),
          (@JournalTransactionId, @Clearing, 0, @DebitAmount, 'Recharge clearing credit');
    IF @CommissionAmount > 0
    BEGIN
        INSERT finance.JournalEntries(JournalTransactionId, LedgerAccountId, DebitAmount, CreditAmount, Memo)
        VALUES(@JournalTransactionId, @Wallet, 0, @CommissionAmount, 'Recharge commission credit'),
              (@JournalTransactionId, @Commission, @CommissionAmount, 0, 'Recharge commission expense');
    END;
    UPDATE finance.LedgerAccounts SET CurrentBalance = CurrentBalance - @DebitAmount + @CommissionAmount WHERE LedgerAccountId = @Wallet;
    UPDATE finance.LedgerAccounts SET CurrentBalance = CurrentBalance + @DebitAmount WHERE LedgerAccountId = @Clearing;
    UPDATE finance.LedgerAccounts SET CurrentBalance = CurrentBalance + @CommissionAmount WHERE LedgerAccountId = @Commission;
    COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK;
        THROW;
    END CATCH;
END;

GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNo = 4)
    INSERT dbo.SchemaVersions(VersionNo, Description) VALUES(4, N'Recharge operators, mock provider route, commission ledger and wallet procedure');
