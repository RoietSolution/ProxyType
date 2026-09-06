/* Hierarchy-secured user document upload and download storage. */
USE [proxytype_DB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'auth.UserDocuments', N'U') IS NULL
BEGIN
    CREATE TABLE auth.UserDocuments
    (
        UserDocumentId uniqueidentifier NOT NULL CONSTRAINT DF_UserDocuments_Id DEFAULT NEWSEQUENTIALID(),
        UserId uniqueidentifier NOT NULL,
        DocumentType varchar(30) NOT NULL,
        FileName nvarchar(255) NOT NULL,
        ContentType varchar(100) NOT NULL,
        FileSize bigint NOT NULL,
        Content varbinary(max) NOT NULL,
        UploadedByUserId uniqueidentifier NOT NULL,
        CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_UserDocuments_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_UserDocuments PRIMARY KEY (UserDocumentId),
        CONSTRAINT FK_UserDocuments_User FOREIGN KEY (UserId) REFERENCES auth.Users(UserId),
        CONSTRAINT FK_UserDocuments_Uploader FOREIGN KEY (UploadedByUserId) REFERENCES auth.Users(UserId),
        CONSTRAINT CK_UserDocuments_Type CHECK (DocumentType IN ('APPLICATION_FORM','ADDRESS_PROOF','ID_PROOF','PAN_CARD')),
        CONSTRAINT CK_UserDocuments_Size CHECK (FileSize > 0 AND FileSize <= 1048576),
        CONSTRAINT UQ_UserDocuments_UserFile UNIQUE (UserId, FileName)
    );
    CREATE INDEX IX_UserDocuments_UserCreated ON auth.UserDocuments(UserId, CreatedAtUtc DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNo = 12)
    INSERT dbo.SchemaVersions(VersionNo, Description)
    VALUES(12, N'Hierarchy-secured user document upload and download storage');
GO
