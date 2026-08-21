USE [master];
GO

IF DB_ID(N'proxytype_DB') IS NULL
BEGIN
    CREATE DATABASE [proxytype_DB];
END;
GO

USE [proxytype_DB];
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'auth') EXEC(N'CREATE SCHEMA [auth]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'org') EXEC(N'CREATE SCHEMA [org]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'catalog') EXEC(N'CREATE SCHEMA [catalog]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'finance') EXEC(N'CREATE SCHEMA [finance]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'integration') EXEC(N'CREATE SCHEMA [integration]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'audit') EXEC(N'CREATE SCHEMA [audit]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'master') EXEC(N'CREATE SCHEMA [master]');
GO

IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SchemaVersions
    (
        VersionNo int NOT NULL CONSTRAINT PK_SchemaVersions PRIMARY KEY,
        Description nvarchar(250) NOT NULL,
        AppliedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_SchemaVersions_AppliedAtUtc DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'master.Regions', N'U') IS NULL
BEGIN
    CREATE TABLE master.Regions
    (
        RegionId uniqueidentifier NOT NULL CONSTRAINT DF_Regions_Id DEFAULT NEWSEQUENTIALID(),
        Code nvarchar(30) NOT NULL,
        Name nvarchar(100) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_Regions_Active DEFAULT 1,
        LegacySystem nvarchar(30) NULL,
        LegacyId nvarchar(50) NULL,
        CONSTRAINT PK_Regions PRIMARY KEY (RegionId),
        CONSTRAINT UQ_Regions_Code UNIQUE (Code)
    );
    CREATE UNIQUE INDEX UX_Regions_Legacy ON master.Regions(LegacySystem, LegacyId)
        WHERE LegacySystem IS NOT NULL AND LegacyId IS NOT NULL;
END;
GO

IF OBJECT_ID(N'master.States', N'U') IS NULL
BEGIN
    CREATE TABLE master.States
    (
        StateId uniqueidentifier NOT NULL CONSTRAINT DF_States_Id DEFAULT NEWSEQUENTIALID(),
        RegionId uniqueidentifier NULL,
        Code nvarchar(30) NOT NULL,
        Name nvarchar(100) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_States_Active DEFAULT 1,
        LegacySystem nvarchar(30) NULL,
        LegacyId nvarchar(50) NULL,
        CONSTRAINT PK_States PRIMARY KEY (StateId),
        CONSTRAINT FK_States_Regions FOREIGN KEY (RegionId) REFERENCES master.Regions(RegionId),
        CONSTRAINT UQ_States_Code UNIQUE (Code)
    );
    CREATE INDEX IX_States_RegionId ON master.States(RegionId);
    CREATE UNIQUE INDEX UX_States_Legacy ON master.States(LegacySystem, LegacyId)
        WHERE LegacySystem IS NOT NULL AND LegacyId IS NOT NULL;
END;
GO

IF OBJECT_ID(N'master.Districts', N'U') IS NULL
BEGIN
    CREATE TABLE master.Districts
    (
        DistrictId uniqueidentifier NOT NULL CONSTRAINT DF_Districts_Id DEFAULT NEWSEQUENTIALID(),
        StateId uniqueidentifier NOT NULL,
        Code nvarchar(30) NOT NULL,
        Name nvarchar(100) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_Districts_Active DEFAULT 1,
        LegacySystem nvarchar(30) NULL,
        LegacyId nvarchar(50) NULL,
        CONSTRAINT PK_Districts PRIMARY KEY (DistrictId),
        CONSTRAINT FK_Districts_States FOREIGN KEY (StateId) REFERENCES master.States(StateId),
        CONSTRAINT UQ_Districts_StateCode UNIQUE (StateId, Code)
    );
    CREATE INDEX IX_Districts_StateId_Name ON master.Districts(StateId, Name);
    CREATE UNIQUE INDEX UX_Districts_Legacy ON master.Districts(LegacySystem, LegacyId)
        WHERE LegacySystem IS NOT NULL AND LegacyId IS NOT NULL;
END;
GO

IF OBJECT_ID(N'master.Banks', N'U') IS NULL
BEGIN
    CREATE TABLE master.Banks
    (
        BankId uniqueidentifier NOT NULL CONSTRAINT DF_Banks_Id DEFAULT NEWSEQUENTIALID(),
        Code nvarchar(30) NOT NULL,
        Name nvarchar(150) NOT NULL,
        IfscPrefix nvarchar(4) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_Banks_Active DEFAULT 1,
        LegacySystem nvarchar(30) NULL,
        LegacyId nvarchar(50) NULL,
        CONSTRAINT PK_Banks PRIMARY KEY (BankId),
        CONSTRAINT UQ_Banks_Code UNIQUE (Code)
    );
    CREATE INDEX IX_Banks_Name ON master.Banks(Name);
    CREATE UNIQUE INDEX UX_Banks_Legacy ON master.Banks(LegacySystem, LegacyId)
        WHERE LegacySystem IS NOT NULL AND LegacyId IS NOT NULL;
END;
GO

IF OBJECT_ID(N'auth.Users', N'U') IS NULL
BEGIN
    CREATE TABLE auth.Users
    (
        UserId uniqueidentifier NOT NULL CONSTRAINT DF_Users_Id DEFAULT NEWSEQUENTIALID(),
        Username nvarchar(100) NOT NULL,
        NormalizedUsername AS UPPER(LTRIM(RTRIM(Username))) PERSISTED,
        Email nvarchar(256) NOT NULL,
        NormalizedEmail AS UPPER(LTRIM(RTRIM(Email))) PERSISTED,
        Mobile nvarchar(20) NULL,
        DisplayName nvarchar(150) NOT NULL,
        PasswordHash nvarchar(500) NOT NULL,
        SecurityStamp uniqueidentifier NOT NULL CONSTRAINT DF_Users_SecurityStamp DEFAULT NEWID(),
        IsActive bit NOT NULL CONSTRAINT DF_Users_Active DEFAULT 1,
        MustChangePassword bit NOT NULL CONSTRAINT DF_Users_MustChange DEFAULT 0,
        AccessFailedCount int NOT NULL CONSTRAINT DF_Users_AccessFailed DEFAULT 0,
        LockoutEndUtc datetime2(3) NULL,
        LastLoginUtc datetime2(3) NULL,
        PasswordChangedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_Users_PasswordChanged DEFAULT SYSUTCDATETIME(),
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_Users_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_Users_Updated DEFAULT SYSUTCDATETIME(),
        CreatedByUserId uniqueidentifier NULL,
        LegacySystem nvarchar(30) NULL,
        LegacyId nvarchar(50) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_Users PRIMARY KEY (UserId),
        CONSTRAINT FK_Users_CreatedBy FOREIGN KEY (CreatedByUserId) REFERENCES auth.Users(UserId),
        CONSTRAINT CK_Users_AccessFailed CHECK (AccessFailedCount >= 0)
    );
    CREATE UNIQUE INDEX UX_Users_NormalizedUsername ON auth.Users(NormalizedUsername);
    CREATE UNIQUE INDEX UX_Users_NormalizedEmail ON auth.Users(NormalizedEmail);
    CREATE UNIQUE INDEX UX_Users_Mobile ON auth.Users(Mobile) WHERE Mobile IS NOT NULL;
    CREATE UNIQUE INDEX UX_Users_Legacy ON auth.Users(LegacySystem, LegacyId)
        WHERE LegacySystem IS NOT NULL AND LegacyId IS NOT NULL;
END;
GO

IF OBJECT_ID(N'auth.Roles', N'U') IS NULL
BEGIN
    CREATE TABLE auth.Roles
    (
        RoleId uniqueidentifier NOT NULL CONSTRAINT DF_Roles_Id DEFAULT NEWSEQUENTIALID(),
        Code nvarchar(50) NOT NULL,
        Name nvarchar(100) NOT NULL,
        Description nvarchar(300) NULL,
        IsSystem bit NOT NULL CONSTRAINT DF_Roles_System DEFAULT 0,
        CONSTRAINT PK_Roles PRIMARY KEY (RoleId),
        CONSTRAINT UQ_Roles_Code UNIQUE (Code)
    );
END;
GO

IF OBJECT_ID(N'auth.UserRoles', N'U') IS NULL
BEGIN
    CREATE TABLE auth.UserRoles
    (
        UserId uniqueidentifier NOT NULL,
        RoleId uniqueidentifier NOT NULL,
        AssignedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_UserRoles_Assigned DEFAULT SYSUTCDATETIME(),
        AssignedByUserId uniqueidentifier NULL,
        CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId),
        CONSTRAINT FK_UserRoles_User FOREIGN KEY (UserId) REFERENCES auth.Users(UserId) ON DELETE CASCADE,
        CONSTRAINT FK_UserRoles_Role FOREIGN KEY (RoleId) REFERENCES auth.Roles(RoleId),
        CONSTRAINT FK_UserRoles_AssignedBy FOREIGN KEY (AssignedByUserId) REFERENCES auth.Users(UserId)
    );
    CREATE INDEX IX_UserRoles_RoleId ON auth.UserRoles(RoleId);
END;
GO

IF OBJECT_ID(N'auth.RefreshTokens', N'U') IS NULL
BEGIN
    CREATE TABLE auth.RefreshTokens
    (
        RefreshTokenId uniqueidentifier NOT NULL CONSTRAINT DF_RefreshTokens_Id DEFAULT NEWSEQUENTIALID(),
        UserId uniqueidentifier NOT NULL,
        TokenHash char(64) NOT NULL,
        FamilyId uniqueidentifier NOT NULL,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_RefreshTokens_Created DEFAULT SYSUTCDATETIME(),
        ExpiresAtUtc datetime2(3) NOT NULL,
        UsedAtUtc datetime2(3) NULL,
        RevokedAtUtc datetime2(3) NULL,
        ReplacedByTokenId uniqueidentifier NULL,
        CreatedByIp nvarchar(64) NULL,
        UserAgent nvarchar(500) NULL,
        CONSTRAINT PK_RefreshTokens PRIMARY KEY (RefreshTokenId),
        CONSTRAINT FK_RefreshTokens_User FOREIGN KEY (UserId) REFERENCES auth.Users(UserId) ON DELETE CASCADE,
        CONSTRAINT FK_RefreshTokens_ReplacedBy FOREIGN KEY (ReplacedByTokenId) REFERENCES auth.RefreshTokens(RefreshTokenId),
        CONSTRAINT UQ_RefreshTokens_TokenHash UNIQUE (TokenHash),
        CONSTRAINT CK_RefreshTokens_Expiry CHECK (ExpiresAtUtc > CreatedAtUtc)
    );
    CREATE INDEX IX_RefreshTokens_UserFamily ON auth.RefreshTokens(UserId, FamilyId);
    CREATE INDEX IX_RefreshTokens_Expiry ON auth.RefreshTokens(ExpiresAtUtc) INCLUDE (RevokedAtUtc, UsedAtUtc);
END;
GO

IF OBJECT_ID(N'auth.LoginAudits', N'U') IS NULL
BEGIN
    CREATE TABLE auth.LoginAudits
    (
        LoginAuditId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_LoginAudits PRIMARY KEY,
        UserId uniqueidentifier NULL,
        UsernameAttempted nvarchar(100) NOT NULL,
        Succeeded bit NOT NULL,
        FailureCode nvarchar(50) NULL,
        IpAddress nvarchar(64) NULL,
        UserAgent nvarchar(500) NULL,
        OccurredAtUtc datetime2(3) NOT NULL CONSTRAINT DF_LoginAudits_Occurred DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_LoginAudits_User FOREIGN KEY (UserId) REFERENCES auth.Users(UserId)
    );
    CREATE INDEX IX_LoginAudits_UserOccurred ON auth.LoginAudits(UserId, OccurredAtUtc DESC);
END;
GO

IF OBJECT_ID(N'org.OrganizationUnits', N'U') IS NULL
BEGIN
    CREATE TABLE org.OrganizationUnits
    (
        OrganizationUnitId uniqueidentifier NOT NULL CONSTRAINT DF_OrgUnits_Id DEFAULT NEWSEQUENTIALID(),
        ParentOrganizationUnitId uniqueidentifier NULL,
        UnitType varchar(20) NOT NULL,
        Code nvarchar(30) NOT NULL,
        Name nvarchar(150) NOT NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_OrgUnits_Status DEFAULT 'PENDING',
        RegionId uniqueidentifier NULL,
        StateId uniqueidentifier NULL,
        DistrictId uniqueidentifier NULL,
        Email nvarchar(256) NULL,
        Mobile nvarchar(20) NULL,
        TaxIdentifier nvarchar(30) NULL,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_OrgUnits_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_OrgUnits_Updated DEFAULT SYSUTCDATETIME(),
        CreatedByUserId uniqueidentifier NULL,
        LegacySystem nvarchar(30) NULL,
        LegacyId nvarchar(50) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_OrganizationUnits PRIMARY KEY (OrganizationUnitId),
        CONSTRAINT FK_OrgUnits_Parent FOREIGN KEY (ParentOrganizationUnitId) REFERENCES org.OrganizationUnits(OrganizationUnitId),
        CONSTRAINT FK_OrgUnits_Region FOREIGN KEY (RegionId) REFERENCES master.Regions(RegionId),
        CONSTRAINT FK_OrgUnits_State FOREIGN KEY (StateId) REFERENCES master.States(StateId),
        CONSTRAINT FK_OrgUnits_District FOREIGN KEY (DistrictId) REFERENCES master.Districts(DistrictId),
        CONSTRAINT FK_OrgUnits_CreatedBy FOREIGN KEY (CreatedByUserId) REFERENCES auth.Users(UserId),
        CONSTRAINT CK_OrgUnits_Type CHECK (UnitType IN ('PLATFORM','CMF','CSF','CSP')),
        CONSTRAINT CK_OrgUnits_Status CHECK (Status IN ('PENDING','ACTIVE','SUSPENDED','REJECTED','CLOSED')),
        CONSTRAINT CK_OrgUnits_NotSelf CHECK (ParentOrganizationUnitId IS NULL OR ParentOrganizationUnitId <> OrganizationUnitId),
        CONSTRAINT UQ_OrgUnits_Code UNIQUE (Code)
    );
    CREATE INDEX IX_OrgUnits_ParentType ON org.OrganizationUnits(ParentOrganizationUnitId, UnitType, Status);
    CREATE UNIQUE INDEX UX_OrgUnits_Legacy ON org.OrganizationUnits(LegacySystem, LegacyId)
        WHERE LegacySystem IS NOT NULL AND LegacyId IS NOT NULL;
END;
GO

CREATE OR ALTER TRIGGER org.TR_OrganizationUnits_ValidateHierarchy
ON org.OrganizationUnits
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        LEFT JOIN org.OrganizationUnits p ON p.OrganizationUnitId = i.ParentOrganizationUnitId
        WHERE (i.UnitType = 'PLATFORM' AND i.ParentOrganizationUnitId IS NOT NULL)
           OR (i.UnitType = 'CMF' AND (p.UnitType <> 'PLATFORM' OR p.UnitType IS NULL))
           OR (i.UnitType = 'CSF' AND (p.UnitType <> 'CMF' OR p.UnitType IS NULL))
           OR (i.UnitType = 'CSP' AND (p.UnitType <> 'CSF' OR p.UnitType IS NULL))
    )
    BEGIN
        THROW 51001, 'Invalid organization hierarchy. Required path is PLATFORM -> CMF -> CSF -> CSP.', 1;
    END;
END;
GO

IF OBJECT_ID(N'org.OrganizationMemberships', N'U') IS NULL
BEGIN
    CREATE TABLE org.OrganizationMemberships
    (
        OrganizationMembershipId uniqueidentifier NOT NULL CONSTRAINT DF_OrgMemberships_Id DEFAULT NEWSEQUENTIALID(),
        OrganizationUnitId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        IsPrimary bit NOT NULL CONSTRAINT DF_OrgMemberships_Primary DEFAULT 0,
        IsActive bit NOT NULL CONSTRAINT DF_OrgMemberships_Active DEFAULT 1,
        ValidFromUtc datetime2(3) NOT NULL CONSTRAINT DF_OrgMemberships_From DEFAULT SYSUTCDATETIME(),
        ValidToUtc datetime2(3) NULL,
        CONSTRAINT PK_OrganizationMemberships PRIMARY KEY (OrganizationMembershipId),
        CONSTRAINT FK_OrgMemberships_Unit FOREIGN KEY (OrganizationUnitId) REFERENCES org.OrganizationUnits(OrganizationUnitId),
        CONSTRAINT FK_OrgMemberships_User FOREIGN KEY (UserId) REFERENCES auth.Users(UserId) ON DELETE CASCADE,
        CONSTRAINT UQ_OrgMemberships_UserUnit UNIQUE (UserId, OrganizationUnitId),
        CONSTRAINT CK_OrgMemberships_Validity CHECK (ValidToUtc IS NULL OR ValidToUtc > ValidFromUtc)
    );
    CREATE INDEX IX_OrgMemberships_UnitActive ON org.OrganizationMemberships(OrganizationUnitId, IsActive) INCLUDE (UserId);
    CREATE UNIQUE INDEX UX_OrgMemberships_Primary ON org.OrganizationMemberships(UserId) WHERE IsPrimary = 1 AND IsActive = 1;
END;
GO

IF OBJECT_ID(N'catalog.ServiceCategories', N'U') IS NULL
BEGIN
    CREATE TABLE catalog.ServiceCategories
    (
        ServiceCategoryId uniqueidentifier NOT NULL CONSTRAINT DF_ServiceCategories_Id DEFAULT NEWSEQUENTIALID(),
        Code nvarchar(50) NOT NULL,
        Name nvarchar(100) NOT NULL,
        SortOrder int NOT NULL CONSTRAINT DF_ServiceCategories_Sort DEFAULT 0,
        CONSTRAINT PK_ServiceCategories PRIMARY KEY (ServiceCategoryId),
        CONSTRAINT UQ_ServiceCategories_Code UNIQUE (Code)
    );
END;
GO

IF OBJECT_ID(N'catalog.Services', N'U') IS NULL
BEGIN
    CREATE TABLE catalog.Services
    (
        ServiceId uniqueidentifier NOT NULL CONSTRAINT DF_Services_Id DEFAULT NEWSEQUENTIALID(),
        ServiceCategoryId uniqueidentifier NOT NULL,
        Code nvarchar(60) NOT NULL,
        Name nvarchar(150) NOT NULL,
        Description nvarchar(500) NULL,
        Icon nvarchar(100) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_Services_Active DEFAULT 0,
        IsChargeable bit NOT NULL CONSTRAINT DF_Services_Chargeable DEFAULT 0,
        IsCommissionable bit NOT NULL CONSTRAINT DF_Services_Commissionable DEFAULT 0,
        RequiresKyc bit NOT NULL CONSTRAINT DF_Services_Kyc DEFAULT 0,
        SortOrder int NOT NULL CONSTRAINT DF_Services_Sort DEFAULT 0,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_Services_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_Services_Updated DEFAULT SYSUTCDATETIME(),
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_Services PRIMARY KEY (ServiceId),
        CONSTRAINT FK_Services_Category FOREIGN KEY (ServiceCategoryId) REFERENCES catalog.ServiceCategories(ServiceCategoryId),
        CONSTRAINT UQ_Services_Code UNIQUE (Code)
    );
    CREATE INDEX IX_Services_CategoryActive ON catalog.Services(ServiceCategoryId, IsActive, SortOrder);
END;
GO

IF OBJECT_ID(N'catalog.Providers', N'U') IS NULL
BEGIN
    CREATE TABLE catalog.Providers
    (
        ProviderId uniqueidentifier NOT NULL CONSTRAINT DF_Providers_Id DEFAULT NEWSEQUENTIALID(),
        Code nvarchar(60) NOT NULL,
        Name nvarchar(150) NOT NULL,
        Environment varchar(20) NOT NULL CONSTRAINT DF_Providers_Environment DEFAULT 'SANDBOX',
        BaseUrl nvarchar(500) NULL,
        SecretReference nvarchar(300) NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_Providers_Enabled DEFAULT 0,
        TimeoutSeconds int NOT NULL CONSTRAINT DF_Providers_Timeout DEFAULT 30,
        CONSTRAINT PK_Providers PRIMARY KEY (ProviderId),
        CONSTRAINT UQ_Providers_Code UNIQUE (Code),
        CONSTRAINT CK_Providers_Environment CHECK (Environment IN ('SANDBOX','PRODUCTION')),
        CONSTRAINT CK_Providers_Timeout CHECK (TimeoutSeconds BETWEEN 1 AND 300)
    );
END;
GO

IF OBJECT_ID(N'catalog.ServiceProviders', N'U') IS NULL
BEGIN
    CREATE TABLE catalog.ServiceProviders
    (
        ServiceProviderId uniqueidentifier NOT NULL CONSTRAINT DF_ServiceProviders_Id DEFAULT NEWSEQUENTIALID(),
        ServiceId uniqueidentifier NOT NULL,
        ProviderId uniqueidentifier NOT NULL,
        Priority int NOT NULL CONSTRAINT DF_ServiceProviders_Priority DEFAULT 100,
        IsEnabled bit NOT NULL CONSTRAINT DF_ServiceProviders_Enabled DEFAULT 0,
        ConfigurationJson nvarchar(max) NULL,
        CONSTRAINT PK_ServiceProviders PRIMARY KEY (ServiceProviderId),
        CONSTRAINT FK_ServiceProviders_Service FOREIGN KEY (ServiceId) REFERENCES catalog.Services(ServiceId),
        CONSTRAINT FK_ServiceProviders_Provider FOREIGN KEY (ProviderId) REFERENCES catalog.Providers(ProviderId),
        CONSTRAINT UQ_ServiceProviders UNIQUE (ServiceId, ProviderId),
        CONSTRAINT CK_ServiceProviders_ConfigJson CHECK (ConfigurationJson IS NULL OR ISJSON(ConfigurationJson) = 1)
    );
    CREATE INDEX IX_ServiceProviders_Routing ON catalog.ServiceProviders(ServiceId, IsEnabled, Priority);
END;
GO

IF OBJECT_ID(N'catalog.OrganizationServicePermissions', N'U') IS NULL
BEGIN
    CREATE TABLE catalog.OrganizationServicePermissions
    (
        OrganizationServicePermissionId uniqueidentifier NOT NULL CONSTRAINT DF_OrgServicePermissions_Id DEFAULT NEWSEQUENTIALID(),
        OrganizationUnitId uniqueidentifier NOT NULL,
        ServiceId uniqueidentifier NOT NULL,
        Effect varchar(10) NOT NULL,
        Reason nvarchar(300) NULL,
        EffectiveFromUtc datetime2(3) NOT NULL CONSTRAINT DF_OrgServicePermissions_From DEFAULT SYSUTCDATETIME(),
        EffectiveToUtc datetime2(3) NULL,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_OrgServicePermissions_Created DEFAULT SYSUTCDATETIME(),
        CreatedByUserId uniqueidentifier NULL,
        CONSTRAINT PK_OrganizationServicePermissions PRIMARY KEY (OrganizationServicePermissionId),
        CONSTRAINT FK_OrgServicePermissions_Unit FOREIGN KEY (OrganizationUnitId) REFERENCES org.OrganizationUnits(OrganizationUnitId),
        CONSTRAINT FK_OrgServicePermissions_Service FOREIGN KEY (ServiceId) REFERENCES catalog.Services(ServiceId),
        CONSTRAINT FK_OrgServicePermissions_User FOREIGN KEY (CreatedByUserId) REFERENCES auth.Users(UserId),
        CONSTRAINT CK_OrgServicePermissions_Effect CHECK (Effect IN ('ALLOW','DENY')),
        CONSTRAINT CK_OrgServicePermissions_Validity CHECK (EffectiveToUtc IS NULL OR EffectiveToUtc > EffectiveFromUtc)
    );
    CREATE UNIQUE INDEX UX_OrgServicePermissions_Current
        ON catalog.OrganizationServicePermissions(OrganizationUnitId, ServiceId)
        WHERE EffectiveToUtc IS NULL;
    CREATE INDEX IX_OrgServicePermissions_Service ON catalog.OrganizationServicePermissions(ServiceId, OrganizationUnitId);
END;
GO

CREATE OR ALTER FUNCTION catalog.fn_EffectiveServices(@OrganizationUnitId uniqueidentifier)
RETURNS TABLE
AS
RETURN
(
    WITH Ancestors AS
    (
        SELECT OrganizationUnitId, ParentOrganizationUnitId, CAST(0 AS int) AS Depth
        FROM org.OrganizationUnits
        WHERE OrganizationUnitId = @OrganizationUnitId
        UNION ALL
        SELECT p.OrganizationUnitId, p.ParentOrganizationUnitId, a.Depth + 1
        FROM org.OrganizationUnits p
        INNER JOIN Ancestors a ON a.ParentOrganizationUnitId = p.OrganizationUnitId
        WHERE a.Depth < 4
    ), Decisions AS
    (
        SELECT s.ServiceId,
               MAX(CASE WHEN p.Effect = 'DENY' THEN 1 ELSE 0 END) AS HasDeny,
               MAX(CASE WHEN p.Effect = 'ALLOW' THEN 1 ELSE 0 END) AS HasAllow,
               MIN(CASE WHEN p.Effect IS NOT NULL THEN a.Depth END) AS NearestDecisionDepth
        FROM catalog.Services s
        CROSS JOIN Ancestors a
        LEFT JOIN catalog.OrganizationServicePermissions p
          ON p.OrganizationUnitId = a.OrganizationUnitId
         AND p.ServiceId = s.ServiceId
         AND p.EffectiveFromUtc <= SYSUTCDATETIME()
         AND (p.EffectiveToUtc IS NULL OR p.EffectiveToUtc > SYSUTCDATETIME())
        GROUP BY s.ServiceId
    )
    SELECT s.ServiceId, s.Code, s.Name, s.ServiceCategoryId,
           CAST(CASE WHEN s.IsActive = 1 AND d.HasDeny = 0 AND d.HasAllow = 1 THEN 1 ELSE 0 END AS bit) AS IsAllowed,
           CASE WHEN s.IsActive = 0 THEN 'SERVICE_INACTIVE'
                WHEN d.HasDeny = 1 THEN 'ANCESTOR_OR_LOCAL_DENY'
                WHEN d.HasAllow = 1 THEN 'INHERITED_OR_LOCAL_ALLOW'
                ELSE 'NO_GRANT' END AS DecisionReason
    FROM catalog.Services s
    INNER JOIN Decisions d ON d.ServiceId = s.ServiceId
);
GO

IF OBJECT_ID(N'catalog.PricingRules', N'U') IS NULL
BEGIN
    CREATE TABLE catalog.PricingRules
    (
        PricingRuleId uniqueidentifier NOT NULL CONSTRAINT DF_PricingRules_Id DEFAULT NEWSEQUENTIALID(),
        ServiceId uniqueidentifier NOT NULL,
        RoleId uniqueidentifier NULL,
        RuleKind varchar(20) NOT NULL,
        CalculationType varchar(20) NOT NULL,
        AmountFrom decimal(19,4) NOT NULL,
        AmountTo decimal(19,4) NOT NULL,
        Rate decimal(19,6) NOT NULL,
        TdsRate decimal(9,6) NOT NULL CONSTRAINT DF_PricingRules_Tds DEFAULT 0,
        GstRate decimal(9,6) NOT NULL CONSTRAINT DF_PricingRules_Gst DEFAULT 0,
        EffectiveFromUtc datetime2(3) NOT NULL,
        EffectiveToUtc datetime2(3) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_PricingRules_Active DEFAULT 1,
        CONSTRAINT PK_PricingRules PRIMARY KEY (PricingRuleId),
        CONSTRAINT FK_PricingRules_Service FOREIGN KEY (ServiceId) REFERENCES catalog.Services(ServiceId),
        CONSTRAINT FK_PricingRules_Role FOREIGN KEY (RoleId) REFERENCES auth.Roles(RoleId),
        CONSTRAINT CK_PricingRules_Kind CHECK (RuleKind IN ('CHARGE','COMMISSION')),
        CONSTRAINT CK_PricingRules_Calc CHECK (CalculationType IN ('FIXED','PERCENTAGE')),
        CONSTRAINT CK_PricingRules_Amounts CHECK (AmountFrom >= 0 AND AmountTo >= AmountFrom AND Rate >= 0),
        CONSTRAINT CK_PricingRules_Rates CHECK (TdsRate BETWEEN 0 AND 100 AND GstRate BETWEEN 0 AND 100),
        CONSTRAINT CK_PricingRules_Validity CHECK (EffectiveToUtc IS NULL OR EffectiveToUtc > EffectiveFromUtc)
    );
    CREATE INDEX IX_PricingRules_Lookup
        ON catalog.PricingRules(ServiceId, RoleId, RuleKind, IsActive, AmountFrom, AmountTo, EffectiveFromUtc);
END;
GO

CREATE OR ALTER TRIGGER catalog.TR_PricingRules_PreventOverlap
ON catalog.PricingRules
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        INNER JOIN catalog.PricingRules r
          ON r.PricingRuleId <> i.PricingRuleId
         AND r.ServiceId = i.ServiceId
         AND ((r.RoleId = i.RoleId) OR (r.RoleId IS NULL AND i.RoleId IS NULL))
         AND r.RuleKind = i.RuleKind
         AND r.IsActive = 1 AND i.IsActive = 1
         AND r.AmountFrom <= i.AmountTo AND i.AmountFrom <= r.AmountTo
         AND r.EffectiveFromUtc < ISNULL(i.EffectiveToUtc, '9999-12-31')
         AND i.EffectiveFromUtc < ISNULL(r.EffectiveToUtc, '9999-12-31')
    )
    BEGIN
        THROW 51002, 'Pricing rule amount/effective range overlaps an active rule.', 1;
    END;
END;
GO

IF OBJECT_ID(N'finance.ServiceTransactions', N'U') IS NULL
BEGIN
    CREATE TABLE finance.ServiceTransactions
    (
        ServiceTransactionId uniqueidentifier NOT NULL CONSTRAINT DF_ServiceTransactions_Id DEFAULT NEWSEQUENTIALID(),
        OrganizationUnitId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        ServiceId uniqueidentifier NOT NULL,
        ProviderId uniqueidentifier NULL,
        TransactionReference nvarchar(80) NOT NULL,
        ClientIdempotencyKey nvarchar(100) NOT NULL,
        ProviderReference nvarchar(150) NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_ServiceTransactions_Status DEFAULT 'CREATED',
        ProviderStatus nvarchar(100) NULL,
        Amount decimal(19,4) NOT NULL,
        ChargeAmount decimal(19,4) NOT NULL CONSTRAINT DF_ServiceTransactions_Charge DEFAULT 0,
        CommissionAmount decimal(19,4) NOT NULL CONSTRAINT DF_ServiceTransactions_Commission DEFAULT 0,
        Currency char(3) NOT NULL CONSTRAINT DF_ServiceTransactions_Currency DEFAULT 'INR',
        RequestSummaryJson nvarchar(max) NULL,
        FailureCode nvarchar(80) NULL,
        FailureMessage nvarchar(500) NULL,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_ServiceTransactions_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_ServiceTransactions_Updated DEFAULT SYSUTCDATETIME(),
        CompletedAtUtc datetime2(3) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_ServiceTransactions PRIMARY KEY (ServiceTransactionId),
        CONSTRAINT FK_ServiceTransactions_Unit FOREIGN KEY (OrganizationUnitId) REFERENCES org.OrganizationUnits(OrganizationUnitId),
        CONSTRAINT FK_ServiceTransactions_User FOREIGN KEY (UserId) REFERENCES auth.Users(UserId),
        CONSTRAINT FK_ServiceTransactions_Service FOREIGN KEY (ServiceId) REFERENCES catalog.Services(ServiceId),
        CONSTRAINT FK_ServiceTransactions_Provider FOREIGN KEY (ProviderId) REFERENCES catalog.Providers(ProviderId),
        CONSTRAINT UQ_ServiceTransactions_Reference UNIQUE (TransactionReference),
        CONSTRAINT UQ_ServiceTransactions_Idempotency UNIQUE (OrganizationUnitId, ClientIdempotencyKey),
        CONSTRAINT CK_ServiceTransactions_Status CHECK (Status IN ('CREATED','PENDING','SUCCEEDED','FAILED','REVERSED','CANCELLED')),
        CONSTRAINT CK_ServiceTransactions_Amount CHECK (Amount >= 0 AND ChargeAmount >= 0 AND CommissionAmount >= 0),
        CONSTRAINT CK_ServiceTransactions_RequestJson CHECK (RequestSummaryJson IS NULL OR ISJSON(RequestSummaryJson) = 1)
    );
    CREATE INDEX IX_ServiceTransactions_OrgCreated ON finance.ServiceTransactions(OrganizationUnitId, CreatedAtUtc DESC);
    CREATE INDEX IX_ServiceTransactions_UserCreated ON finance.ServiceTransactions(UserId, CreatedAtUtc DESC);
    CREATE INDEX IX_ServiceTransactions_ProviderRef ON finance.ServiceTransactions(ProviderId, ProviderReference) WHERE ProviderReference IS NOT NULL;
END;
GO

IF OBJECT_ID(N'finance.LedgerAccounts', N'U') IS NULL
BEGIN
    CREATE TABLE finance.LedgerAccounts
    (
        LedgerAccountId uniqueidentifier NOT NULL CONSTRAINT DF_LedgerAccounts_Id DEFAULT NEWSEQUENTIALID(),
        OrganizationUnitId uniqueidentifier NULL,
        UserId uniqueidentifier NULL,
        Code nvarchar(80) NOT NULL,
        Name nvarchar(150) NOT NULL,
        AccountType varchar(20) NOT NULL,
        NormalBalance char(1) NOT NULL,
        Currency char(3) NOT NULL CONSTRAINT DF_LedgerAccounts_Currency DEFAULT 'INR',
        CurrentBalance decimal(19,4) NOT NULL CONSTRAINT DF_LedgerAccounts_Balance DEFAULT 0,
        IsActive bit NOT NULL CONSTRAINT DF_LedgerAccounts_Active DEFAULT 1,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_LedgerAccounts_Created DEFAULT SYSUTCDATETIME(),
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_LedgerAccounts PRIMARY KEY (LedgerAccountId),
        CONSTRAINT FK_LedgerAccounts_Unit FOREIGN KEY (OrganizationUnitId) REFERENCES org.OrganizationUnits(OrganizationUnitId),
        CONSTRAINT FK_LedgerAccounts_User FOREIGN KEY (UserId) REFERENCES auth.Users(UserId),
        CONSTRAINT UQ_LedgerAccounts_Code UNIQUE (Code),
        CONSTRAINT CK_LedgerAccounts_Type CHECK (AccountType IN ('ASSET','LIABILITY','REVENUE','EXPENSE','CLEARING')),
        CONSTRAINT CK_LedgerAccounts_Normal CHECK (NormalBalance IN ('D','C'))
    );
    CREATE INDEX IX_LedgerAccounts_User ON finance.LedgerAccounts(UserId, IsActive) WHERE UserId IS NOT NULL;
    CREATE INDEX IX_LedgerAccounts_Unit ON finance.LedgerAccounts(OrganizationUnitId, IsActive) WHERE OrganizationUnitId IS NOT NULL;
END;
GO

IF OBJECT_ID(N'finance.JournalTransactions', N'U') IS NULL
BEGIN
    CREATE TABLE finance.JournalTransactions
    (
        JournalTransactionId uniqueidentifier NOT NULL,
        ServiceTransactionId uniqueidentifier NULL,
        IdempotencyKey nvarchar(120) NOT NULL,
        Reference nvarchar(100) NOT NULL,
        Description nvarchar(500) NOT NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_JournalTransactions_Status DEFAULT 'DRAFT',
        ReversalOfJournalId uniqueidentifier NULL,
        CreatedByUserId uniqueidentifier NULL,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_JournalTransactions_Created DEFAULT SYSUTCDATETIME(),
        PostedAtUtc datetime2(3) NULL,
        CONSTRAINT PK_JournalTransactions PRIMARY KEY (JournalTransactionId),
        CONSTRAINT FK_JournalTransactions_ServiceTxn FOREIGN KEY (ServiceTransactionId) REFERENCES finance.ServiceTransactions(ServiceTransactionId),
        CONSTRAINT FK_JournalTransactions_Reversal FOREIGN KEY (ReversalOfJournalId) REFERENCES finance.JournalTransactions(JournalTransactionId),
        CONSTRAINT FK_JournalTransactions_User FOREIGN KEY (CreatedByUserId) REFERENCES auth.Users(UserId),
        CONSTRAINT UQ_JournalTransactions_Idempotency UNIQUE (IdempotencyKey),
        CONSTRAINT UQ_JournalTransactions_Reference UNIQUE (Reference),
        CONSTRAINT UQ_JournalTransactions_Reversal UNIQUE (ReversalOfJournalId),
        CONSTRAINT CK_JournalTransactions_Status CHECK (Status IN ('DRAFT','POSTED','VOID'))
    );
END;
GO

IF OBJECT_ID(N'finance.JournalEntries', N'U') IS NULL
BEGIN
    CREATE TABLE finance.JournalEntries
    (
        JournalEntryId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_JournalEntries PRIMARY KEY,
        JournalTransactionId uniqueidentifier NOT NULL,
        LedgerAccountId uniqueidentifier NOT NULL,
        DebitAmount decimal(19,4) NOT NULL CONSTRAINT DF_JournalEntries_Debit DEFAULT 0,
        CreditAmount decimal(19,4) NOT NULL CONSTRAINT DF_JournalEntries_Credit DEFAULT 0,
        Memo nvarchar(300) NULL,
        CONSTRAINT FK_JournalEntries_Journal FOREIGN KEY (JournalTransactionId) REFERENCES finance.JournalTransactions(JournalTransactionId),
        CONSTRAINT FK_JournalEntries_Account FOREIGN KEY (LedgerAccountId) REFERENCES finance.LedgerAccounts(LedgerAccountId),
        CONSTRAINT CK_JournalEntries_OneSide CHECK
        (
            (DebitAmount > 0 AND CreditAmount = 0) OR
            (CreditAmount > 0 AND DebitAmount = 0)
        )
    );
    CREATE INDEX IX_JournalEntries_Journal ON finance.JournalEntries(JournalTransactionId);
    CREATE INDEX IX_JournalEntries_Account ON finance.JournalEntries(LedgerAccountId, JournalEntryId DESC);
END;
GO

IF TYPE_ID(N'finance.JournalEntryInput') IS NULL
    EXEC(N'CREATE TYPE finance.JournalEntryInput AS TABLE
    (
        LedgerAccountId uniqueidentifier NOT NULL,
        DebitAmount decimal(19,4) NOT NULL,
        CreditAmount decimal(19,4) NOT NULL,
        Memo nvarchar(300) NULL
    )');
GO

CREATE OR ALTER PROCEDURE finance.PostJournal
    @JournalTransactionId uniqueidentifier,
    @ServiceTransactionId uniqueidentifier = NULL,
    @IdempotencyKey nvarchar(120),
    @Reference nvarchar(100),
    @Description nvarchar(500),
    @CreatedByUserId uniqueidentifier = NULL,
    @ReversalOfJournalId uniqueidentifier = NULL,
    @Entries finance.JournalEntryInput READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM @Entries) THROW 51010, 'A journal requires entries.', 1;
    IF EXISTS (SELECT 1 FROM @Entries WHERE NOT ((DebitAmount > 0 AND CreditAmount = 0) OR (CreditAmount > 0 AND DebitAmount = 0)))
        THROW 51011, 'Every journal entry must contain exactly one positive debit or credit.', 1;
    IF (SELECT SUM(DebitAmount) FROM @Entries) <> (SELECT SUM(CreditAmount) FROM @Entries)
        THROW 51012, 'Journal is not balanced.', 1;

    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM finance.JournalTransactions WITH (UPDLOCK, HOLDLOCK) WHERE IdempotencyKey = @IdempotencyKey)
    BEGIN
        COMMIT TRANSACTION;
        RETURN;
    END;

    IF EXISTS
    (
        SELECT 1 FROM @Entries e
        LEFT JOIN finance.LedgerAccounts a WITH (UPDLOCK, HOLDLOCK) ON a.LedgerAccountId = e.LedgerAccountId
        WHERE a.LedgerAccountId IS NULL OR a.IsActive = 0
    )
        THROW 51013, 'Journal references a missing or inactive ledger account.', 1;

    INSERT finance.JournalTransactions
    (
        JournalTransactionId, ServiceTransactionId, IdempotencyKey, Reference, Description,
        Status, ReversalOfJournalId, CreatedByUserId, PostedAtUtc
    )
    VALUES
    (
        @JournalTransactionId, @ServiceTransactionId, @IdempotencyKey, @Reference, @Description,
        'POSTED', @ReversalOfJournalId, @CreatedByUserId, SYSUTCDATETIME()
    );

    INSERT finance.JournalEntries(JournalTransactionId, LedgerAccountId, DebitAmount, CreditAmount, Memo)
        SELECT @JournalTransactionId, LedgerAccountId, DebitAmount, CreditAmount, Memo FROM @Entries;

    UPDATE a
       SET CurrentBalance = CurrentBalance +
           CASE WHEN a.NormalBalance = 'D' THEN e.DebitAmount - e.CreditAmount
                ELSE e.CreditAmount - e.DebitAmount END
    FROM finance.LedgerAccounts a
    INNER JOIN
    (
        SELECT LedgerAccountId, SUM(DebitAmount) DebitAmount, SUM(CreditAmount) CreditAmount
        FROM @Entries GROUP BY LedgerAccountId
    ) e ON e.LedgerAccountId = a.LedgerAccountId;

    COMMIT TRANSACTION;
END;
GO

IF OBJECT_ID(N'finance.FundRequests', N'U') IS NULL
BEGIN
    CREATE TABLE finance.FundRequests
    (
        FundRequestId uniqueidentifier NOT NULL CONSTRAINT DF_FundRequests_Id DEFAULT NEWSEQUENTIALID(),
        OrganizationUnitId uniqueidentifier NOT NULL,
        RequestedByUserId uniqueidentifier NOT NULL,
        Amount decimal(19,4) NOT NULL,
        PaymentMode varchar(20) NOT NULL,
        ExternalReference nvarchar(150) NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_FundRequests_Status DEFAULT 'PENDING',
        ReviewedByUserId uniqueidentifier NULL,
        ReviewedAtUtc datetime2(3) NULL,
        ReviewReason nvarchar(500) NULL,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_FundRequests_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_FundRequests PRIMARY KEY (FundRequestId),
        CONSTRAINT FK_FundRequests_Unit FOREIGN KEY (OrganizationUnitId) REFERENCES org.OrganizationUnits(OrganizationUnitId),
        CONSTRAINT FK_FundRequests_Requester FOREIGN KEY (RequestedByUserId) REFERENCES auth.Users(UserId),
        CONSTRAINT FK_FundRequests_Reviewer FOREIGN KEY (ReviewedByUserId) REFERENCES auth.Users(UserId),
        CONSTRAINT CK_FundRequests_Amount CHECK (Amount > 0),
        CONSTRAINT CK_FundRequests_Mode CHECK (PaymentMode IN ('MANUAL','UPI','BANK_TRANSFER','GATEWAY')),
        CONSTRAINT CK_FundRequests_Status CHECK (Status IN ('PENDING','APPROVED','REJECTED','CANCELLED'))
    );
    CREATE INDEX IX_FundRequests_OrgStatus ON finance.FundRequests(OrganizationUnitId, Status, CreatedAtUtc DESC);
END;
GO

IF OBJECT_ID(N'finance.Receipts', N'U') IS NULL
BEGIN
    CREATE TABLE finance.Receipts
    (
        ReceiptId uniqueidentifier NOT NULL CONSTRAINT DF_Receipts_Id DEFAULT NEWSEQUENTIALID(),
        ReceiptNumber nvarchar(50) NOT NULL,
        ServiceTransactionId uniqueidentifier NULL,
        JournalTransactionId uniqueidentifier NULL,
        IssuedToUserId uniqueidentifier NOT NULL,
        IssuedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_Receipts_Issued DEFAULT SYSUTCDATETIME(),
        SnapshotJson nvarchar(max) NOT NULL,
        CONSTRAINT PK_Receipts PRIMARY KEY (ReceiptId),
        CONSTRAINT UQ_Receipts_Number UNIQUE (ReceiptNumber),
        CONSTRAINT FK_Receipts_ServiceTxn FOREIGN KEY (ServiceTransactionId) REFERENCES finance.ServiceTransactions(ServiceTransactionId),
        CONSTRAINT FK_Receipts_Journal FOREIGN KEY (JournalTransactionId) REFERENCES finance.JournalTransactions(JournalTransactionId),
        CONSTRAINT FK_Receipts_User FOREIGN KEY (IssuedToUserId) REFERENCES auth.Users(UserId),
        CONSTRAINT CK_Receipts_SnapshotJson CHECK (ISJSON(SnapshotJson) = 1),
        CONSTRAINT CK_Receipts_Link CHECK (ServiceTransactionId IS NOT NULL OR JournalTransactionId IS NOT NULL)
    );
END;
GO

IF OBJECT_ID(N'integration.ProviderEvents', N'U') IS NULL
BEGIN
    CREATE TABLE integration.ProviderEvents
    (
        ProviderEventId uniqueidentifier NOT NULL CONSTRAINT DF_ProviderEvents_Id DEFAULT NEWSEQUENTIALID(),
        ProviderId uniqueidentifier NOT NULL,
        ExternalEventId nvarchar(180) NOT NULL,
        ServiceTransactionId uniqueidentifier NULL,
        EventType nvarchar(80) NOT NULL,
        PayloadHash char(64) NOT NULL,
        RawStatus nvarchar(100) NULL,
        ProcessingStatus varchar(20) NOT NULL CONSTRAINT DF_ProviderEvents_Status DEFAULT 'RECEIVED',
        FailureMessage nvarchar(500) NULL,
        ReceivedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_ProviderEvents_Received DEFAULT SYSUTCDATETIME(),
        ProcessedAtUtc datetime2(3) NULL,
        CONSTRAINT PK_ProviderEvents PRIMARY KEY (ProviderEventId),
        CONSTRAINT FK_ProviderEvents_Provider FOREIGN KEY (ProviderId) REFERENCES catalog.Providers(ProviderId),
        CONSTRAINT FK_ProviderEvents_ServiceTxn FOREIGN KEY (ServiceTransactionId) REFERENCES finance.ServiceTransactions(ServiceTransactionId),
        CONSTRAINT UQ_ProviderEvents_External UNIQUE (ProviderId, ExternalEventId),
        CONSTRAINT CK_ProviderEvents_Status CHECK (ProcessingStatus IN ('RECEIVED','PROCESSED','IGNORED','FAILED'))
    );
    CREATE INDEX IX_ProviderEvents_Transaction ON integration.ProviderEvents(ServiceTransactionId, ReceivedAtUtc DESC);
END;
GO

IF OBJECT_ID(N'audit.AuditLogs', N'U') IS NULL
BEGIN
    CREATE TABLE audit.AuditLogs
    (
        AuditLogId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY,
        ActorUserId uniqueidentifier NULL,
        OrganizationUnitId uniqueidentifier NULL,
        Action nvarchar(100) NOT NULL,
        EntityType nvarchar(100) NOT NULL,
        EntityId nvarchar(100) NULL,
        CorrelationId uniqueidentifier NOT NULL,
        IpAddress nvarchar(64) NULL,
        DetailsJson nvarchar(max) NULL,
        OccurredAtUtc datetime2(3) NOT NULL CONSTRAINT DF_AuditLogs_Occurred DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_AuditLogs_Actor FOREIGN KEY (ActorUserId) REFERENCES auth.Users(UserId),
        CONSTRAINT FK_AuditLogs_Unit FOREIGN KEY (OrganizationUnitId) REFERENCES org.OrganizationUnits(OrganizationUnitId),
        CONSTRAINT CK_AuditLogs_DetailsJson CHECK (DetailsJson IS NULL OR ISJSON(DetailsJson) = 1)
    );
    CREATE INDEX IX_AuditLogs_ActorOccurred ON audit.AuditLogs(ActorUserId, OccurredAtUtc DESC);
    CREATE INDEX IX_AuditLogs_EntityOccurred ON audit.AuditLogs(EntityType, EntityId, OccurredAtUtc DESC);
END;
GO

MERGE auth.Roles AS target
USING (VALUES
    ('PLATFORM_ADMIN', 'Platform Administrator', 'Full platform administration'),
    ('CMF_ADMIN', 'CMF Administrator', 'Administers one CMF subtree'),
    ('CMF_USER', 'CMF User', 'Operates within one CMF subtree'),
    ('CSF_ADMIN', 'CSF Administrator', 'Administers one CSF subtree'),
    ('CSF_USER', 'CSF User', 'Operates within one CSF subtree'),
    ('CSP_USER', 'CSP User', 'Operates one CSP')
) AS source(Code, Name, Description)
ON target.Code = source.Code
WHEN NOT MATCHED THEN INSERT(Code, Name, Description, IsSystem)
VALUES(source.Code, source.Name, source.Description, 1);
GO

DECLARE @PlatformId uniqueidentifier = (SELECT OrganizationUnitId FROM org.OrganizationUnits WHERE Code = 'PLATFORM');
IF @PlatformId IS NULL
BEGIN
    INSERT org.OrganizationUnits(ParentOrganizationUnitId, UnitType, Code, Name, Status)
    VALUES(NULL, 'PLATFORM', 'PLATFORM', 'ProxyType Platform', 'ACTIVE');
END;
GO

MERGE catalog.ServiceCategories AS target
USING (VALUES
    ('WALLET','Wallet & Funding',10),('RECHARGE','Recharge',20),('BILL','Bill Payments',30),
    ('TRANSFER','Money Transfer',40),('AEPS','AEPS',50),('GOVERNMENT','Government Services',60),
    ('BANKING','Banking & Accounts',70),('COMMERCE','Commerce',80)
) AS source(Code, Name, SortOrder)
ON target.Code = source.Code
WHEN NOT MATCHED THEN INSERT(Code, Name, SortOrder) VALUES(source.Code, source.Name, source.SortOrder);
GO

DECLARE @ServiceSeed TABLE
(
    CategoryCode nvarchar(50), Code nvarchar(60), Name nvarchar(150), Description nvarchar(500),
    Chargeable bit, Commissionable bit, RequiresKyc bit, SortOrder int
);
INSERT @ServiceSeed VALUES
('WALLET','wallet_transfer','Wallet Transfer','Transfer funds between approved wallets',1,0,0,10),
('WALLET','fund_request','Fund Request','Manual or gateway-assisted wallet funding',0,0,0,20),
('RECHARGE','recharge_v1','Recharge V1','Legacy recharge adapter version 1',0,1,1,10),
('RECHARGE','recharge_v2','Recharge V2','Legacy recharge adapter version 2',0,1,1,20),
('RECHARGE','recharge_v3','Recharge V3','Legacy RechargeExchange adapter',0,1,1,30),
('RECHARGE','recharge_v4','Recharge V4','Legacy MRobotics adapter',0,1,1,40),
('RECHARGE','recharge_v5','Recharge V5','Legacy Bluecant adapter',0,1,1,50),
('BILL','bbps','BBPS','Bharat Bill Payment System',0,1,1,10),
('BILL','bbps_v2','BBPS V2','Legacy BBPS provider variant 2',0,1,1,20),
('BILL','bbps_v3','BBPS V3','Legacy dynamic-provider BBPS',0,1,1,30),
('BILL','lpg_booking','LPG Booking','LPG bill fetch and payment',0,1,1,40),
('BILL','lic_payment','LIC Payment','LIC policy bill payment',0,1,1,50),
('TRANSFER','dmt','Domestic Money Transfer','DMT beneficiary and transfer workflow',1,0,1,10),
('TRANSFER','dmt_v2','Domestic Money Transfer V2','DMT KYC/beneficiary workflow',1,0,1,20),
('TRANSFER','payout','Payout','Bank payout workflow',1,0,1,30),
('TRANSFER','payout_v2','Payout V2','Merchant-bank payout workflow',1,0,1,40),
('AEPS','aeps','AEPS','AEPS onboarding and transactions',0,1,1,10),
('AEPS','aeps_v2','AEPS V2','Multi-bank AEPS workflow',0,1,1,20),
('AEPS','aeps_union_bank','AEPS Union Bank','Union Bank AEPS workflow',0,1,1,30),
('GOVERNMENT','pan_card','PAN Card','NSDL PAN application workflow',1,0,1,10),
('BANKING','open_account','Account Opening','BC agent and account-opening workflow',0,0,1,10),
('COMMERCE','prime_plan','Prime Plan','Membership plan purchase',0,0,0,10),
('COMMERCE','product','Product','Product purchase',0,0,0,20),
('COMMERCE','credit_card_external','Credit Card (External)','Catalog-only legacy external link; no live integration',0,0,1,30);

MERGE catalog.Services AS target
USING
(
    SELECT c.ServiceCategoryId, s.Code, s.Name, s.Description, s.Chargeable, s.Commissionable, s.RequiresKyc, s.SortOrder
    FROM @ServiceSeed s INNER JOIN catalog.ServiceCategories c ON c.Code = s.CategoryCode
) AS source
ON target.Code = source.Code
WHEN NOT MATCHED THEN
    INSERT(ServiceCategoryId, Code, Name, Description, IsActive, IsChargeable, IsCommissionable, RequiresKyc, SortOrder)
    VALUES(source.ServiceCategoryId, source.Code, source.Name, source.Description, 1, source.Chargeable, source.Commissionable, source.RequiresKyc, source.SortOrder);
GO

DECLARE @Platform uniqueidentifier = (SELECT OrganizationUnitId FROM org.OrganizationUnits WHERE Code = 'PLATFORM');
INSERT catalog.OrganizationServicePermissions(OrganizationUnitId, ServiceId, Effect, Reason)
SELECT @Platform, s.ServiceId, 'ALLOW', 'Initial platform catalog grant; provider routing remains disabled.'
FROM catalog.Services s
WHERE NOT EXISTS
(
    SELECT 1 FROM catalog.OrganizationServicePermissions p
    WHERE p.OrganizationUnitId = @Platform AND p.ServiceId = s.ServiceId AND p.EffectiveToUtc IS NULL
);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNo = 1)
    INSERT dbo.SchemaVersions(VersionNo, Description) VALUES(1, N'Initial identity, CMF/CSF/CSP, service catalog, permissions, finance, integration, audit and master schema');
GO
