/* Legacy-backed charge slabs and role/operator recharge commissions. */
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

IF OBJECT_ID(N'finance.RechargeCommissionRules', N'U') IS NULL
BEGIN
    CREATE TABLE finance.RechargeCommissionRules
    (
        RechargeCommissionRuleId uniqueidentifier NOT NULL CONSTRAINT PK_RechargeCommissionRules PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        RechargeOperatorId uniqueidentifier NOT NULL,
        RoleId uniqueidentifier NULL,
        CalculationType varchar(20) NOT NULL,
        Rate decimal(19,6) NOT NULL,
        EffectiveFromUtc datetime2(3) NOT NULL CONSTRAINT DF_RechargeCommissionRules_From DEFAULT SYSUTCDATETIME(),
        EffectiveToUtc datetime2(3) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_RechargeCommissionRules_Active DEFAULT 1,
        CONSTRAINT FK_RechargeCommissionRules_Operator FOREIGN KEY (RechargeOperatorId) REFERENCES finance.RechargeOperators(RechargeOperatorId),
        CONSTRAINT FK_RechargeCommissionRules_Role FOREIGN KEY (RoleId) REFERENCES auth.Roles(RoleId),
        CONSTRAINT CK_RechargeCommissionRules_Calculation CHECK (CalculationType IN ('FIXED','PERCENTAGE')),
        CONSTRAINT CK_RechargeCommissionRules_Rate CHECK (Rate >= 0),
        CONSTRAINT CK_RechargeCommissionRules_Validity CHECK (EffectiveToUtc IS NULL OR EffectiveToUtc > EffectiveFromUtc)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'finance.RechargeCommissionRules') AND name = N'UX_RechargeCommissionRules_ActiveRole')
    CREATE UNIQUE INDEX UX_RechargeCommissionRules_ActiveRole
        ON finance.RechargeCommissionRules(RechargeOperatorId, RoleId) WHERE IsActive = 1;
GO

/* Legacy commercial-role mapping: SH/MD/DT/RT -> PLATFORM/CMF/CSF/CSP. */
DECLARE @CommissionSeed TABLE
(
    Label nvarchar(100), Type nvarchar(30), CalculationType varchar(20),
    PlatformRate decimal(19,6), CmfRate decimal(19,6), CsfRate decimal(19,6), CspRate decimal(19,6)
);
INSERT @CommissionSeed VALUES
('airtel','MOBILE','PERCENTAGE',1.60,3.00,3.00,1.50),
('bsnl_special','MOBILE','PERCENTAGE',3.00,3.00,3.00,3.00),
('bsnl_talktime','MOBILE','PERCENTAGE',3.00,3.00,3.00,3.00),
('bsnl_lapu','LAPU','PERCENTAGE',3.00,3.00,3.00,3.00),
('google_play','GOOGLE','FIXED',0.00,0.00,0.00,0.00),
('jio','MOBILE','PERCENTAGE',1.50,1.50,1.50,1.50),
('vi','MOBILE','PERCENTAGE',3.00,3.00,3.00,3.00),
('airtel_digital','DTH','PERCENTAGE',3.00,3.00,3.00,3.00),
('dishtv','DTH','PERCENTAGE',3.00,3.00,3.00,3.00),
('sun_direct','DTH','PERCENTAGE',3.00,3.00,3.00,3.00),
('tata_sky','DTH','PERCENTAGE',3.00,3.00,3.00,3.00),
('videocon_d2h','DTH','PERCENTAGE',3.00,3.00,3.00,3.00);

MERGE finance.RechargeCommissionRules AS target
USING
(
    SELECT o.RechargeOperatorId, r.RoleId, s.CalculationType,
        CASE r.Code WHEN 'PLATFORM_ADMIN' THEN s.PlatformRate WHEN 'CMF_ADMIN' THEN s.CmfRate
             WHEN 'CSF_ADMIN' THEN s.CsfRate ELSE s.CspRate END Rate
    FROM @CommissionSeed s
    JOIN finance.RechargeOperators o ON o.Label = s.Label AND o.Type = s.Type
    CROSS JOIN auth.Roles r
    WHERE r.Code IN ('PLATFORM_ADMIN','CMF_ADMIN','CSF_ADMIN','CSP_USER')
) AS source
ON target.RechargeOperatorId = source.RechargeOperatorId AND target.RoleId = source.RoleId AND target.IsActive = 1
WHEN MATCHED THEN UPDATE SET CalculationType = source.CalculationType, Rate = source.Rate
WHEN NOT MATCHED THEN INSERT(RechargeOperatorId, RoleId, CalculationType, Rate, EffectiveFromUtc, IsActive)
VALUES(source.RechargeOperatorId, source.RoleId, source.CalculationType, source.Rate, '2024-08-16', 1);

DECLARE @WalletService uniqueidentifier = (SELECT ServiceId FROM catalog.Services WHERE Code = 'wallet_transfer');
UPDATE catalog.Services SET IsChargeable = 1, UpdatedAtUtc = SYSUTCDATETIME() WHERE ServiceId = @WalletService;
DECLARE @WalletSlabs TABLE(RoleCode varchar(50), AmountFrom decimal(19,4), AmountTo decimal(19,4), Rate decimal(19,6));
INSERT @WalletSlabs VALUES
('CSP_USER',1,1000,1),('CSP_USER',1001,5000,5),('CSP_USER',5001,10000,10),
('CSF_ADMIN',10,5000,3);
INSERT catalog.PricingRules(ServiceId, RoleId, RuleKind, CalculationType, AmountFrom, AmountTo, Rate, TdsRate, GstRate, EffectiveFromUtc, IsActive)
SELECT @WalletService, r.RoleId, 'CHARGE', 'FIXED', s.AmountFrom, s.AmountTo, s.Rate, 0, 0, '2024-09-05', 1
FROM @WalletSlabs s JOIN auth.Roles r ON r.Code = s.RoleCode
WHERE @WalletService IS NOT NULL AND NOT EXISTS
(
    SELECT 1 FROM catalog.PricingRules p WHERE p.ServiceId = @WalletService AND p.RoleId = r.RoleId
      AND p.RuleKind = 'CHARGE' AND p.AmountFrom = s.AmountFrom AND p.AmountTo = s.AmountTo AND p.IsActive = 1
);

IF NOT EXISTS (SELECT 1 FROM finance.LedgerAccounts WHERE Code = 'REVENUE:WALLET_TRANSFER_CHARGE')
    INSERT finance.LedgerAccounts(Code, Name, AccountType, NormalBalance, Currency, IsActive, CreatedAtUtc)
    VALUES('REVENUE:WALLET_TRANSFER_CHARGE', 'Wallet transfer charge revenue', 'REVENUE', 'C', 'INR', 1, SYSUTCDATETIME());
GO

CREATE OR ALTER PROCEDURE finance.PostWalletToWalletTransfer
    @JournalTransactionId uniqueidentifier, @ServiceTransactionId uniqueidentifier, @IdempotencyKey nvarchar(120),
    @Reference nvarchar(100), @SenderUserId uniqueidentifier, @ReceiverUserId uniqueidentifier,
    @Amount decimal(19,4), @ChargeAmount decimal(19,4) = 0
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF @SenderUserId = @ReceiverUserId THROW 51041, 'Self-transfer is not allowed.', 1;
    IF @Amount < 10 OR @ChargeAmount < 0 THROW 51042, 'Invalid wallet transfer amount.', 1;
    BEGIN TRY
        BEGIN TRAN;
        DECLARE @lock int;
        EXEC @lock = sp_getapplock @Resource=@IdempotencyKey,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
        IF @lock < 0 THROW 51043, 'Could not acquire transfer idempotency lock.', 1;
        IF EXISTS (SELECT 1 FROM finance.JournalTransactions WHERE IdempotencyKey=@IdempotencyKey AND Status='POSTED') BEGIN COMMIT; RETURN; END;
        DECLARE @SenderWallet uniqueidentifier=(SELECT LedgerAccountId FROM finance.LedgerAccounts WHERE UserId=@SenderUserId AND Code=CONCAT('USER:',CONVERT(varchar(36),@SenderUserId),':WALLET') AND IsActive=1);
        DECLARE @ReceiverWallet uniqueidentifier=(SELECT LedgerAccountId FROM finance.LedgerAccounts WHERE UserId=@ReceiverUserId AND Code=CONCAT('USER:',CONVERT(varchar(36),@ReceiverUserId),':WALLET') AND IsActive=1);
        DECLARE @ChargeRevenue uniqueidentifier=(SELECT LedgerAccountId FROM finance.LedgerAccounts WHERE Code='REVENUE:WALLET_TRANSFER_CHARGE' AND IsActive=1);
        SELECT LedgerAccountId FROM finance.LedgerAccounts WITH (UPDLOCK,HOLDLOCK) WHERE LedgerAccountId IN (@SenderWallet,@ReceiverWallet) ORDER BY LedgerAccountId;
        IF @SenderWallet IS NULL OR @ReceiverWallet IS NULL THROW 51044, 'Wallet account not found.', 1;
        IF @ChargeAmount > 0 AND @ChargeRevenue IS NULL THROW 51045, 'Wallet transfer charge account not found.', 1;
        IF @Amount+@ChargeAmount > (SELECT CurrentBalance FROM finance.LedgerAccounts WHERE LedgerAccountId=@SenderWallet) THROW 51022, 'Insufficient wallet balance.', 1;
        INSERT finance.JournalTransactions(JournalTransactionId,ServiceTransactionId,IdempotencyKey,Reference,Description,Status,CreatedByUserId,CreatedAtUtc,PostedAtUtc)
        VALUES(@JournalTransactionId,@ServiceTransactionId,@IdempotencyKey,@Reference,N'Wallet to Wallet transfer','POSTED',@SenderUserId,SYSUTCDATETIME(),SYSUTCDATETIME());
        INSERT finance.JournalEntries(JournalTransactionId,LedgerAccountId,DebitAmount,CreditAmount,Memo)
        VALUES(@JournalTransactionId,@SenderWallet,@Amount+@ChargeAmount,0,N'Wallet transfer sender debit'),
              (@JournalTransactionId,@ReceiverWallet,0,@Amount,N'Wallet transfer receiver credit');
        IF @ChargeAmount > 0 INSERT finance.JournalEntries(JournalTransactionId,LedgerAccountId,DebitAmount,CreditAmount,Memo)
            VALUES(@JournalTransactionId,@ChargeRevenue,0,@ChargeAmount,N'Wallet transfer service charge');
        UPDATE finance.LedgerAccounts SET CurrentBalance=CurrentBalance-@Amount-@ChargeAmount WHERE LedgerAccountId=@SenderWallet;
        UPDATE finance.LedgerAccounts SET CurrentBalance=CurrentBalance+@Amount WHERE LedgerAccountId=@ReceiverWallet;
        IF @ChargeAmount > 0 UPDATE finance.LedgerAccounts SET CurrentBalance=CurrentBalance+@ChargeAmount WHERE LedgerAccountId=@ChargeRevenue;
        COMMIT;
    END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNo = 13)
    INSERT dbo.SchemaVersions(VersionNo, Description) VALUES(13, N'Legacy charge slabs and role-specific recharge commissions');
GO
