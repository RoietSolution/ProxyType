/* CRM identity import metadata. This preserves source classification without granting hierarchy access. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
IF OBJECT_ID(N'auth.LegacyUserImportMap', N'U') IS NULL
BEGIN
    CREATE TABLE auth.LegacyUserImportMap
    (
        LegacyUserImportMapId uniqueidentifier NOT NULL CONSTRAINT DF_LegacyUserImportMap_Id DEFAULT NEWSEQUENTIALID(),
        UserId uniqueidentifier NOT NULL,
        LegacySystem nvarchar(30) NOT NULL,
        LegacyUserId int NOT NULL,
        LegacyUserType int NULL,
        LegacyGroup nvarchar(30) NULL,
        LegacyRegionId int NULL,
        LegacyStateIds nvarchar(255) NULL,
        ImportedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_LegacyUserImportMap_Imported DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_LegacyUserImportMap PRIMARY KEY (LegacyUserImportMapId),
        CONSTRAINT FK_LegacyUserImportMap_User FOREIGN KEY (UserId) REFERENCES auth.Users(UserId) ON DELETE CASCADE,
        CONSTRAINT UQ_LegacyUserImportMap_Source UNIQUE (LegacySystem, LegacyUserId),
        CONSTRAINT UQ_LegacyUserImportMap_User UNIQUE (UserId)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNo = 6)
    INSERT dbo.SchemaVersions(VersionNo, Description) VALUES(6, N'CRM DWUSER import metadata and safe password-reset onboarding state');
