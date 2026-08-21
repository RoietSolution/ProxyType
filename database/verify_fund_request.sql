SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

BEGIN TRANSACTION;
DECLARE @UserId uniqueidentifier = (SELECT TOP 1 UserId FROM auth.Users WHERE Username = 'platform.admin');
DECLARE @UnitId uniqueidentifier = (SELECT OrganizationUnitId FROM org.OrganizationUnits WHERE Code = 'PLATFORM');
DECLARE @ServiceId uniqueidentifier = (SELECT ServiceId FROM catalog.Services WHERE Code = 'fund_request');
DECLARE @Amount decimal(19,4) = 125.00;
DECLARE @Tx uniqueidentifier = NEWID();
DECLARE @Fr uniqueidentifier = NEWID();
DECLARE @Ref nvarchar(80) = CONCAT('SMOKE-FR-', CONVERT(nvarchar(36), NEWID()));
DECLARE @Ext nvarchar(150) = CONCAT('UTR-SMOKE-', CONVERT(nvarchar(36), NEWID()));
DECLARE @Before decimal(19,4);
DECLARE @After decimal(19,4);
DECLARE @JournalCount int;

IF NOT EXISTS (SELECT 1 FROM finance.LedgerAccounts WHERE UserId = @UserId AND Code = CONCAT('USER:', CONVERT(nvarchar(36), @UserId), ':WALLET'))
    INSERT finance.LedgerAccounts(UserId, Code, Name, AccountType, NormalBalance, Currency, IsActive, CreatedAtUtc)
    VALUES(@UserId, CONCAT('USER:', CONVERT(nvarchar(36), @UserId), ':WALLET'), 'User wallet', 'LIABILITY', 'C', 'INR', 1, SYSUTCDATETIME());

SELECT @Before = CurrentBalance FROM finance.LedgerAccounts
WHERE UserId = @UserId AND Code = CONCAT('USER:', CONVERT(nvarchar(36), @UserId), ':WALLET');

INSERT finance.ServiceTransactions
(ServiceTransactionId, OrganizationUnitId, UserId, ServiceId, TransactionReference, ClientIdempotencyKey, Status, Amount, ChargeAmount, CommissionAmount, Currency, RequestSummaryJson, CreatedAtUtc, UpdatedAtUtc, TaxAmount, DebitAmount, CreditAmount, RequestAtUtc)
VALUES(@Tx, @UnitId, @UserId, @ServiceId, @Ref, CONCAT('idem-', CONVERT(nvarchar(36), NEWID())), 'PENDING', @Amount, 0, 0, 'INR', N'{"PaymentMode":"NEFT"}', SYSUTCDATETIME(), SYSUTCDATETIME(), 0, 0, 0, SYSUTCDATETIME());

INSERT finance.FundRequests(FundRequestId, OrganizationUnitId, RequestedByUserId, ServiceTransactionId, Amount, PaymentMode, ExternalReference, Status, CreatedAtUtc, UpdatedAtUtc)
VALUES(@Fr, @UnitId, @UserId, @Tx, @Amount, 'NEFT', @Ext, 'PENDING', SYSUTCDATETIME(), SYSUTCDATETIME());

INSERT finance.Receipts(ReceiptId, ReceiptNumber, ServiceTransactionId, IssuedToUserId, IssuedAtUtc, SnapshotJson)
VALUES(NEWID(), CONCAT('RCP-', @Ref), @Tx, @UserId, SYSUTCDATETIME(), N'{"Status":"PENDING"}');

SELECT fr.Status, st.Status TransactionStatus, st.CreditAmount, @Before WalletBalance
FROM finance.FundRequests fr JOIN finance.ServiceTransactions st ON st.ServiceTransactionId = fr.ServiceTransactionId
WHERE fr.FundRequestId = @Fr;

EXEC finance.ReviewFundRequest @Fr, @UserId, 'APPROVED', N'approved smoke';
SELECT @After = CurrentBalance FROM finance.LedgerAccounts
WHERE UserId = @UserId AND Code = CONCAT('USER:', CONVERT(nvarchar(36), @UserId), ':WALLET');
SELECT @JournalCount = COUNT(*) FROM finance.JournalTransactions WHERE ServiceTransactionId = @Tx;
SELECT fr.Status, st.Status TransactionStatus, st.CreditAmount, @After WalletBalance, @JournalCount JournalCount
FROM finance.FundRequests fr JOIN finance.ServiceTransactions st ON st.ServiceTransactionId = fr.ServiceTransactionId
WHERE fr.FundRequestId = @Fr;

EXEC finance.ReviewFundRequest @Fr, @UserId, 'APPROVED', N'duplicate smoke';
SELECT @JournalCount = COUNT(*) FROM finance.JournalTransactions WHERE ServiceTransactionId = @Tx;
SELECT fr.Status, st.CreditAmount, @JournalCount JournalCount
FROM finance.FundRequests fr JOIN finance.ServiceTransactions st ON st.ServiceTransactionId = fr.ServiceTransactionId
WHERE fr.FundRequestId = @Fr;

DECLARE @Tx2 uniqueidentifier = NEWID();
DECLARE @Fr2 uniqueidentifier = NEWID();
INSERT finance.ServiceTransactions
(ServiceTransactionId, OrganizationUnitId, UserId, ServiceId, TransactionReference, ClientIdempotencyKey, Status, Amount, ChargeAmount, CommissionAmount, Currency, RequestSummaryJson, CreatedAtUtc, UpdatedAtUtc, TaxAmount, DebitAmount, CreditAmount, RequestAtUtc)
VALUES(@Tx2, @UnitId, @UserId, @ServiceId, CONCAT('SMOKE-FR-', CONVERT(nvarchar(36), NEWID())), CONCAT('idem-', CONVERT(nvarchar(36), NEWID())), 'PENDING', 50, 0, 0, 'INR', N'{"PaymentMode":"NEFT"}', SYSUTCDATETIME(), SYSUTCDATETIME(), 0, 0, 0, SYSUTCDATETIME());
INSERT finance.FundRequests(FundRequestId, OrganizationUnitId, RequestedByUserId, ServiceTransactionId, Amount, PaymentMode, ExternalReference, Status, CreatedAtUtc, UpdatedAtUtc)
VALUES(@Fr2, @UnitId, @UserId, @Tx2, 50, 'NEFT', CONCAT('UTR-SMOKE-', CONVERT(nvarchar(36), NEWID())), 'PENDING', SYSUTCDATETIME(), SYSUTCDATETIME());
INSERT finance.Receipts(ReceiptId, ReceiptNumber, ServiceTransactionId, IssuedToUserId, IssuedAtUtc, SnapshotJson)
VALUES(NEWID(), CONCAT('RCP-', CONVERT(nvarchar(36), @Tx2)), @Tx2, @UserId, SYSUTCDATETIME(), N'{"Status":"PENDING"}');
EXEC finance.ReviewFundRequest @Fr2, @UserId, 'REJECTED', N'rejected smoke';
SELECT fr.Status, st.Status TransactionStatus, st.CreditAmount
FROM finance.FundRequests fr JOIN finance.ServiceTransactions st ON st.ServiceTransactionId = fr.ServiceTransactionId
WHERE fr.FundRequestId = @Fr2;

ROLLBACK TRANSACTION;
