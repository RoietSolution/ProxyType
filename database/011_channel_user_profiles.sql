/* CSP, CSF, and CMF profile/KYC data for ProxyType channel-user CRUD. */
USE [proxytype_DB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'auth.UserProfiles', N'U') IS NULL
BEGIN
    CREATE TABLE auth.UserProfiles
    (
        UserId uniqueidentifier NOT NULL,
        ProfileJson nvarchar(max) NOT NULL CONSTRAINT DF_UserProfiles_Json DEFAULT N'{}',
        AadhaarDocumentContent varbinary(max) NULL,
        AadhaarDocumentContentType varchar(100) NULL,
        AadhaarDocumentFileName nvarchar(255) NULL,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_UserProfiles_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_UserProfiles_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_UserProfiles PRIMARY KEY (UserId),
        CONSTRAINT FK_UserProfiles_User FOREIGN KEY (UserId) REFERENCES auth.Users(UserId),
        CONSTRAINT CK_UserProfiles_Json CHECK (ISJSON(ProfileJson) = 1)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNo = 11)
    INSERT dbo.SchemaVersions(VersionNo, Description)
    VALUES(11, N'CSP, CSF, and CMF channel-user profiles and Aadhaar KYC documents');
GO
