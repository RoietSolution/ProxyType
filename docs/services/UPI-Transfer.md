# UPI Transfer

## Legacy verification

No standalone outbound UPI Transfer was found in the Laravel source, SQL dump, or documented CRM inspection. The verified legacy UPI behavior is wallet funding: TarangPay order creation/status polling and a separate bearer-authenticated fund-order/webhook flow. Those flows credit the wallet after provider success; they do not define an outbound transfer.

The legacy pages collect only an amount. One opens a provider-returned `payment_url`; the other displays a configured static QR and may open a provider-returned `payment_url`. The second provider request includes the logged-in user's name, email, and mobile as customer metadata. No VPA, mobile-linked destination, account/IFSC, collect address, intent URI, or explicit routing field is present. The provider-side payment method behind the hosted order/QR is **UNKNOWN / REQUIRES CONFIRMATION**.

Beneficiary/customer fields, production VPA rules, limits, provider contract and authentication, response/status mapping, charges, commission, callback signature, polling, reversal/refund, and receipt/history requirements are **UNKNOWN / REQUIRES CONFIRMATION**.

## Replacement behavior

ProxyType now represents the verified legacy shape as `UPI Add Money` while retaining the existing service code `upi_transfer` to avoid a duplicate catalog entry. The request accepts only an amount and idempotency key. The ₹1–₹100,000 range is new-platform server-side safety validation; legacy limits beyond minimum ₹1 are **UNKNOWN / REQUIRES CONFIRMATION**.

The service reuses `finance.ServiceTransactions`, service permissions, CMF → CSF → CSP scope evaluation, provider routes, the shared ledger account model, journal entries, receipts, audit, and transaction history. Order creation stores pending state and hosted payment details. The wallet is not changed when an order is created. A provider status/callback result must be normalized to SUCCESS before the atomic wallet credit procedure runs. Charges and commission are zero because no rule was verified.

MOCK order creation is deterministic through `UpiTransfer:MockOrderResult`: `ORDER_CREATED`, `FAILED`, or `EXPIRED`. Status checks use `UpiTransfer:MockStatus`: `PENDING`, `SUCCESS`, `FAILED`, or `EXPIRED`. `POST /callbacks/mock` requires the configured MOCK callback key. SUCCESS credits the user wallet once through `finance.ApplyUpiFundingProviderResult`; FAILED, EXPIRED, and PENDING do not credit. The SQL procedure uses an application lock, provider-event uniqueness, provider-reference uniqueness, wallet row locks, journal idempotency, and balanced journal postings. LIVE is explicitly disabled and cannot fall back to MOCK silently.

The MOCK callback seam is enabled for deterministic development and is authenticated with its configured callback key. A production callback must add provider signature/authentication and replay verification when the real contract is confirmed. Pending reconciliation, settlement, and reversal/refund rules are **UNKNOWN / REQUIRES CONFIRMATION**.

## API/UI

- `POST /api/services/upi-transfer/orders` (authenticated)
- `GET /api/services/upi-transfer/orders/{transactionId}/status` (authenticated)
- `POST /api/services/upi-transfer/callbacks/mock` (MOCK callback key required)
- Angular route: `/services/upi-transfer`
- History: existing scoped `GET /api/transactions?service=upi_transfer`
- SQL: `database/008_upi_transfer.sql` and `database/009_upi_funding_flow.sql`, schema version 9

## Readiness

This is a MOCK hosted-funding slice, not a production UPI integration. LIVE/provider approval, exact payment-method semantics, pricing, callback signature verification, settlement, reversal, and broader authenticated E2E verification remain required before production use. There is no outbound VPA transfer implementation.
