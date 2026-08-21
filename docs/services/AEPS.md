# AEPS

## Verified behavior

The legacy Laravel transaction stage supports Cash Withdrawal, Balance Enquiry, and Mini Statement. It requires Aadhaar, customer mobile, bank/IIN, device name, and biometric/fingerprint data. Cash Withdrawal additionally requires an amount. Active AEPS onboarding is checked. Latitude/longitude/pincode were verified on onboarding models, not this transaction payload.

## Replacement implementation

- API: `GET /api/services/aeps/banks`, `POST /api/services/aeps/transactions`.
- UI: `/services/aeps`, with transaction type, bank/IIN, Aadhaar, mobile, device name, biometric capture reference, and conditional amount.
- Database: `finance.AepsBanks` and `finance.AepsTransactionDetails`; shared service transactions, receipts, audit logs, permissions, and ledger accounts are reused.
- Statuses: canonical `SUCCEEDED`, `FAILED`, and `PENDING`; provider raw status remains on the shared transaction.
- Idempotency: organization-scoped transaction key and a journal idempotency key protect duplicate requests and wallet postings.

## Wallet, charges, and commission

Verified legacy cash success credits the separate AEPS wallet. The replacement represents this as a dedicated per-user AEPS ledger account and posts an atomic balanced journal only after a successful provider result. Balance enquiry and mini statement do not mutate the wallet. No verified pricing/commission band was found, so charges and commission are zero.

## Provider mode

`MOCK` is enabled for development and supports deterministic `SUCCESS`, `FAILED`, and `PENDING` through `Aeps:MockResult`. `LIVE` resolves to an explicit disabled provider and never falls back to MOCK.

## Sensitive data

Raw Aadhaar and biometric payloads are transient request data only: they are not persisted, logged, returned, or placed in receipt snapshots. Stored details and history expose masked Aadhaar/mobile only. Provider credentials remain server-side.

## Unresolved

Provider onboarding/authentication, exact live bank master, callbacks/status checks, reversals/refunds, charge/commission bands, and production biometric-device integration require an approved provider contract and compliance decision.

## Local verification

Migration 007 is deployed at schema version 7. Authenticated MOCK E2E passed for cash withdrawal, balance enquiry, mini statement, SUCCESS, FAILED, PENDING, invalid Aadhaar, invalid amount, idempotency, receipts, history, wallet/journal integrity, disabled service, and CSP scope isolation. Raw Aadhaar and biometric capture references were absent from stored request summaries, receipts, audits, and API responses.

The local app connection remains the appsettings value `Server=localhost;Database=proxytype_DB;Integrated Security=true;Encrypt=false;TrustServerCertificate=true`. The original SSPI failure was caused by the restricted process running as `DESKTOP-DQ0868S\\CodexSandboxOffline`; SQL Server authorizes the host identity `DESKTOP-DQ0868S\\pc`. No credential or connection-string change was committed. Angular production build passes under the host identity; the restricted process's `-1073741819` crash is an environment/native filesystem limitation, with TypeScript still passing.
