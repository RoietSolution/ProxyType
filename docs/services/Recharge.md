# Recharge

## Legacy behavior verified

The Laravel application contains five recharge variants:

- `/v1/recharge/*` → `RechargeController`
- `/v2/recharge/*` → `RechargeController3`
- `/v3/recharge/*` → `RechargeController2`
- `/v4/recharge/*` → `RechargeController4`
- `/v5/recharge/*` → `RechargeController5`

The inspected forms use `account`, `amount`, `operator`, `type`, six-digit `pincode`, `lat`, and `lng`. Amount validation is numeric with a minimum of 10. The verified types are `mobile`, `dth`, `google`, and `lapu`.

Verified legacy operators include Airtel, BSNL Special Tariff, BSNL Talktime, BSNL Special LAPU, Google Play Voucher, Jio, Vi, Airtel Digital TV, Dish TV, Sun Direct, Tata Sky, and Videocon D2H.

Legacy providers are an internal Basic Auth proxy, RechargeExchange, MRobotics, and Bluecant. Their credentials and complete production contracts are not available to the replacement application and are never returned to Angular.

## New implementation

The replacement uses the shared `finance.ServiceTransactions`, provider route, permission evaluator, wallet ledger, idempotency key, journal, receipt, and transaction-history endpoint. It uses service code `recharge_v1` as the first Recharge implementation slice; the other legacy provider variants remain catalog entries and are not separately implemented.

API endpoints:

- `GET /api/services/recharge/operators?type=MOBILE|DTH|GOOGLE|LAPU`
- `POST /api/services/recharge/transactions`

The request requires account, amount, operator, recharge type, pincode, latitude, longitude, and an idempotency key. The server validates all values; Angular-calculated values are not trusted.

## Status and wallet behavior

Provider responses normalize to `SUCCEEDED`, `FAILED`, or `PENDING`. In MOCK mode, `Recharge:MockResult` can be `SUCCESS`, `FAILED`, or `PENDING`.

- `SUCCEEDED`: wallet debit is posted with the configured operator commission.
- `PENDING`: wallet debit is posted without commission, matching the inspected legacy v1 behavior.
- `FAILED`: no wallet debit or commission is posted.
- Insufficient balance: the transaction fails safely and the wallet remains unchanged.
- Duplicate idempotency keys return the original transaction and cannot create another journal debit.

The ledger procedure uses SQL transaction locking and a filtered reversal index. LIVE mode is explicitly disabled and cannot fall back to MOCK.

Commission rates are stored in `finance.RechargeOperators`. The legacy dump contains role-specific commission rates, but the current CRM role mapping is not equivalent, so deployed default rates are zero until a current-role pricing policy is confirmed. Charges are zero because no verified Recharge charge rule was found.

## Database objects

`database/004_recharge.sql` deploys `finance.RechargeOperators`, the `recharge_mock` provider route, Recharge clearing and commission ledger accounts, `finance.PostRechargeWalletDebit`, and schema version 4.

## Known limitations

Live provider URLs, credentials, callbacks, exact reversal timing, and current-role commission rates require confirmation. The implementation is MOCK/test-ready and not production-ready for live financial recharge.
