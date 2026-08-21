# Fino DMT

## Legacy evidence and boundary

The legacy sources verify the shared Domestic Money Transfer workflow, not a Fino-DMT provider workflow. Shared Laravel references are:

- Web: `POST /dmt/transaction` → `DmtController::transaction`.
- API: `POST dmt-transaction`, `POST dmt-transaction-status`, and `GET dmt-transaction-history`.
- View: `resources/views/dmt/index.blade.php`.
- Tables: `dmt_txns`, `dmt_registrations`, `dmt_beneficiaries`, `aeps_wallets`, `wallets`, and `receipts`.

Fino-specific routes found in the same source are AEPS registration/authentication/cash-withdrawal routes. No Fino-DMT provider URL, credential contract, response schema, or charge/commission rule was verified. Live implementation is therefore blocked and the replacement uses a safe mock provider. Fino DMT, Airtel DMT, and DMT PPI remain provider-specific variants over shared DMT components; no separate generic service is created.

## New business flow

1. Authenticate the user.
2. Resolve the user’s active primary organization.
3. Require the `fino_dmt` service to be active and effectively allowed through the organization hierarchy.
4. Validate the bank account, IFSC, beneficiary, mobile, transfer mode, amount, comments, and idempotency key server-side.
5. Create one `finance.ServiceTransactions` envelope with a masked request summary.
6. Call the selected provider adapter. Default mode is `MOCK`; LIVE throws a disabled-provider error and never falls back to MOCK.
7. Normalize the provider result to `SUCCEEDED`, `FAILED`, or `PENDING`.
8. On a successful result, post the wallet debit through `finance.PostWalletDebit`, which locks the wallet account, checks balance, creates balanced journal entries, and prevents duplicate posting by idempotency key.
9. Issue a receipt snapshot and return the transaction reference/result.

Charges and commission are currently zero because the legacy evidence does not verify a Fino-DMT pricing rule. They must not be enabled until confirmed.

## Inputs and validation

| Field | Rule |
|---|---|
| Account number | 9–18 digits |
| IFSC | `^[A-Z]{4}0[A-Z0-9]{6}$` |
| Beneficiary name | Required, maximum 255 characters |
| Mobile | Exactly 10 digits |
| Amount | INR 1,000–10,000 |
| Transfer mode | `IMPS`, `NEFT`, or `RTGS` |
| Comments | Required, maximum 1,000 characters |
| Idempotency key | Required, 8–100 characters |

## API

`POST /api/services/fino-dmt/transfers` (authenticated)

The response includes the internal transaction ID/reference, normalized status, provider mode, provider reference when present, amount, charges, commission, failure reason, and receipt number. Provider secrets and unmasked account/mobile values are never returned.

Reusable history is available at `GET /api/transactions` with service, status, date, search, and pagination filters. Results are restricted to the caller’s hierarchy scope.

## Database objects

- `finance.ServiceTransactions`: common transaction envelope and idempotency constraint.
- `finance.LedgerAccounts`: per-user wallet and Fino DMT clearing accounts.
- `finance.JournalTransactions` / `finance.JournalEntries`: immutable balanced wallet postings.
- `finance.Receipts`: receipt snapshots.
- `catalog.Services`, `catalog.Providers`, and `catalog.ServiceProviders`: service/provider routing.
- `database/002_fino_dmt.sql`: idempotent schema additions, mock route, ledger seed, and wallet debit procedure.

## Provider modes

- `MOCK`: default development provider; no external request or live financial movement. Set `FinoDmt:MockResult` to `SUCCESS`, `PENDING`, or `FAILED` for deterministic testing.
- `TEST`: reserved for a future verified sandbox adapter; currently uses no live provider.
- `LIVE`: explicitly disabled until Fino’s verified API contract, credentials, callback verification, compliance approval, reconciliation, and sandbox certification are supplied. It cannot silently fall back to MOCK.

## Known limitations

- Fino-DMT legacy behavior is unresolved; the implementation is a safe architectural foundation, not a certified production integration.
- Fino-specific charges, commission, KYC, beneficiary lifecycle, OTP, callback/status polling, refund/reversal, and provider response mapping require confirmation.
- The mock success path requires sufficient wallet balance and a deployed wallet ledger account; no live transaction should be attempted.
