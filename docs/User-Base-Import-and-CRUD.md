# User base import and CRUD

## CRM import result

The read-only CRM `dbo.DWUSER` source contained 278 records, of which 12 had `USER_STATUS = 1`. All 278 were imported into `proxytype_DB` using their CRM `USER_ID` as the repeatable source key. Source user type, group, region, and state metadata is preserved in `auth.LegacyUserImportMap`.

CRM password values are never copied. Every imported account has a newly generated non-disclosed password hash and `MustChangePassword = 1`; an administrator must reset the password before onboarding. The CRM hierarchy was imported from `CMFREG` and `CSPREG`: 32 CMF units, 175 CSF units, and 2,085 CSP units. Each imported hierarchy record has a corresponding reset-required ProxyType user and organization membership. The target hierarchy has no orphaned imported units, and every imported CSP is below a CSF as required by the operational schema.

One CRM CSP record (`CSP_ID=192`) has `CSF_ID=0` and `CMF_ID=1`. Because the target schema requires `PLATFORM -> CMF -> CSF -> CSP`, it was not assigned to an invented CSF and remains an explicit unresolved import exception.

Run the importer with Windows Integrated Security:

```powershell
dotnet run --project tools/CrmUserImport/CrmUserImport.csproj
dotnet run --project tools/CrmUserImport/CrmUserImport.csproj -- --apply
```

The `--apply` operation is idempotent for CRM users, hierarchy units, and memberships. It does not copy CRM passwords or print generated passwords.

Optional connection overrides use `PROXYTYPE_CRM_CONNECTION` and `PROXYTYPE_DB_CONNECTION`; do not commit them.

## CRUD and filtering API

- `GET /api/users?search=&active=&unitType=CMF|CSF|CSP&organizationUnitId=&page=&pageSize=` — filtered, paginated users within the caller's hierarchy. Default page size is 50; supported sizes are 25, 50, and 100. Platform administrators can see unassigned imported users.
- `GET /api/users/{userId}` — details view data for one authorized user.
- `POST /api/users` — create a user with role and organization assignment.
- `PUT /api/users/{userId}` — update contact, active state, role, and organization assignment.
- `DELETE /api/users/{userId}` — soft-disable a user; self-disable is rejected.
- `POST /api/users/{userId}/reset-password` — set a new password and require a change on next login.

Only platform, CMF, and CSF administrators can use these endpoints. Hierarchy scope is enforced server-side; Angular visibility is not an authorization boundary.

The dashboard menu provides separate CSP List, CSF List, and CMF List entries. A CMF/CSF administrator can see permitted lower-level users through the same descendant-scope query; unrelated branches are excluded.
