# Service Implementation Status

Statuses reflect only work actually verified in the repository. Fino DMT, UPI Add Money, and Fund Request are the implemented service slices; no additional service is being implemented in this task.

| Service | Legacy analysis | Requirements verified | New DB design | Backend model | Backend API | Provider integration | Angular form | Validation | Wallet integration | Commission integration | Transaction history | Receipt | Automated tests | Integration tested | Production ready |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Fino DMT | ANALYZED | BLOCKED — no verified Fino-DMT legacy/provider flow | IMPLEMENTED | IMPLEMENTED | IMPLEMENTED | MOCK implemented; LIVE BLOCKED | IMPLEMENTED | IMPLEMENTED | IMPLEMENTED for successful mock result, insufficient-balance safe failure | NOT STARTED — legacy Fino rule unresolved | IMPLEMENTED | IMPLEMENTED | IN PROGRESS | NOT STARTED | BLOCKED |
| AEPS | ANALYZED | IMPLEMENTED (legacy transaction stage verified) | IMPLEMENTED and deployed (v7) | IMPLEMENTED | IMPLEMENTED | MOCK implemented; LIVE BLOCKED | IMPLEMENTED | IMPLEMENTED | IMPLEMENTED and E2E-verified for successful cash withdrawal AEPS-wallet credit | NOT STARTED (legacy pricing rule unresolved; returns zero) | IMPLEMENTED and E2E-verified | IMPLEMENTED and E2E-verified | TESTED (23/23 plus E2E) | TESTED (host SQL/API MOCK E2E) | BLOCKED (LIVE/provider/compliance/UAT) |
| UPI Add Money (`upi_transfer`) | ANALYZED — verified hosted funding variants | IMPLEMENTED for verified legacy shape; provider payment method UNKNOWN | IMPLEMENTED (v8/v9 migrations) | IMPLEMENTED | IMPLEMENTED | MOCK hosted-order/status/callback; LIVE BLOCKED | IMPLEMENTED | IMPLEMENTED | IMPLEMENTED: no credit on order/pending/failed; atomic credit after SUCCESS | ZERO / NOT VERIFIED | IMPLEMENTED via shared history | IMPLEMENTED | TESTED (29/29 plus SQL smoke) | BLOCKED (browser unavailable; authenticated host E2E not run) | BLOCKED |
| Fund Request (`fund_request`) | ANALYZED — verified manual NEFT proof/approval flow | IMPLEMENTED for verified manual shape; hierarchy routing beyond legacy admin permissions UNKNOWN | IMPLEMENTED (v10 migration) | IMPLEMENTED | IMPLEMENTED | NOT APPLICABLE — manual workflow; no provider route | IMPLEMENTED | IMPLEMENTED | IMPLEMENTED: pending/rejected no credit; authorized approval atomically credits once | ZERO / NOT VERIFIED | IMPLEMENTED via shared history | IMPLEMENTED | TESTED (32/32 plus SQL smoke) | NOT RUN | BLOCKED (legacy rules/UAT/host E2E) |
| CMS | ANALYZED | BLOCKED — not found | NOT STARTED | NOT STARTED | NOT STARTED | BLOCKED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | BLOCKED |
| Airtel DMT | ANALYZED | BLOCKED — Airtel found for AEPS, not DMT | NOT STARTED | NOT STARTED | NOT STARTED | BLOCKED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | BLOCKED |
| AEPS King | ANALYZED | BLOCKED — not found | NOT STARTED | NOT STARTED | NOT STARTED | BLOCKED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | BLOCKED |
| PAN Card | ANALYZED | ANALYZED | NOT STARTED | NOT STARTED | NOT STARTED | BLOCKED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | BLOCKED |
| Payment Gateway | ANALYZED as funding variants | BLOCKED — no standalone definition | NOT STARTED | NOT STARTED | NOT STARTED | BLOCKED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | BLOCKED |
| DMT PPI | ANALYZED | BLOCKED — not found | NOT STARTED | NOT STARTED | NOT STARTED | BLOCKED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | BLOCKED |
| Move to Bank | ANALYZED as DMT/Payout aliases | BLOCKED — separate product not found | NOT STARTED | NOT STARTED | NOT STARTED | BLOCKED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | BLOCKED |
| Recharge | ANALYZED | ANALYZED | IMPLEMENTED | IMPLEMENTED | IMPLEMENTED | MOCK IMPLEMENTED; LIVE BLOCKED | IMPLEMENTED | IMPLEMENTED | IMPLEMENTED | IMPLEMENTED | IMPLEMENTED | IMPLEMENTED | TESTED | TESTED | BLOCKED |
| Wallet to Wallet | ANALYZED | ANALYZED | IMPLEMENTED | IMPLEMENTED | IMPLEMENTED | NOT APPLICABLE | IMPLEMENTED | IMPLEMENTED | IMPLEMENTED | NOT APPLICABLE (verified legacy charge is zero; configurable bands unresolved) | IMPLEMENTED | IMPLEMENTED | TESTED | TESTED (SQL atomic/idempotency smoke) | BLOCKED (production readiness requires broader live-user/UAT coverage) |
| Credit Card | ANALYZED | BLOCKED — catalog-only external link | NOT STARTED | NOT STARTED | NOT STARTED | BLOCKED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | NOT STARTED | BLOCKED |

## Status definitions

### UPI Transfer verification update

Legacy tracing verified only UPI wallet-funding variants: amount-only forms, a TarangPay hosted `payment_url`, and a second provider's hosted order/static QR flow with customer metadata. No outbound VPA, mobile-linked, account/IFSC, collect, intent, or explicit routing semantics were found. The existing `upi_transfer` code is now represented as `UPI Add Money`: order creation, pending hosted details, status check, MOCK callback seam, and atomic success-only wallet credit. Charges, commission, callback signature verification, exact payment-method semantics, status/reversal/refund, and LIVE provider behavior remain UNKNOWN / REQUIRES CONFIRMATION. Automated tests and rollback-scoped SQL smoke verification passed; authenticated host/browser E2E is BLOCKED because no browser runtime or usable test credential was available.

### Fund Request verification update

Legacy manual Fund Request is distinct from hosted UPI funding: authenticated user → NEFT payment to configured business bank account → amount/reference/screenshot submission → admin/employee review → direct wallet credit on approval. Legacy maximum amount, CMF/CSF/CSP approval routing, cancellation, reversal, receipt, and concurrency/idempotency rules remain unknown. The replacement uses shared transactions, receipts, audit, permissions, hierarchy scope, proof storage, a fixed NEFT mode, and SQL application-lock/journal idempotency for approval.

### Fino DMT verification update

The deployed MOCK flow was integration-tested for SUCCESS, FAILED, PENDING, duplicate idempotency, transaction history, receipt creation, and safe insufficient-balance failure. Concurrent wallet-debit safety was verified at the SQL procedure level. LIVE remains BLOCKED because no verified Fino-DMT provider contract or credentials were found.

- `NOT STARTED`: no implementation work completed.
- `ANALYZED`: legacy evidence has been recorded, but no replacement implementation exists.
- `IN PROGRESS`: implementation exists but required verification remains.
- `IMPLEMENTED`: repository code/schema exists for the item.
- `TESTED`: automated or integration verification has executed successfully.
- `BLOCKED`: a verified requirement, provider contract, credential, or safe integration prerequisite is missing.

### Recharge verification update

Recharge has an implemented shared-foundation MOCK slice for the verified v1 fields and operator catalog. MOCK success, failure, pending, invalid input, insufficient balance, duplicate idempotency, wallet integrity, history, receipt, and denied service permission were executed. LIVE provider integration and production readiness remain BLOCKED.

### AEPS verification update

The verified legacy transaction stage supports balance enquiry, mini statement, and cash withdrawal. It requires Aadhaar, customer mobile, bank/IIN, device name, and fingerprint data; amount is required only for cash withdrawal. Active AEPS onboarding is required. Legacy cash success credits the separate AEPS wallet and issues a receipt; inquiry flows do not have a verified wallet mutation. Onboarding models contain latitude/longitude, but these were not required in the verified transaction payload. The replacement stores only masked Aadhaar/mobile and device metadata, never raw biometric payloads.

The new AEPS service uses `finance.AepsBanks`, `finance.AepsTransactionDetails`, shared service transaction/receipt/audit structures, and an atomic idempotent AEPS ledger-credit procedure. The UI accepts a biometric capture reference for MOCK development only. LIVE is disabled because no verified provider contract or credentials were available. Charges, commission, callback/status polling, and reversal/refund rules remain unresolved and are not invented; commission and charges remain zero pending approved rules.

The SQL deployment and authenticated MOCK E2E run were completed on `localhost / proxytype_DB` under the host Windows identity `DESKTOP-DQ0868S\pc`. The restricted agent identity `DESKTOP-DQ0868S\CodexSandboxOffline` is not authorized for Windows SSPI, which caused the earlier connectivity block; appsettings, User Secrets, and environment configuration did not contain a conflicting connection string. The Angular production build also passes under the host context. Under the restricted context it reproducibly exited `-1073741819` (`0xC0000005`) and the direct Angular compiler hit `EPERM` creating `out-tsc`, identifying a native/filesystem sandbox issue rather than an Angular application error. The host build emitted only the existing dashboard stylesheet budget warning.

### AEPS King verification update

AEPS King remains **BLOCKED** and was not implemented. Searches of the Laravel source and SQL dump found no AEPS King service, route, provider, table, or service-catalog entry. The SQL catalog contains separate `aeps`, `aeps2`, and `unionbank` entries; those variants are not treated as AEPS King. A read-only check of the referenced CRM found no AEPS service catalog and no AEPS King provider value. Because transaction types, inputs, provider contract, statuses, wallet/commission rules, receipts, callbacks, and reversals are unverified, no API, Angular screen, SQL migration, MOCK route, or LIVE configuration was added.

PAN Card is recommended as the next unimplemented service to verify because its legacy route/controller/view, persistence, NSDL submission endpoint, charge/debit, and receipt behavior are documented, although exact form/document and live-provider rules still require confirmation.
