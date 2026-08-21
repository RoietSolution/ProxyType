SET NOCOUNT ON;
CREATE TABLE #Verification(CheckName varchar(60) PRIMARY KEY, Passed bit NOT NULL);
GO

DECLARE @platform uniqueidentifier = (SELECT OrganizationUnitId FROM org.OrganizationUnits WHERE UnitType = 'PLATFORM');
DECLARE @hierarchyRejected bit = 0;
BEGIN TRY
    INSERT org.OrganizationUnits(OrganizationUnitId, ParentOrganizationUnitId, UnitType, Code, Name, Status)
    VALUES(NEWID(), @platform, 'CSP', CONCAT('BAD-', LEFT(CONVERT(varchar(36), NEWID()), 8)), 'Invalid CSP', 'ACTIVE');
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() = 51001 SET @hierarchyRejected = 1;
END CATCH;
INSERT #Verification VALUES('Invalid hierarchy rejected', @hierarchyRejected);
GO

BEGIN TRANSACTION;
DECLARE @platform uniqueidentifier = (SELECT OrganizationUnitId FROM org.OrganizationUnits WHERE UnitType = 'PLATFORM');
DECLARE @cmf uniqueidentifier = NEWID();
DECLARE @service uniqueidentifier = (SELECT TOP (1) ServiceId FROM catalog.Services WHERE IsActive = 1 ORDER BY Code);
DECLARE @denyPrecedence bit = 0;
INSERT org.OrganizationUnits(OrganizationUnitId, ParentOrganizationUnitId, UnitType, Code, Name, Status)
VALUES(@cmf, @platform, 'CMF', CONCAT('TEST-', LEFT(CONVERT(varchar(36), @cmf), 8)), 'Rollback CMF', 'ACTIVE');
INSERT catalog.OrganizationServicePermissions(OrganizationUnitId, ServiceId, Effect, Reason)
VALUES(@cmf, @service, 'DENY', 'rollback test');
IF EXISTS(SELECT 1 FROM catalog.fn_EffectiveServices(@cmf) WHERE ServiceId = @service AND IsAllowed = 0 AND DecisionReason = 'ANCESTOR_OR_LOCAL_DENY')
    SET @denyPrecedence = 1;
INSERT #Verification VALUES('Deny precedence enforced', @denyPrecedence);
ROLLBACK TRANSACTION;
GO

DECLARE @journalRejected bit = 0;
DECLARE @journalId uniqueidentifier = NEWID();
DECLARE @entries finance.JournalEntryInput;
INSERT @entries(LedgerAccountId, DebitAmount, CreditAmount, Memo) VALUES(NEWID(), 1, 0, 'unbalanced');
BEGIN TRY
    EXEC finance.PostJournal @JournalTransactionId=@journalId, @IdempotencyKey='rollback-unbalanced',
         @Reference='rollback-unbalanced', @Description='test', @Entries=@entries;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() = 51012 SET @journalRejected = 1;
END CATCH;
INSERT #Verification VALUES('Unbalanced journal rejected', @journalRejected);
GO

IF EXISTS(SELECT 1 FROM #Verification WHERE Passed = 0)
    THROW 51099, 'One or more database integrity checks failed.', 1;
DROP TABLE #Verification;
GO
