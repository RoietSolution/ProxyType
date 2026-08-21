# ProxyType Target Architecture

## Solution shape

```text
Angular 20 SPA
    │ HTTPS / JSON
ASP.NET Core 9 API
    ├── Authentication and RBAC
    ├── Organization hierarchy and scope policy
    ├── Service catalog and permission policy
    ├── Finance/ledger boundary
    ├── Provider adapter boundary (disabled by default)
    └── Audit and reconciliation
        │
SQL Server 2022 / proxytype_DB
```

The initial implementation deliberately covers authentication, CMF → CSF → CSP hierarchy, configurable services, inherited service permissions, login, and dashboard. Financial provider adapters are represented in the schema/catalog but are not activated.

## Boundaries

- **Identity** owns users, roles, refresh tokens, lockout, and login audit.
- **Organization** owns typed CMF/CSF/CSP nodes and memberships. Authorization always combines a role permission with the caller's organization scope.
- **Catalog** owns services, providers, provider routing, price/commission rules, and organization service grants.
- **Finance** owns service transaction envelopes, fund requests, ledger accounts, balanced journals, assessments, and receipts.
- **Integration** owns idempotent provider callback/event records and redacted external-call metadata.
- **Audit** owns append-only actor/action records.
- **Master** owns region/state/district/bank data and legacy source identifiers.

## Hierarchy model

`org.OrganizationUnit` is an adjacency list with an explicit `UnitType`:

- `PLATFORM` has no parent.
- `CMF` is a child of `PLATFORM`.
- `CSF` is a child of `CMF`.
- `CSP` is a child of `CSF`.

A SQL trigger rejects any other parent/type combination and prevents self-parenting. API scope queries use a recursive CTE so a CMF can see its CSFs/CSPs, a CSF can see itself and its CSPs, and a CSP sees only itself.

Users and organization units are not the same record. A user may hold one or more memberships; roles control actions while memberships control data scope.

## Service permission model

`catalog.OrganizationServicePermission` stores explicit `ALLOW` or `DENY` decisions for an organization node and service. Effective access is calculated over the node's ancestor chain:

1. The service itself must be globally active.
2. Any explicit denial from platform/CMF/CSF/CSP denies access.
3. A CSP requires an allow at every commercial ancestor in its path once the service is delegated below platform.
4. An explicit child allow cannot bypass a parent denial.

The initial API exposes effective results with the source node and reason. Mutation endpoints validate that an allow is supported by the parent's effective permission.

## Authentication and security

- ASP.NET `PasswordHasher<TUser>` (PBKDF2) rather than migrated legacy hashes.
- JWT access token with issuer/audience/signing-key validation and short lifetime.
- Refresh tokens are random, stored only as SHA-256 hashes, rotated on use, and revoked as a family on reuse.
- Failed-login counter and timed lockout.
- Password-reset/forced-change fields are modeled; bootstrap admin creation requires an environment variable and sets `MustChangePassword`.
- CORS uses configured origins; production configuration must not use wildcards.
- API errors use problem details and do not return secrets or database details.
- Provider secrets are not stored in service/settings rows. `SecretReference` points to an external secret store or protected configuration.
- Audit records include actor, scope, action, entity, correlation ID, IP, and JSON details with secret redaction.

## Ledger design

- `finance.LedgerAccount` represents an asset/liability/revenue/expense/clearing account and includes a concurrency rowversion.
- `finance.JournalTransaction` is the immutable business transaction header with an idempotency key.
- `finance.JournalEntry` contains debit/credit postings; amounts are positive and exactly one side is populated.
- Posting occurs through a stored procedure that verifies total debits equal credits before marking the journal posted and adjusting cached account balances in the same SQL transaction.
- Reversal uses a new journal linked to `ReversalOfJournalId`; posted entries are never edited or deleted.
- Holds/locked funds are separate ledger accounts or explicit hold records, not a mutable subtraction field on a user.

## Transaction and provider state

Canonical service transaction states are `CREATED`, `PENDING`, `SUCCEEDED`, `FAILED`, `REVERSED`, and `CANCELLED`. `ProviderStatus` stores the raw value without driving business rules directly.

Provider callbacks require `(ProviderId, ExternalEventId)` uniqueness. Processing records the payload hash, timestamps, outcome, and linked transaction. Replayed callbacks return the original outcome and cannot post another debit/refund.

## Database deployment

- `database/001_initial.sql` is an idempotent SQL Server 2022 deployment script.
- It creates `proxytype_DB`, schemas, tables, constraints, indexes, hierarchy/pricing triggers, ledger posting procedure, service/hierarchy seed data, and schema version record.
- CRM and Laravel identifiers are retained only through nullable `LegacySystem`/`LegacyId` mapping columns during future staged import.
- Application startup does not modify CRM or either Laravel source.

## Initial API surface

| Area | Endpoint |
|---|---|
| Authentication | `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout`, `GET /api/auth/me` |
| Dashboard | `GET /api/dashboard` |
| Hierarchy | `GET /api/organizations/tree`, `POST /api/organizations`, `GET /api/organizations/{id}/users` |
| Services | `GET /api/services`, `POST /api/services`, `PUT /api/services/{id}` |
| Permissions | `GET /api/organizations/{id}/services`, `PUT /api/organizations/{id}/services/{serviceId}` |

Provider transaction endpoints are intentionally absent from the initial live surface.

## Angular application

- Angular 20 standalone components with strict TypeScript.
- Login form with reactive validation, password visibility control, safe generic authentication errors, loading state, and session persistence.
- Responsive dashboard with hierarchy summary, service availability, organization scope, recent security activity, and a role-aware navigation shell.
- HTTP interceptor adds the access token and performs one refresh-and-retry cycle for expired tokens.
- Route guard protects dashboard/admin routes.
- No provider secret, password, or refresh token is written to logs. Refresh token storage will move to a secure HttpOnly cookie before public deployment; the initial local implementation keeps the response contract isolated behind `AuthService`.

## Verification strategy

- SQL catalog checks for database/schema/table/constraint creation.
- API unit tests for hierarchy parent rules, permission inheritance/denial, password verification, and token rotation helpers.
- API integration smoke test against `proxytype_DB` using a transaction or isolated records.
- Angular production build and component/service tests.
- Security checks for anonymous rejection, cross-hierarchy access, parent-deny enforcement, duplicate callback/idempotency constraints, and redacted configuration.
