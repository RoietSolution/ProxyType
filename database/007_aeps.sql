/* AEPS common foundation and MOCK deployment. No legacy identifiers are used. */
USE [proxytype_DB];
GO
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET ANSI_NULL_DFLT_ON ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID('finance.AepsBanks'))
BEGIN
    CREATE TABLE finance.AepsBanks
    (
        AepsBankId uniqueidentifier NOT NULL CONSTRAINT PK_AepsBanks PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Iin nvarchar(20) NOT NULL,
        Name nvarchar(150) NOT NULL,
        ProviderBankCode nvarchar(50) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_AepsBanks_Active DEFAULT 1,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_AepsBanks_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_AepsBanks_Iin UNIQUE(Iin)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID('finance.AepsTransactionDetails'))
BEGIN
    CREATE TABLE finance.AepsTransactionDetails
    (
        AepsTransactionDetailId uniqueidentifier NOT NULL CONSTRAINT PK_AepsTransactionDetails PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        ServiceTransactionId uniqueidentifier NOT NULL,
        AepsBankId uniqueidentifier NOT NULL,
        TransactionType nvarchar(30) NOT NULL,
        MaskedAadhaar nvarchar(20) NOT NULL,
        MaskedMobile nvarchar(20) NOT NULL,
        DeviceName nvarchar(100) NOT NULL,
        DeviceProvider nvarchar(80) NULL,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_AepsTransactionDetails_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_AepsTransactionDetails_Transaction FOREIGN KEY(ServiceTransactionId) REFERENCES finance.ServiceTransactions(ServiceTransactionId),
        CONSTRAINT FK_AepsTransactionDetails_Bank FOREIGN KEY(AepsBankId) REFERENCES finance.AepsBanks(AepsBankId),
        CONSTRAINT UQ_AepsTransactionDetails_Transaction UNIQUE(ServiceTransactionId),
        CONSTRAINT CK_AepsTransactionDetails_Type CHECK(TransactionType IN ('BALANCE_INQUIRY','MINI_STATEMENT','CASH_WITHDRAWAL'))
    );
END;

MERGE finance.AepsBanks AS target
USING (VALUES ('607152','State Bank of India','SBI'),('608001','HDFC Bank','HDFC'),('607189','ICICI Bank','ICICI'),('607153','Axis Bank','AXIS'),('607396','Bank of Baroda','BOB')) AS source(Iin,Name,ProviderBankCode)
ON target.Iin = source.Iin
WHEN MATCHED THEN UPDATE SET Name=source.Name, ProviderBankCode=source.ProviderBankCode, IsActive=1
WHEN NOT MATCHED THEN INSERT(Iin,Name,ProviderBankCode,IsActive) VALUES(source.Iin,source.Name,source.ProviderBankCode,1);

IF NOT EXISTS (SELECT 1 FROM catalog.Providers WHERE Code = 'aeps_mock')
    INSERT catalog.Providers(Code,Name,Environment,IsEnabled,TimeoutSeconds) VALUES('aeps_mock','AEPS Mock Provider','SANDBOX',1,10);
DECLARE @Service uniqueidentifier=(SELECT ServiceId FROM catalog.Services WHERE Code='aeps');
DECLARE @Provider uniqueidentifier=(SELECT ProviderId FROM catalog.Providers WHERE Code='aeps_mock');
IF @Service IS NOT NULL AND @Provider IS NOT NULL AND NOT EXISTS(SELECT 1 FROM catalog.ServiceProviders WHERE ServiceId=@Service AND ProviderId=@Provider)
    INSERT catalog.ServiceProviders(ServiceId,ProviderId,Priority,IsEnabled,ConfigurationJson) VALUES(@Service,@Provider,1,1,N'{"mode":"MOCK"}');
IF NOT EXISTS (SELECT 1 FROM finance.LedgerAccounts WHERE Code='CLEARING:AEPS')
    INSERT finance.LedgerAccounts(Code,Name,AccountType,NormalBalance,Currency,IsActive) VALUES('CLEARING:AEPS','AEPS clearing','CLEARING','C','INR',1);
DECLARE @Platform uniqueidentifier=(SELECT OrganizationUnitId FROM org.OrganizationUnits WHERE Code='PLATFORM');
IF @Platform IS NOT NULL AND @Service IS NOT NULL AND NOT EXISTS(SELECT 1 FROM catalog.OrganizationServicePermissions WHERE OrganizationUnitId=@Platform AND ServiceId=@Service AND EffectiveToUtc IS NULL)
    INSERT catalog.OrganizationServicePermissions(OrganizationUnitId,ServiceId,Effect,Reason) VALUES(@Platform,@Service,'ALLOW','AEPS deployed with deterministic MOCK provider; LIVE disabled.');
GO

CREATE OR ALTER PROCEDURE finance.PostAepsWalletCredit
    @JournalTransactionId uniqueidentifier, @ServiceTransactionId uniqueidentifier, @IdempotencyKey nvarchar(120),
    @Reference nvarchar(100), @UserId uniqueidentifier, @Amount decimal(19,4)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF @Amount <= 0 THROW 51030,'AEPS credit amount must be positive.',1;
    BEGIN TRANSACTION;
    EXEC sp_getapplock @Resource=@IdempotencyKey,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
    IF EXISTS(SELECT 1 FROM finance.JournalTransactions WITH(UPDLOCK,HOLDLOCK) WHERE IdempotencyKey=@IdempotencyKey)
    BEGIN COMMIT; RETURN; END;
    DECLARE @Aeps uniqueidentifier=(SELECT LedgerAccountId FROM finance.LedgerAccounts WITH(UPDLOCK,HOLDLOCK) WHERE UserId=@UserId AND Code=CONCAT('USER:',CONVERT(varchar(36),@UserId),':AEPS') AND IsActive=1);
    DECLARE @Clearing uniqueidentifier=(SELECT LedgerAccountId FROM finance.LedgerAccounts WITH(UPDLOCK,HOLDLOCK) WHERE Code='CLEARING:AEPS' AND IsActive=1);
    IF @Aeps IS NULL OR @Clearing IS NULL THROW 51031,'AEPS wallet accounts are not configured.',1;
    INSERT finance.JournalTransactions(JournalTransactionId,ServiceTransactionId,IdempotencyKey,Reference,Description,Status,CreatedByUserId,PostedAtUtc)
    VALUES(@JournalTransactionId,@ServiceTransactionId,@IdempotencyKey,@Reference,'AEPS wallet credit','POSTED',@UserId,SYSUTCDATETIME());
    INSERT finance.JournalEntries(JournalTransactionId,LedgerAccountId,DebitAmount,CreditAmount,Memo)
    VALUES(@JournalTransactionId,@Clearing,@Amount,0,'AEPS clearing debit'),(@JournalTransactionId,@Aeps,0,@Amount,'AEPS customer credit');
    UPDATE finance.LedgerAccounts SET CurrentBalance=CurrentBalance-@Amount WHERE LedgerAccountId=@Clearing;
    UPDATE finance.LedgerAccounts SET CurrentBalance=CurrentBalance+@Amount WHERE LedgerAccountId=@Aeps;
    COMMIT;
END;
GO
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNo=7)
    INSERT dbo.SchemaVersions(VersionNo,Description) VALUES(7,N'AEPS common transaction foundation, bank master, MOCK provider route and AEPS wallet credit');
GO
