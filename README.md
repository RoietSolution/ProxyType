# ProxyType

ProxyType is a clean-room ASP.NET Core 9 and Angular 20 implementation derived from three read-only legacy sources. Its operational store is SQL Server 2022 database `proxytype_DB`; the Laravel application, MariaDB dump, and `CRM` database remain unchanged references.

## Repository layout

- `docs/Legacy-Service-Requirements.md` — verified legacy behavior and source comparison
- `docs/New-Architecture.md` — target boundaries, hierarchy, security, ledger, and migration decisions
- `database/001_initial.sql` — repeatable SQL Server schema and catalog seed
- `database/verify.sql` — rollback-only hierarchy, permission, and journal integrity checks
- `src/ProxyType.Api` — ASP.NET Core 9 API
- `src/proxytype-web` — Angular 20 portal
- `tests/ProxyType.Api.Tests` — hierarchy and permission tests

## First run

The database has already been deployed locally. The API loads secrets through ASP.NET Core configuration. During development, store them in User Secrets; in production, supply them as environment variables. Never add them to `appsettings.json`.

For development, from the repository root:

```powershell
dotnet user-secrets set "Jwt:SigningKey" "<at-least-32-random-bytes>" --project src\ProxyType.Api\ProxyType.Api.csproj
dotnet user-secrets set "PROXYTYPE_BOOTSTRAP_ADMIN_PASSWORD" "<one-time-password-at-least-14-characters>" --project src\ProxyType.Api\ProxyType.Api.csproj
dotnet run --project src\ProxyType.Api\ProxyType.Api.csproj
```

For production, set `Jwt__SigningKey` and `PROXYTYPE_BOOTSTRAP_ADMIN_PASSWORD` in the process environment before starting the API:

```powershell
$env:Jwt__SigningKey = '<at-least-32-random-bytes>'
$env:PROXYTYPE_BOOTSTRAP_ADMIN_PASSWORD = '<one-time-password-at-least-14-characters>'
dotnet run --project src\ProxyType.Api\ProxyType.Api.csproj
```

On the first run only, this provisions `platform.admin`. Remove `PROXYTYPE_BOOTSTRAP_ADMIN_PASSWORD` immediately afterward. The account is flagged for a mandatory password change through `POST /api/auth/change-password`, which revokes all existing refresh tokens.

In a second terminal:

```powershell
cd src\proxytype-web
npm ci
npm start
```

Open `http://localhost:4200`. The development proxy targets `https://localhost:7248`.

## Verification

```powershell
dotnet build src\ProxyType.Api\ProxyType.Api.csproj -c Release
dotnet test tests\ProxyType.Api.Tests\ProxyType.Api.Tests.csproj -c Release
cd src\proxytype-web
npm run build
npm test -- --watch=false --browsers=ChromeHeadless
```

Provider credentials and live routes are intentionally absent. A provider must remain disabled until its traced requirements, secret references, callback verification, idempotency, reconciliation, and certification are implemented.

## Financial-service development

The legacy service evidence is maintained in [Legacy-Service-Requirements.md](docs/Legacy-Service-Requirements.md). Implementation progress is tracked in [Service-Implementation-Status.md](docs/Service-Implementation-Status.md). The first new service slice is documented in [Fino-DMT.md](docs/services/Fino-DMT.md).

Apply the initial database deployment followed by the idempotent Fino DMT deployment before using the financial APIs:

```powershell
sqlcmd -S DESKTOP-DQ0868S -E -C -i database\001_initial.sql
dotnet run --project tools\DbProbe\DbProbe.csproj -- proxytype_DB @database\002_fino_dmt.sql
```

Fino DMT defaults to `MOCK` only in Development. Production defaults to disabled LIVE mode unless `FinoDmt:ProviderMode` is explicitly configured. The mock provider never calls an external financial provider; `FinoDmt:MockResult` can be `SUCCESS`, `PENDING`, or `FAILED` for deterministic development checks.
