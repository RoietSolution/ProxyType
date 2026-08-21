# Wallet to Wallet

## Verified legacy flow

The legacy form accepts a 10-digit recipient mobile number and an amount with a minimum of 10. It resolves the recipient by mobile, blocks self-transfer, checks the active `wallet_to_wallet` service, computes a role/service charge, checks the sender balance, debits the sender, credits the receiver, writes `wallet_to_wallet_txns`, and creates a receipt. Legacy writes were not protected by one database transaction or row locks.

The legacy charge function supports fixed and percentage bands. The inspected dump includes a zero fixed charge for the available wallet-transfer role range, but complete production role-band mapping is `UNKNOWN / REQUIRES CONFIRMATION`.

## New implementation

- API: `GET /api/services/wallet-to-wallet/receivers?mobile=...` and `POST /api/services/wallet-to-wallet/transfers`.
- Receiver lookup exposes only active users with active memberships inside the caller's accessible CMF → CSF → CSP descendant scope.
- Server-side checks cover amount, service activation, user activity, hierarchy scope, service permission, receiver state, self-transfer, and idempotency key.
- `database/005_wallet_to_wallet.sql` adds counterparty tracking and `finance.PostWalletToWalletTransfer`.
- Sender debit and receiver credit are balanced journal entries in one SQL transaction. Account locks are deterministic and the idempotency key is protected with `sp_getapplock`.
- No external provider is used. Charge and commission are currently zero because no complete new-platform charge policy is verified.
- Each attempt receives a transaction record and receipt. Failed posting does not alter either wallet; duplicate idempotency does not create a second journal.

## UI and testing

The dashboard service card and sidebar link to `/services/wallet-to-wallet`. The responsive form supports receiver lookup, amount, loading/error states, close/back-to-home, and receipt display.

Automated hierarchy tests pass for CMF, CSF, and CSP isolation. A disposable SQL smoke test passed atomic balances (sender 200→100, receiver 0→100), duplicate journal count 1, and cleanup count 0.

## Known limitations

Production readiness remains blocked pending broader multi-user/UAT coverage, explicit insufficient-balance and concurrent-request execution in the deployed API, and confirmation of all legacy role-specific charge bands. No live provider credentials are required for this internal transfer.
