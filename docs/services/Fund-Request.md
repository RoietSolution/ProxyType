# Fund Request

## Verified legacy workflow

The manual legacy flow is separate from UPI hosted funding:

`Authenticated user with approved bank account` → `NEFT to configured business bank account` → `amount + txnId/reference + screenshot` → `pending` → `admin/authorized employee approval or rejection` → `wallet credit only on approval`.

Verified rules:

- Amount is required, numeric, and at least ₹1. A legacy maximum was not found.
- `txnId` is required and unique; screenshot is required and accepts JPG/JPEG/PNG/GIF.
- The UI displays configured bank details and says only NEFT is accepted.
- Manual requests are stored as pending, then become approved or rejected through the admin Fund Request action.
- Approval directly credits the requesting user's wallet; rejection does not credit.
- Users can view their own request history. No standalone manual receipt, cancellation, reversal, or explicit CMF → CSF → CSP approval routing was verified.

The legacy approval write is not atomic or idempotent. ProxyType does not reproduce that unsafe behavior.

## ProxyType implementation

The service code is `fund_request`, with display name `Fund Request`. It does not reuse the UPI Add Money hosted provider flow and has no provider route.

Create request:

- `POST /api/services/fund-request/requests` (authenticated multipart request)
- Amount, UTR/transaction reference, image proof, and idempotency key are required.
- The server controls payment mode as `NEFT`; the client cannot choose a gateway or UPI mode.
- New-platform safety limits are ₹1–₹1,000,000 and proof size up to 5 MB. Legacy maximum/size rules are **UNKNOWN / REQUIRES CONFIRMATION**.

Review and history:

- `GET /api/services/fund-request/requests` returns the requester’s history; Platform/CMF/CSF administrators receive only requests in their permitted hierarchy branch.
- `POST /api/services/fund-request/requests/{id}/review` accepts `APPROVED` or `REJECTED`.
- `GET /api/services/fund-request/requests/{id}/proof` is restricted to the requester or an authorized reviewer in scope.
- Service permissions, active membership, CMF → CSF → CSP scope, transaction history, receipt, and audit records are enforced.

Approval is implemented by `finance.ReviewFundRequest`. It locks the request, makes repeated same decisions no-ops, posts one balanced clearing-to-wallet journal on approval, updates the request and service transaction, and updates the receipt/audit record in the same transaction. Pending and rejected requests have zero wallet credit.

The clearing account `CLEARING:FUND_REQUEST` represents the externally received bank deposit. Charges and commission are zero because no legacy rule was verified.

## Unresolved requirements

`UNKNOWN / REQUIRES CONFIRMATION`: exact reviewer hierarchy, employee/admin role mapping in the replacement, amount maximum, bank-account ownership/matching rules, proof retention policy, cancellation, reversal/refund, receipt format, notifications, and any post-approval dispute behavior.

No `LegacyID`, `OldId`, or similar compatibility field was added. LIVE/provider integration is not applicable to this manual workflow.
