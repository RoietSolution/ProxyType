/* Rollback-only smoke verification for migration 13. */
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

IF (SELECT COUNT(*) FROM finance.RechargeCommissionRules WHERE IsActive = 1) <> 48
    THROW 51130, 'Expected 48 active role/operator recharge commission rules.', 1;
IF NOT EXISTS
(
    SELECT 1 FROM finance.RechargeCommissionRules r
    JOIN finance.RechargeOperators o ON o.RechargeOperatorId = r.RechargeOperatorId
    JOIN auth.Roles ar ON ar.RoleId = r.RoleId
    WHERE o.Label = 'airtel' AND o.Type = 'MOBILE' AND ar.Code = 'CSP_USER'
      AND r.CalculationType = 'PERCENTAGE' AND r.Rate = 1.5 AND r.IsActive = 1
)
    THROW 51131, 'CSP Airtel recharge commission was not migrated correctly.', 1;
IF (SELECT COUNT(*) FROM catalog.PricingRules p JOIN catalog.Services s ON s.ServiceId = p.ServiceId
    WHERE s.Code = 'wallet_transfer' AND p.RuleKind = 'CHARGE' AND p.IsActive = 1) <> 4
    THROW 51132, 'Expected four active legacy wallet-transfer charge slabs.', 1;

BEGIN TRANSACTION;
DECLARE @Sender uniqueidentifier, @Receiver uniqueidentifier, @Unit uniqueidentifier;
SELECT TOP 1 @Sender = a.UserId, @Unit = m.OrganizationUnitId
FROM finance.LedgerAccounts a JOIN org.OrganizationMemberships m ON m.UserId = a.UserId AND m.IsActive = 1
WHERE a.UserId IS NOT NULL AND a.Code = CONCAT('USER:', CONVERT(varchar(36), a.UserId), ':WALLET');
SELECT TOP 1 @Receiver = a.UserId FROM finance.LedgerAccounts a
WHERE a.UserId IS NOT NULL AND a.UserId <> @Sender AND a.Code = CONCAT('USER:', CONVERT(varchar(36), a.UserId), ':WALLET');
IF @Sender IS NULL OR @Receiver IS NULL THROW 51133, 'Two initialized wallets are required for the smoke test.', 1;

DECLARE @Service uniqueidentifier = (SELECT ServiceId FROM catalog.Services WHERE Code = 'wallet_transfer');
DECLARE @Transaction uniqueidentifier = NEWID(), @Journal uniqueidentifier = NEWID();
DECLARE @Token varchar(36) = CONVERT(varchar(36), NEWID());
DECLARE @Reference nvarchar(80) = CONCAT('VERIFY-', @Token);
UPDATE finance.LedgerAccounts SET CurrentBalance = CurrentBalance + 100 WHERE UserId = @Sender AND Code = CONCAT('USER:', CONVERT(varchar(36), @Sender), ':WALLET');
DECLARE @SenderBefore decimal(19,4) = (SELECT CurrentBalance FROM finance.LedgerAccounts WHERE UserId = @Sender AND Code = CONCAT('USER:', CONVERT(varchar(36), @Sender), ':WALLET'));
DECLARE @ReceiverBefore decimal(19,4) = (SELECT CurrentBalance FROM finance.LedgerAccounts WHERE UserId = @Receiver AND Code = CONCAT('USER:', CONVERT(varchar(36), @Receiver), ':WALLET'));

INSERT finance.ServiceTransactions(ServiceTransactionId, OrganizationUnitId, UserId, ServiceId, CounterpartyUserId,
    TransactionReference, ClientIdempotencyKey, Status, Amount, ChargeAmount, CommissionAmount, TaxAmount,
    DebitAmount, CreditAmount, Currency, RequestAtUtc, CreatedAtUtc, UpdatedAtUtc)
VALUES(@Transaction, @Unit, @Sender, @Service, @Receiver, @Reference, CONCAT('VERIFY-', @Token), 'CREATED',
    10, 1, 0, 0, 11, 10, 'INR', SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME());
EXEC finance.PostWalletToWalletTransfer @Journal, @Transaction, @Reference, @Reference, @Sender, @Receiver, 10, 1;

IF (SELECT CurrentBalance FROM finance.LedgerAccounts WHERE UserId = @Sender AND Code = CONCAT('USER:', CONVERT(varchar(36), @Sender), ':WALLET')) <> @SenderBefore - 11
    THROW 51134, 'Sender was not debited by principal plus charge.', 1;
IF (SELECT CurrentBalance FROM finance.LedgerAccounts WHERE UserId = @Receiver AND Code = CONCAT('USER:', CONVERT(varchar(36), @Receiver), ':WALLET')) <> @ReceiverBefore + 10
    THROW 51135, 'Receiver was not credited by the principal.', 1;
IF (SELECT SUM(DebitAmount) - SUM(CreditAmount) FROM finance.JournalEntries WHERE JournalTransactionId = @Journal) <> 0
    THROW 51136, 'The charged wallet-transfer journal is not balanced.', 1;
ROLLBACK TRANSACTION;

SELECT 'PASS' Result, 4 ChargeSlabs, 48 RechargeCommissionRules;
GO
