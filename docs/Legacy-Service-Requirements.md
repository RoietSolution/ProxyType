# Legacy Service Requirements

## Scope and evidence

This document records the verified behavior of the three read-only sources inspected on 20 August 2026:

1. Laravel 11 source at `C:\Users\pc\Downloads\well-known (2)\`.
2. SQL dump at `C:\Users\pc\Downloads\proxytourtravels_x0c5Hi3tPyP38ScHVkpbnq (2).sql`.
3. SQL Server 2022 database `DESKTOP-DQ0868S / CRM`.

The Laravel application and dump were not modified. CRM was queried only through catalog and aggregate/select queries. The new database must not be treated as a byte-for-byte migration of either legacy schema.

## Laravel application profile

- Framework: Laravel 11, PHP 8.2+, Sanctum 4, Blade, JavaScript/AJAX.
- Runtime database: `.env` selects `mysql`; the matching dump identifies MariaDB 10.11.18 and phpMyAdmin 5.2.2.
- Authentication: distinct `user`, `admin`, and `employee` guards, plus Sanctum bearer tokens for the API. User login can require OTP based on a setting. Admin/employee permissions use `employee_permissions.permission_key`; ordinary service access uses `active_services`.
- Application shape: controller-heavy. The API `UserController` is approximately 9,000 lines and duplicates much of the web-controller behavior. Provider adapters, orchestration, wallet mutation, persistence, messaging, and HTTP response construction are mixed in controllers.
- UI: 274 Blade files. Financial forms use server-rendered pages plus AJAX JSON submissions. Errors are generally returned as HTTP 422 with the first validation message.
- Provider credentials: sourced inconsistently from `settings`, `.env`, `config/credentials.php`, and literals. The dump contains plaintext provider/email credentials. Values are intentionally omitted here and must be rotated rather than migrated.

## Laravel identity and hierarchy

`users` has `role_id`, `ref_by`, and `created_by`:

- Roles seeded in the dump: State Head (`SH`), Master Distributor (`MD`), Distributor (`DT`), Retailer (`RT`).
- `ref_by` is a self foreign key and drives referrals.
- `created_by` is declared as `varchar(50)` despite the model treating it as a user relationship; runtime records also contain values such as `admin`. It is not a reliable hierarchy foreign key.
- The code permits role changes and referral chains, but it does not encode CMF → CSF → CSP as a strongly constrained hierarchy.

This conflicts with the requested target hierarchy. CRM is the authoritative structural source for CMF/CSF/CSP, while Laravel remains the behavioral source for financial services.

## Original MariaDB dump

### Table inventory

The dump contains 69 application/framework tables.

| Area | Tables |
|---|---|
| Identity and access | `users`, `roles`, `sub_roles`, `admins`, `employees`, `employee_permissions`, `personal_access_tokens`, `sessions`, `login_logs` |
| Service catalog/configuration | `services`, `active_services`, `settings`, `charges`, `commissions`, `recharge_operators`, `RechargeCommissionRecords` |
| Wallet/funding | `wallets`, `aeps_wallets`, `wallet_to_wallet_txns`, `fund_records`, `add_money_records`, `bank_accounts`, `add_accounts` |
| Recharge/bill/transfer transactions | `recharge_txns`, `bbps_txns`, `dmt_txns`, `payout_txns`, `lic_txns`, `lpg_txns`, `pan_card_txns`, `aeps_txns` |
| Provider onboarding/beneficiaries | `aeps_onboarding_registrations`, `aeps2_onboarding_registrations`, `union_bank_aeps_registrations`, `aeps_chagans`, `dmt_registrations`, `dmt_beneficiaries`, `payout_banks` |
| Account-opening services | `account_agents`, `account_details`, `kotakbank_bcajents`, `payoutaccount` |
| KYC and documents | `kycs`, `digilocar_users`, `certificates`, `id_cards` |
| Products/plans/rewards | `prime_plan`, `prime_plan_txn`, `products`, `user_orders`, `rewards`, `get_user_rewards`, `winners`, `paid_registartions`, `registration_amounts` |
| Support/content | `support_tickets`, `support_replies`, `ticket`, `news`, `news_post`, `offer_images`, `web_pages`, `otherservices`, `other_services`, `user_notifications` |
| Receipts/framework | `receipts`, `migrations`, `cache`, `cache_locks` |

### Important relational rules

- `users.username`, `users.email`, and `users.mobile` are unique; `users.role_id → roles.id`; `users.ref_by → users.id`.
- `active_services` is unique on `(user_id, service_id)` and cascades to users/services.
- Charge and commission bands are unique on `(service_id, role_id, price_from, price_to)`, but overlapping bands are still possible.
- Most primary service transaction IDs are unique (`recharge_txns`, `bbps_txns`, `dmt_txns`, `payout_txns`, `lic_txns`, `pan_card_txns`). AEPS does not have the same unique transaction constraint in this dump.
- `wallets` declares both a global unique `txnId` and `(user_id, txnId, txn_category)`. The global unique key prevents the intended debit/credit pair from sharing a transfer transaction ID across two wallet rows. Dump data is sparse enough not to expose every runtime conflict.
- `wallet_to_wallet_txns.sender_id` and `receiver_id` are indexed but have no foreign keys.
- Numerous provider/onboarding tables have `user_id` columns without foreign keys.
- The dump uses both `utf8mb4` and legacy `latin1` collations and mixes `bigint`, `int`, and unsigned keys.

### Status vocabulary

The schema/code uses incompatible values and spelling:

- `pending`, `success`, `failed`, `refund`
- `refend`, `faild`
- `approved`, `declined`, `rejected`
- `active`, `inactive`, `deactive`, `deleted`
- mixed-case `Pending`, `Accepted`, `Rejected`

The new database must use canonical status codes and keep provider raw statuses separately.

## End-to-end Laravel service mapping

Routes below show the principal web path. Equivalent Sanctum API endpoints exist in `routes/api.php` and largely repeat the same behavior in `Api\UserController`.

### Wallet, fund, and gateway flows

| Route → controller method | Model/query → tables | Form and validation | Provider/result |
|---|---|---|---|
| `POST /wallet-transfer/transfer` → `WalletController::walletTransferSubmit` | `User`, `WalletToWalletTxn`, `Wallet`, `Receipt` → `users`, `wallet_to_wallet_txns`, `wallets`, `receipts` | `wallet.wallettowallet`; mobile is 10 digits, amount numeric and at least 10; blocks self-transfer; service must be active | Looks up role/service charge, verifies balance after `lock_amount`, debits sender, credits receiver, writes success transaction and receipt. No DB transaction/row lock protects the multi-write operation. |
| `POST /add-money/request` → `MoneyController::addMoneyRequest` | `AddMoneyRecord` → `add_money_records` | `add_money`; amount/reference/payment proof fields | Manual request remains pending until admin approval. Admin `fundRequestUpdate` changes status and directly increments `users.wallet_balance`. |
| `POST /add-money-upirequest` → `MoneyController::addMoneyUpirequest` | `AddMoneyRecord`, `User` | UPI amount/request fields | TarangPay order API; status poll can approve and directly increment wallet. |
| `POST /add-fund-request` → `MoneyController::addFundRequest` | `AddMoneyRecord`, `User` | `addfundupi`; amount and payer data | CutePe order API with bearer token; poll/webhook updates request and wallet. Webhook/poll duplication needs idempotency in the replacement. |
| Admin credit/debit routes → `Admin\WalletController` | `FundRecord`, `Wallet`, `Receipt`, `UserNotification` | user must exist; amount numeric ≥ 1 | Calls shared credit/debit helpers and records a receipt. The legacy debit helper can create a negative balance if invoked without a preceding balance check. |
| `POST /aepsamount-transfer-wallet` → `WalletController::aepswalletTransferWallet` | `AepsWallet`, `Wallet`, `User`, `Receipt` | amount numeric ≥ 10 | Computes AEPS balance as credits minus debits, credits main wallet and debits AEPS wallet. Not atomic. |

### Recharge variants

Common form views are `recharge_v1.recharge`, `recharge_v3.recharge3`, `recharge_v2.rechargev2`, `recharge_v4.recharge4`, and `recharge_v5.recharge5`. Common validation: account string, amount numeric ≥ 10, operator/type required; most versions also require six-digit pincode and numeric latitude/longitude.

| Route → controller | Provider | Persistence/result |
|---|---|---|
| `/v1/recharge/*` → `RechargeController::rechargeV1Submit` | Internal proxy `/v1/recharge/do-recharge` using Basic Auth | `RechargeTxn` → `recharge_txns`; wallet debit on success/pending; operator commission record and wallet credit on success; receipt and WhatsApp message. |
| `/v2/recharge/*` → `RechargeController3::rechargeV1Submit2` | Internal proxy `/v2/recharge/do-recharge3` | Same shared transaction/wallet/commission pattern, service label `recharge_v2`. |
| `/v3/recharge/*` → `RechargeController2::rechargeV1Submit3` | RechargeExchange transaction API with hard-coded operator mapping | Same pattern, service label `recharge_v3`; callbacks reconcile success/failure/refund. |
| `/v4/recharge/*` → `RechargeController4::rechargeV1Submit4` | MRobotics recharge API | Same pattern, service label `recharge_v4`. |
| `/v5/recharge/*` → `RechargeController5::rechargeV1Submit5` | Bluecant recharge API | Same pattern, service label `recharge_v5`; provider statuses `Success`/`Failed` mapped to legacy status. |

All variants use timestamp-plus-prefix transaction IDs with only second precision, creating a collision risk under concurrency. Some success paths debit first and credit commission as a second wallet operation; neither is transactional.

### BBPS

| Route → controller method | Model/table/form/validation | Provider/result |
|---|---|---|
| `POST /operator-fields` → `BbpsController::fetchopFilds` | Dynamic operator fields; provider payload validation | Internal `/opretor-filds`; drives dynamic Blade fields. |
| `POST /bbps/fetch-bill` → `fetchBill` | `bbps.pay`; operator and provider-defined fields | Internal `/bill-payment/bill/fetch-bill-details`. |
| `POST /bbps/bill-payment` → `payBill` | `BbpsTxn` → `bbps_txns`; operator, account, amount/bill data; wallet sufficiency | Internal `/bill-payment/bill/pay-bill`; debit on success/pending, commission on success, receipt. |
| BBPS2 routes → `bbps2index`, `fetchBillbbps2`, `paybill2` | Same `bbps_txns`, views `bbps.bbps2*` | `/bill-payment2/...` for discovery/fetch, but payment calls the v1 payment path. |
| BBPS3 routes → `index3`, `bbps3fetchFields`, `bbps3fetchBillRecord`, `paybill3` | Dynamic provider schema; same transaction table | `/providerlist`, `/bbps3/provider-fields`, `/bbps3/fetchbill`, `/bill-payment3/paybill3`. |

Statuses are success/pending/failed/refund; callback updates can credit refunds.

### DMT

| Route → controller method | Tables/forms/validation | Provider/result |
|---|---|---|
| `POST /dmt/transaction` → `DmtController::transaction` | `dmt.index`; beneficiary bank/account/IFSC, amount, mode; `DmtTxn`, `AepsWallet`, `Receipt` | Internal `/dmt/send-money`; charges are banded; successful/pending sends debit the main wallet or AEPS wallet depending on flow. |
| DMT2 KYC routes → `dmtKycuser`, `dmtEKycuser` | `dmt_registrations`; mobile/PAN/Aadhaar/KYC fields | `/dmt/registermobile`, `/dmt/remitterkyc`, `/dmt/registration`. |
| Beneficiary routes → `dmtAddbank`, `fetchbank`, `sendOtp` | `dmt_beneficiaries`; beneficiary/bank/account/IFSC and OTP | `/dmt/get-banks`, `/dmt/addbank`, `/dmt/getbankdet`, `/dmt/getotp`. |
| `POST /dmt/money-transfer` → `dmtMoneytran` | `dmt_txns`; amount/mode/beneficiary/OTP | `/dmt/sendMoney`; charge + principal, receipt, messages, status. |
| `/dmt/refend*` → DMT refund methods | `dmt_txns`, `aeps_wallets` | `/dmt/sendotprefned`, `/dmt/submitrefeotp`; writes misspelled `refend`. |

### Payout

| Route → controller method | Tables/forms/validation | Provider/result |
|---|---|---|
| `POST /payout/transaction` → `PayoutController::transaction` | `payout.index`; approved bank account, amount, IMPS/NEFT/RTGS; `payout_txns`, `aeps_wallets`, receipts | Internal `/payout/send-money`; charge bands, balance check, status persistence, callback refund. |
| Payout2 account/document/status routes | `payout_banks`, AEPS2 merchant registration; account, IFSC, PAN/documents | `/payout2/get-banks`, `/addaccount`, `/uploaddoc`, `/bankstatus`. |
| `payout2Sendmoney` | `payout_txns`; beneficiary and transfer fields | `/payout2/payoutbanklist`, `/sendmoney`; same wallet/status pattern. |

### AEPS

AEPS is a multi-stage provider workflow rather than a single form:

| Stage/routes | Models/tables | Provider and result |
|---|---|---|
| Registration/OTP/biometric KYC | `aeps_onboarding_registrations`, `aeps2_onboarding_registrations`, `union_bank_aeps_registrations` | Internal `/aeps/registration`, `/submit-otp`, `/kyc`, `/onboarding`, `/onboarding2`, and Union Bank endpoints. |
| Bank activation/authentication/location | Same onboarding records | Bank-specific endpoints for Fino, Airtel, Jio and Union Bank. Forms collect merchant, geo-location, identity, device/biometric data. |
| Cash withdrawal/balance/mini statement | `aeps_txns`, `aeps_wallets`, `receipts` | `/aeps/widthcash`, `/widthcash2`, `/unionbankwidth`; cash-withdraw success credits the separate AEPS wallet and records an AEPS transaction. |
| Status and callbacks | `AepsTxn`, onboarding records | Callback/status updates and commission calculations. |

Sensitive biometric/Aadhaar payloads are assembled in controllers. The replacement must minimize retention, encrypt required identity data, never log biometric payloads, and keep live adapters disabled until compliance and provider contracts are confirmed.

### PAN, LPG, LIC, and account opening

| Service route → method | Table/form/validation | Provider/result |
|---|---|---|
| `POST /pan-card/submit` → `PanCardController::submit` | `pan.apply`; PAN application type, personal/contact data and documents; `pan_card_txns` | `/nsdl/pan/submit`; applies service charge, wallet debit, record/receipt; incomplete and status endpoints reconcile later. |
| `POST /lpg/pay-bill` → `LpgController::payBill` | `lpg.index`; operator/state/district/distributor/customer/amount; `lpg_txns` | `/lpg/fetch-details`, `/pay-bill`, `/status`; wallet/commission pattern. |
| `POST /lic/pay-bill` → `LicController::payBill` | `lic.index`; policy/customer/amount; `lic_txns` | `/lic/fetch-details`, `/pay-bill`, `/status`; wallet/commission pattern. |
| Account/BC-agent routes → `AccountController` | `account_agents`, `account_details`, `kotakbank_bcajents`; extensive KYC/account fields | Internal `/bcagent`, `/open-account`, `/account-stats`, `/account-view`, `/useraccount-open`, `/download-passbook`, `/kotakbanksubmiapi`. |

### Catalog-only or non-core financial items

- Credit Card appears only through `OtherServiceController`/`other_services` as a configurable external service/link and employee permission label. No credit-card transaction table or complete payment workflow was found.
- Prime plan purchases debit wallet and write `prime_plan_txn`.
- Product purchases use `products`/`user_orders`; rewards use `rewards`/`get_user_rewards`.
- These can be cataloged in the new service model, but they are not approved live-provider integrations.

## Wallet, commission, receipt, and refund rules

### Wallet

- `users.wallet_balance` is the current-balance cache; `wallets` is the movement history.
- Available balance is `wallet_balance - lock_amount`.
- `debitEntry`, `creditEntry`, and `callbackRefund` read a balance, calculate a new balance, update `users`, then insert a wallet row.
- No SQL transaction, row lock, optimistic concurrency token, or idempotency key protects this sequence. Concurrent requests can lose updates or double-credit.
- AEPS has a second ledger-like table whose balance is recomputed as credits minus debits.

The new system must use an append-only journal, database transactions, per-account concurrency control, balanced postings, and idempotent external references. A cached account balance may be maintained only within the same transaction.

### Charges and commissions

- Charge/commission selection is by service, role, and inclusive amount band.
- Charge type is fixed or percentage. Different helpers inconsistently return `charge`, `principal + charge`, or add TDS to a fixed charge.
- Commission is fixed or percentage; TDS and GST are subtracted to produce net commission.
- Recharge also has a separate operator/role commission table and record table.
- Legacy code credits only the transacting user in most paths; it does not consistently distribute commission through the Laravel referral hierarchy.

The target design separates pricing rules, calculated assessment lines, and ledger postings and rejects overlapping effective bands.

### Receipts and reconciliation

- `receipts` is polymorphic and uses a short-name + random eight-digit number.
- Provider callbacks update service transaction status and may call `callbackRefund`.
- Several callbacks check for an existing wallet category before refunding, but the checks and provider reference fields vary by service.
- Admin failed-transaction refund searches wallet activity and writes a refund, then updates the service transaction.

The new design requires a unique provider event key, immutable event log, state-transition policy, one refund posting per original debit, and audit actor/reason.

## CRM reference analysis

### Inventory and purpose

CRM has 34 tables, 52 stored procedures/functions, and one view. Relevant row counts at inspection time included 207 `CMFREG`, 2,086 `CSPREG`, 2,152 `CSPUNIT`, 278 `DWUSER`, 277 `DWUSERGROUP`, 1,902 `PAYMENT`, and 102 `PAYMENTLOG` rows.

Key concepts:

- `CMFREG`: both CMF and CSF records. `USERTYPE=1` means CMF; `USERTYPE=2` means CSF. All 175 CSF rows have a non-root `PARENTID`; all 32 CMF rows are roots.
- `CSPREG`: CSP profile with `CSF_ID`, `CMF_ID`, `ASM_ID`, bank/KYC/operational fields.
- `CSPUNIT`: one CSP can participate in one or more unit types and has a separate lifecycle/status per unit.
- `DWUSER`, `DWGROUP`, `DWUSERGROUP`, `DWUSERAREA`: application identity, group membership, and geographic scope.
- `DWGROUP`: Admin, Ops, CMF, Area Sales Manager, Regional Head, and Customer Care groups.
- `REGIONMST → STATEMST → DISTRICTMST`: master geography. `STATEMST.ASM_IDS` and `DWUSERAREA.STATE_IDS` are comma-delimited legacy relations.
- `ASMMST`: area sales managers by region.
- `PAYMENT` and `PAYMENTLOG`: onboarding/operational payment records with component amounts and audit-like before/history rows; not a service wallet ledger.
- `BANKMST`: 53 bank master rows.
- `ENQUIRY`: public/contact enquiries.

### CRM hierarchy rules proven by stored procedures

- `usp_addcmf` inserts CMF and CSF into `CMFREG`, checks code/PAN within user type, and records `PARENTID`.
- `usp_listcmf` self-joins `CMFREG` from CSF to parent CMF.
- `usp_addcsp` stores both `CSF_ID` and `CMF_ID`, rejects duplicate active email/mobile/PAN, and adds selected unit rows to `CSPUNIT` transactionally.
- `usp_listcsp` joins CSP → CSF → CMF → ASM → district/state/region and unit status.
- `usp_adduser` maps user types 1/2 to CMF group, creates area mappings, and enforces one user per `(USER_TYPE, USER_TYPEID)` procedurally.

CRM itself declares primary keys on major masters but has no catalog foreign keys for the relevant hierarchy and almost no supporting indexes beyond primary keys. The replacement must encode the proven joins as foreign keys and indexed unique constraints.

## Legacy Data Source Comparison

### Correspondence

| Laravel/MariaDB | CRM | Relationship |
|---|---|---|
| `users`, `roles`, `sub_roles` | `DWUSER`, `DWGROUP`, `DWUSERGROUP`, `DWUSERAREA` | Both represent login/authorization. CRM also stores geographic scope and a typed link to CMF/CSF/ASM. |
| Laravel referral/created-by links | `CMFREG.PARENTID`, `CSPREG.CSF_ID`, `CSPREG.CMF_ID` | Laravel links are generic and weak; CRM expresses the required CMF → CSF → CSP business hierarchy. |
| `bank_accounts`, provider bank lists | `BANKMST` plus bank fields in CMF/CSP | CRM has an organizational bank master; Laravel has user payout/funding accounts and provider bank IDs. |
| No normalized geography | `REGIONMST`, `STATEMST`, `DISTRICTMST`, `ASMMST` | CRM is the better master/hierarchy source. |
| `fund_records`, `add_money_records` | `PAYMENT`, `PAYMENTLOG` | Superficially similar payments, but different purpose: Laravel funds wallets; CRM records channel/onboarding payment components. They must not be merged blindly. |
| Laravel KYC/onboarding tables | `CMFREG`, `CSPREG`, `CSPUNIT`, UIDAI/operator/station tables, `DOCUMENT` | Both hold onboarding data; CRM is broader for channel operations while Laravel is provider/service-specific. |

### Entities only in Laravel

- Financial service catalog and per-user service activation.
- Wallet and AEPS wallet activity.
- Recharge, BBPS, DMT, payout, AEPS, PAN, LPG, LIC transactions.
- Pricing/charge/commission bands and commission records.
- Provider settings, UPI fund requests, products, plans, rewards, receipts, support/content.

### Entities only in CRM

- Explicit CMF/CSF/CSP channel hierarchy and CSP multi-unit operational status.
- Regional/State/District/ASM and user-area assignments.
- Ops/Regional Head/Customer Care groups and group rights/options.
- CSP station/operator/verifier/UIDAI data and a centralized document store.
- Channel onboarding payment components and payment history.

### Structural differences

| Concern | Laravel | CRM | Target decision |
|---|---|---|---|
| User hierarchy | SH/MD/DT/RT roles plus referral links | CMF root, CSF child, CSP leaf; geographic/ops roles | Use CRM hierarchy semantics with explicit organization nodes and parent foreign keys. Keep RBAC separate from hierarchy. |
| Service definitions | Configurable services and provider variants | Unit types/statuses, not consumer financial service catalog | Use Laravel service concepts; model providers/adapters separately. |
| Transactions | One table per service and a wallet history | Generic onboarding payments plus log | Use a common transaction envelope plus typed detail/provider payload references and a separate double-entry ledger. |
| Wallet | Mutable user balance plus history; separate AEPS wallet | No equivalent service wallet | New append-only balanced ledger; do not derive from CRM payment. |
| Commission | Role/service/amount bands plus recharge operator rules | No equivalent financial commission engine | Normalize Laravel rules, add effective dates and non-overlap checks. |
| Master data | Provider/operator data, few geographic masters | Stronger bank/geographic/organizational masters | Seed geography/banks from CRM after quality checks; retain legacy IDs in mapping columns. |

### Conflicts and inconsistencies

1. The requested name says CMF → CSF → CSP; CRM physically stores CMF and CSF in one table distinguished by numeric type. The target will use a single organization-node hierarchy with explicit node type, not reproduce the overloaded table.
2. Laravel roles do not correspond one-to-one with CMF/CSF/CSP. Role controls actions; organization membership controls data scope. They remain separate.
3. CRM has no declared foreign keys for hierarchy even though stored procedures rely on the relationships. The target enforces them.
4. CRM stores comma-separated unit/state/ASM identifiers. The target uses bridge tables.
5. Laravel service status, provider status, and wallet status are conflated and misspelled. The target separates canonical transaction state from raw provider state.
6. Laravel settings contain secrets alongside display settings. The target stores only secret references/encrypted values and redacts audit/log output.
7. CRM stored procedures write to a separate `XPRESSO` database. That coupling is outside the new operational boundary and is not carried forward implicitly.
8. `usp_modifycmf` assigns `BACKENDCOORDINATORNAME` from the bank-account-holder parameter, which appears to be a legacy defect and must not be reproduced.

### Migration considerations

- Import CRM CMF/CSF/CSP and master data through staging tables, preserve `LegacySystem`/`LegacyId`, validate parent existence, and quarantine the one CSP observed with a missing CSF.
- Normalize comma-delimited CRM mappings before loading.
- Never migrate CRM or Laravel password strings as valid ASP.NET passwords. Provision/claim accounts and require a reset; Laravel bcrypt may be verified only in a tightly controlled one-time upgrader if explicitly approved.
- Do not migrate plaintext secrets from the dump. Rotate all provider, email, SMS, WhatsApp, gateway, and database credentials.
- Map every historical status through an explicit lookup, preserving original status text for audit.
- Reconcile Laravel cached wallet balances against ordered wallet rows before importing opening balances. Post one audited opening-balance journal transaction rather than copying old rows as live ledger entries.
- Provider transaction IDs and callback IDs become idempotency keys. Duplicate/colliding IDs go to exception handling.
- Aadhaar/biometric/document retention requires a separate compliance decision before migration.

## Requirements carried into the new application

1. CMF → CSF → CSP hierarchy with database-enforced parent type, organization-scoped access, and auditable lifecycle status.
2. Authentication with modern password hashing, short-lived access tokens, rotating refresh tokens, lockout, password reset, and audit records.
3. RBAC independent of hierarchy; initial roles include Platform Admin, CMF Admin/User, CSF Admin/User, and CSP User.
4. Configurable service catalog with categories, provider adapters, environments, global enablement, and per-CMF/CSF/CSP grants.
5. A child cannot receive a service its ancestors are not allowed; denials override grants.
6. Common transaction state machine: Created → Pending → Succeeded/Failed, with Reversed as an audited terminal correction and raw provider statuses retained.
7. Double-entry ledger with main/AEPS accounts, transactional balances, idempotent posting, holds/locked funds, reversals, and immutable entries.
8. Effective-dated, non-overlapping charge and commission rules with stored calculation snapshots.
9. Receipts, callback events, provider request metadata, and audit history without secrets or biometric payloads.
10. Live provider integrations remain disabled until the corresponding adapter requirements, credentials, compliance, and sandbox tests are approved.

## Named-service verification addendum

The following dedicated records make the requested service inventory explicit. They distinguish behavior verified in the read-only Laravel application/dump from behavior that was not found. DMT means Domestic Money Transfer and is treated as a shared capability; Fino, Airtel, and PPI behavior are provider-specific variants. `Fino DMT` is intentionally not treated as equivalent to Fino AEPS.

### Fino DMT

- Purpose: **UNKNOWN / REQUIRES CONFIRMATION**. Shared Domestic Money Transfer behavior exists; Fino is verified for AEPS, not DMT.
- Laravel route/controller/view: no Fino-DMT route found. Generic reference: `POST /dmt/transaction` → `DmtController::transaction` → `resources/views/dmt/index.blade.php`; API equivalents are `POST dmt-transaction`, status, and history in `routes/api.php`.
- Verified shared DMT fields: account number, IFSC, mobile, beneficiary name, amount, transfer type, comments. Controller validation is account 9–18 digits, IFSC `^[A-Z]{4}0[A-Z0-9]{6}$`, mobile 10 digits, amount 1000–10000, type IMPS/NEFT/RTGS, comments required.
- Database: generic `dmt_txns`, `dmt_registrations`, `dmt_beneficiaries`, `aeps_wallets`, `wallets`, `receipts`; no Fino-DMT-specific table or provider mapping verified.
- Provider/request/response: generic controller posts to internal `/dmt/send-money` with Basic Auth and an AEPS merchant code. Raw provider statuses observed are `success`, `pending`, and failure variants; exact Fino mapping is **UNKNOWN / REQUIRES CONFIRMATION**.
- Wallet/charges/commission/refund: shared DMT calculates a service charge, checks main wallet and AEPS wallet, debits on success, and persists pending/failed records; the implementation is not atomic and refund/callback details require confirmation. No Fino-DMT rule was found.
- Restrictions/history/receipt/errors: service activation is checked; an active AEPS onboarding record is required; recent history and receipt are rendered. Validation returns HTTP 422 JSON. Fino-specific rules are **UNKNOWN / REQUIRES CONFIRMATION**.

### AEPS

- Purpose: cash withdrawal, balance enquiry, mini statement, and onboarding/KYC across Fino, NSDL, Airtel, Jio, and Union Bank variants.
- Laravel routes/controllers/views: `AepsController`, `AepsController1`, API `UserController`; views under `resources/views/aeps/`. Fino routes include `/aeps/fino-kyc`, `/aeps2/fino-bank-active`, and bank-specific registration/authentication/cash-withdrawal flows.
- Inputs: onboarding identity, merchant, device/biometric, location, bank, and transaction fields vary by provider. Exact required fields are provider-stage dependent.
- Database/provider/wallet: `aeps_onboarding_registrations`, `aeps2_onboarding_registrations`, `union_bank_aeps_registrations`, `aeps_txns`, `aeps_wallets`, `receipts`; internal provider endpoints include registration/authentication/balance/mini-statement/cash-withdrawal. Sensitive biometric retention rules remain unresolved.

#### AEPS implementation verification update (21 August 2026)

The actual Laravel API branch was traced in `Api/UserController`: `balanceInquiry`, `miniStament` (legacy spelling), and `cashwidthdraw` are the verified transaction types. All three require `fingerData`, Aadhaar, mobile, `bankId`, and `deviceName`; cash withdrawal additionally requires `cashAmount`. The selected bank maps to Fino (`be`/`ms`/`cw`) or NSDL (`be3`/`ms3`/`cw3`) method names. An active `AepsOnboardingRegistration` is required. The transaction payload also includes the onboarding mobile and `accessmodetype`; latitude, longitude, and pincode were found on onboarding models, not required in this transaction branch.

For cash withdrawal, legacy success creates an `aeps_txns` row, issues a receipt, and credits `aeps_wallets`; failure creates a failed transaction/receipt without the success credit. Inquiry branches return provider data and do not show a verified wallet posting in the traced code. Legacy status strings include success/failure and provider response shapes also expose boolean data status; exact callback, status-check, refund/reversal, charge, and commission rules vary by provider and remain **UNKNOWN / REQUIRES CONFIRMATION**. The replacement uses canonical statuses, an atomic idempotent AEPS ledger credit for successful cash withdrawal, zero pricing until approved rules exist, and no live adapter.

### UPI Transfer

- Purpose/routes: no standalone route or service label named exactly UPI Transfer was found. UPI appears in `MoneyController` fund-request flows (`/add-money-upirequest` and `/add-fund-request`).
- Form/database/provider: amount, payer/reference/proof fields are used by `add_money_records`; TarangPay and CutePe order/poll/webhook integrations are referenced. Exact “UPI Transfer” business definition, wallet posting and user restrictions are **UNKNOWN / REQUIRES CONFIRMATION**.
- Direct trace: `MoneyController::addMoneyUpirequest` creates a pending `AddMoneyRecord` after a TarangPay `create-order` response; `addMoneystatus` polls `check-order-status` and credits `users.wallet_balance` when the provider status is `COMPLETED`. The API controller also contains `addFundRequest`, which sends a bearer-authenticated provider order request and creates a pending `AddMoneyRecord`.
- UI/provider semantics: `addmoneyupi.blade.php` and `addfundupi.blade.php` collect only `amount`. The TarangPay branch opens the provider response's `payment_url`; the second branch displays the configured `settings.qr_code` image and also opens a provider `payment_url` when returned. The second provider payload sends the logged-in user's name, email, and mobile as customer metadata, not as a beneficiary destination.
- Payment-method classification: no VPA/UPI-ID, mobile-linked destination, bank account/IFSC destination, collect address, intent URI, or account-routing field is accepted or constructed by the Laravel code. The hosted provider order/QR may encapsulate a provider-specific payment method, but its contract is not present in the source or dump, so the exact method is **UNKNOWN / REQUIRES CONFIRMATION**.
- Callback trace: `upifundWebhook` reads `data.status`, `data.client_txn_id`, and `data.udf1`; for `udf1=addfund`, `success` changes pending to approved and directly credits the user wallet, while `failure` changes pending to rejected. A second TarangPay webhook is commented out. Signature verification, replay protection, callback authentication, reversal/refund, and provider event identity are **UNKNOWN / REQUIRES CONFIRMATION**.
- No VPA/UPI-ID input or validation, beneficiary/customer identity workflow, outbound transfer, outbound wallet debit, charge/commission rule, outbound status-check, or standalone receipt/history contract was found. These are **UNKNOWN / REQUIRES CONFIRMATION** and are not inferred from the funding flow.
- Replacement boundary: the new `upi_transfer` service represents the verified legacy shape as `UPI Add Money`: amount-only hosted funding order, provider payment URL/optional QR, pending status, and wallet credit only after a verified provider success. It does not accept or store a destination VPA and does not implement outbound transfer semantics. It uses only a deterministic MOCK provider, zero unverified charges/commission, and atomic success-only wallet credit. LIVE remains disabled.

### CMS

- No service, route, controller, view, table, provider, or status flow named CMS was found in the inspected Laravel source or dump.
- Purpose, fields, validation, wallet impact, permissions, reporting, and receipt behavior: **UNKNOWN / REQUIRES CONFIRMATION**.

### Airtel DMT

- No Airtel-specific DMT route or provider adapter was found. Airtel is present in AEPS provider selection/onboarding code, not a verified DMT flow.
- All Airtel-DMT-specific fields, provider mapping, statuses, wallet, commission, and refund behavior: **UNKNOWN / REQUIRES CONFIRMATION**.

### AEPS King

- No service, route, controller, view, model, provider configuration, or database object named `AEPS King`, `AEPS-King`, or `King AEPS` was found in the inspected Laravel application or SQL dump.
- The legacy SQL service catalog contains separate entries for `aeps`, `aeps2`, and `unionbank`. Laravel also contains separate AEPS controllers, onboarding records, and provider branches for those variants. These are not evidence that any of them is AEPS King.
- The read-only `CRM` database was checked for service-related objects and values. It has no AEPS/service catalog table; the populated `dbo.CSPREG.SERVICEPROVIDER` value observed was `Airtel`, with no AEPS King value.

#### AEPS King verification update (21 August 2026)

The requested end-to-end workflow cannot be verified from the available legacy sources. There is no evidence for AEPS King transaction types, customer/Aadhaar inputs, bank/IIN selection, mobile or amount rules, biometric/device envelope, provider/API, validation, status mapping, wallet debit/credit, charges, commission, receipt/history shape, callback/status check, or reversal/refund behavior. The AEPS and AEPS2 evidence is retained under the separate `AEPS` entry above and is not reused as an AEPS King product definition.

Decision: **BLOCKED — do not implement guessed behavior**. No AEPS King service key, backend endpoint, Angular screen, provider adapter, database migration, or activation record has been created. Implementation requires an approved product specification and provider contract or representative request/response and callback samples, including wallet, pricing, settlement, and reversal rules. LIVE must remain disabled until those are verified.

### PAN Card

- Route/controller/view: `POST /pan-card/submit` → `PanCardController::submit` → `resources/views/pan/apply*`.
- Inputs: PAN application type, personal/contact data, and documents; exact field set is form/version dependent. Validation and document requirements must be taken from the selected view/request class at implementation time.
- Database/provider: `pan_card_txns`; internal `/nsdl/pan/submit` plus incomplete/status routes. Charge and wallet debit are verified; live provider status/refund details require sandbox/provider confirmation.

### Payment Gateway

- No standalone service named Payment Gateway was found. Gateway behavior is present in fund-request flows (`add_money_upirequest`, `add-fund-request`) using TarangPay/CutePe.
- Gateway fields, webhook signature verification, idempotency, wallet credit timing, refund and reconciliation requirements are not consistently implemented in legacy code and are **UNKNOWN / REQUIRES CONFIRMATION** for a replacement.

### DMT PPI

- No DMT PPI service, route, controller, view, table, or provider was found.
- Entire workflow and business rules: **UNKNOWN / REQUIRES CONFIRMATION**.

### Move to Bank

- No service named Move to Bank was found. Generic bank movement appears through DMT and Payout.
- DMT uses `dmt_txns`; Payout uses `payout_txns` and approved bank accounts with IMPS/NEFT/RTGS. Whether “Move to Bank” is a separate product or alias is **UNKNOWN / REQUIRES CONFIRMATION**.

### Fund Request

- Verified manual routes/controllers: authenticated `POST /add-money/request` → `MoneyController::addMoneyRequest`; admin review `fundRequestUpdate`; UPI hosted funding routes are separate and belong to `UPI Add Money`.
- Requester: an authenticated user can submit the manual form, but the page is shown only when the user has an approved `AddAccount` bank record (`status=success`). The source does not define CMF/CSF/CSP request eligibility or routing.
- Inputs: amount is required and numeric with minimum `1`; `txnId`/payment reference is required and globally unique; screenshot is required and accepts JPG/JPEG/PNG/GIF. The page displays configured bank name, account holder, account number, and IFSC and explicitly says only NEFT is accepted. No separate user-selected payment-mode field or verified amount maximum was found.
- Persistence/status: `add_money_records` stores user, amount, screenshot URL, `txnId`, and `pending|approved|rejected`; manual requests use the `manuly` type default. The user page lists the request history. No standalone legacy receipt was created in the traced manual request/approval path.
- Approval: `/fund-request/status/update` accepts `approved|rejected`; legacy admin access is provided by the admin route and employee permissions `pending_fund_request`/`all_fund_request`. Approval directly increments `users.wallet_balance`; rejection does not credit. The legacy update is not protected by a database transaction, row lock, idempotency key, or duplicate-approval guard.
- Cancellation/reversal and hierarchy: no manual cancellation or reversal route was found; no verified CMF → CSF → CSP approval rule, unrelated-branch rule, reviewer assignment rule, or post-approval reversal behavior was found. These remain **UNKNOWN / REQUIRES CONFIRMATION**.
- Replacement boundary: ProxyType implements the verified manual shape as a separate `fund_request` service: fixed server-controlled NEFT mode, amount/reference/proof, pending request, authorized hierarchy-scoped review, and atomic success-only wallet credit. The new maximum ₹1,000,000 and 5 MB proof limit are platform safety limits, not legacy claims. It does not reuse the hosted UPI provider flow and creates no LegacyID fields.

### Recharge

- Routes/controllers/views: `/v1/recharge/*` through `/v5/recharge/*`, using five controller variants and corresponding recharge views.
- Inputs: account, amount, operator/type; most versions also require pincode and geo-location. Amount is generally numeric and at least 10.
- Database/providers/status/wallet: `recharge_txns`, `RechargeCommissionRecords`, operator/commission tables; providers include internal Basic Auth proxy, RechargeExchange, MRobotics, and Bluecant. Success/pending/failed/refund callbacks are observed; legacy writes are not atomic.
- Verified operator/type catalog includes Airtel, BSNL Special Tariff, BSNL Talktime, BSNL Special LAPU, Google Play Voucher, Jio, Vi, Airtel Digital TV, Dish TV, Sun Direct, Tata Sky, and Videocon D2H. Legacy types are `mobile`, `dth`, `google`, and `lapu`.
- Verified v1 validation requires `account` string, `amount` numeric minimum 10, `operator`, `type`, six-digit `pincode`, numeric `lat`, and numeric `lng`. Invalid input returns HTTP 422 JSON.
- Success debits the wallet and records operator commission; pending debits the wallet without commission in the inspected v1 controller; failed provider responses create a failed transaction without a debit. Callback code indicates success/failure/refund reconciliation exists for selected variants, but exact reversal timing and atomic guarantees are **UNKNOWN / REQUIRES CONFIRMATION**.

### Wallet to Wallet

- Route/controller: `POST /wallet-transfer/transfer` → `WalletController::walletTransferSubmit` → wallet transfer form.
- Inputs/validation: recipient mobile, amount numeric and at least 10; self-transfer is blocked and the service must be active.
- Charge details: `serviceChargeAmounts` selects a role/service amount band and supports fixed or percentage charges. The inspected dump contains a zero fixed charge for the available wallet-transfer role range; complete production role-band mapping is UNKNOWN / REQUIRES CONFIRMATION. The replacement currently posts zero charge and does not invent a charge engine.
- Database/wallet/receipt: `wallet_to_wallet_txns`, `wallets`, `receipts`; sender debit and receiver credit are written as a successful transfer after charge/balance checks. The legacy multi-write flow lacks a database transaction/row lock; replacement must use balanced idempotent journals.
- Hierarchy: the legacy controller does not enforce CMF/CSF/CSP relationship scope. The replacement applies the CRM hierarchy rule: sender may select only active receivers in the sender's accessible descendant branch; unrelated branches are not exposed.

### Credit Card

- Only a catalog/external-link entry was found through `OtherServiceController`/`other_services` and employee permission labels.
- No transaction table, complete form, provider, wallet, commission, receipt, or refund workflow was found. All operational requirements are **UNKNOWN / REQUIRES CONFIRMATION**.
