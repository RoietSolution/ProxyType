/* Fund payment dates and CSP-specific recharge commission overrides. */
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

IF COL_LENGTH('finance.FundRequests', 'TransactionDate') IS NULL
    ALTER TABLE finance.FundRequests ADD TransactionDate date NULL;
GO

IF EXISTS
(
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('finance.FundRequests') AND name = 'TransactionDate' AND is_nullable = 1
)
BEGIN
    UPDATE finance.FundRequests SET TransactionDate = CONVERT(date, CreatedAtUtc) WHERE TransactionDate IS NULL;
    ALTER TABLE finance.FundRequests ALTER COLUMN TransactionDate date NOT NULL;
END;
IF NOT EXISTS
(
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID('finance.FundRequests') AND c.name = 'TransactionDate'
)
    ALTER TABLE finance.FundRequests ADD CONSTRAINT DF_FundRequests_TransactionDate DEFAULT CONVERT(date, SYSUTCDATETIME()) FOR TransactionDate;
GO

IF COL_LENGTH('finance.RechargeCommissionRules', 'UserId') IS NULL
    ALTER TABLE finance.RechargeCommissionRules ADD UserId uniqueidentifier NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_RechargeCommissionRules_User')
    ALTER TABLE finance.RechargeCommissionRules ADD CONSTRAINT FK_RechargeCommissionRules_User
        FOREIGN KEY (UserId) REFERENCES auth.Users(UserId);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_RechargeCommissionRules_Scope')
    ALTER TABLE finance.RechargeCommissionRules ADD CONSTRAINT CK_RechargeCommissionRules_Scope
        CHECK (RoleId IS NULL OR UserId IS NULL);
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('finance.RechargeCommissionRules') AND name = 'UX_RechargeCommissionRules_ActiveRole')
    DROP INDEX UX_RechargeCommissionRules_ActiveRole ON finance.RechargeCommissionRules;
CREATE UNIQUE INDEX UX_RechargeCommissionRules_ActiveRole
    ON finance.RechargeCommissionRules(RechargeOperatorId, RoleId)
    WHERE IsActive = 1 AND RoleId IS NOT NULL AND UserId IS NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('finance.RechargeCommissionRules') AND name = 'UX_RechargeCommissionRules_ActiveUser')
    CREATE UNIQUE INDEX UX_RechargeCommissionRules_ActiveUser
        ON finance.RechargeCommissionRules(RechargeOperatorId, UserId)
        WHERE IsActive = 1 AND UserId IS NOT NULL AND RoleId IS NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('finance.RechargeCommissionRules') AND name = 'IX_RechargeCommissionRules_UserLookup')
    CREATE INDEX IX_RechargeCommissionRules_UserLookup
        ON finance.RechargeCommissionRules(UserId, IsActive, EffectiveFromUtc, EffectiveToUtc)
        INCLUDE(RechargeOperatorId, CalculationType, Rate);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNo = 14)
    INSERT dbo.SchemaVersions(VersionNo, Description) VALUES(14, N'Fund transaction date and CSP recharge commission overrides');
GO
