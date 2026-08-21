# AEPS King

Status: BLOCKED — no verified legacy service definition

## Verification result

AEPS King was not implemented because the available legacy evidence does not define it safely.

The following read-only sources were checked on 21 August 2026:

- Laravel source at `C:\Users\pc\Downloads\well-known (2)\`
- SQL dump at `C:\Users\pc\Downloads\proxytourtravels_x0c5Hi3tPyP38ScHVkpbnq (2).sql`
- CRM database `DESKTOP-DQ0868S / CRM`
- `docs/Legacy-Service-Requirements.md`

No `AEPS King`, `AEPS-King`, or `King AEPS` service, route, controller, view, model, provider configuration, table, or SQL service-catalog row was found. The legacy sources do contain separate AEPS variants named `aeps`, `aeps2`, and `unionbank`; they cannot be assumed to be AEPS King. The read-only CRM check found no AEPS service catalog and no AEPS King value; the populated `dbo.CSPREG.SERVICEPROVIDER` value observed was `Airtel`.

## Unverified behavior

The sources do not establish:

- transaction types or supported bank/IIN behavior;
- Aadhaar/customer, mobile, amount, location, or biometric/device fields;
- provider/API contract or request signing;
- validation and status mapping;
- wallet/ledger impact, charges, commission, or settlement;
- receipt and transaction-history fields;
- callback/status polling, reversal, refund, or reconciliation behavior.

The existing AEPS common foundation is not enabled for AEPS King because doing so would create an unverified product contract. No AEPS King service key, API, Angular route, SQL migration, provider adapter, or LIVE configuration was added. No LegacyID/OldId field was added.

## Required evidence before implementation

Provide an approved AEPS King product definition and provider documentation or representative sandbox traffic covering the missing workflow, including success/failure/pending responses, callback and reversal rules, pricing/commission, wallet settlement, and access restrictions. After that evidence is verified, the existing AEPS bank/IIN, transaction, masking, provider, ledger, receipt, audit, idempotency, permission, and hierarchy components can be evaluated for reuse.

## Verification outcome

AEPS King authenticated E2E, SQL deployment, backend/API implementation, Angular implementation, and provider tests are **BLOCKED / NOT RUN** because no safe AEPS King contract exists. LIVE is disabled and no fallback to MOCK was introduced.

The strongest verified next unimplemented candidate is **PAN Card**. Its legacy route/controller/view, `pan_card_txns` persistence, internal NSDL submission endpoint, service-charge calculation, wallet debit, and receipt behavior are documented. Its exact form/document rules and live provider status/refund contract still require confirmation before production use, so it should be verified and implemented as a separate task.
