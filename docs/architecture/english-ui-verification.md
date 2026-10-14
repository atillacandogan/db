# Dashboard and access management — English UI verification

Date: 2026-10-11.

This is a historical report. The user subsequently restricted English to the
sidebar and requested separate project/team cards. The content translation and
layout below have been superseded by
[the sidebar-only/layout verification](resource-access-layout-verification.md).

## Changes

- Sidebar and breadcrumbs: `Access Management`, `Teams and Target Permissions`.
- Dashboard: summaries, coverage, links, loading/empty/error states and dates.
- User management: forms, filters, role display labels, account actions, feedback
  and tenant-specific restrictions.
- Team/target management: real Keycloak user search, IDs, permissions, team detail,
  effective access preview, audit labels and feedback.
- Shared sign-in/sign-out, access-denied and notification dismissal text.
- API access errors, preview explanations and user-management validation text.

Dates use `en-US`. English sections have `lang="en"`, including loading/error
states; unrelated Turkish pages retain the existing root language. Real user,
project and team names are not translated. User initials retain the existing
Turkish-aware casing. Routes, role/error/audit keys, permission bits, request
payloads and authorization rules are unchanged.

The POC still creates users with a temporary password and requires password
change on first sign-in. SMTP/invitation functionality for future production use
was not deleted or enabled by this change.

## Verification

- Release solution build: 0 errors, 0 warnings after API/service text changes.
- Application/API: 191 passed after those changes.
- Infrastructure: 174 passed, 1 skipped in one full isolated Docker run. No
  Infrastructure source changed in this task.
- Python ops/repository safety: 37 passed.
- New Web rendering/contract regression tests: 23 passed.
- Web lint, standalone TypeScript check and production build: passed.
- Total automated tests: 425 passed, 0 failed, 1 skipped. The skipped existing
  SQL Server/Nmap integration test requires host Nmap, which is not installed.

The Web tests render the actual components with mocked query/auth fixtures.
They cover English copy and accessibility labels, admin-only navigation and
route guards, tenant restrictions, retained Keycloak IDs/names, audit keys and
the unchanged temporary-password/role request payloads. They do not perform
browser interactions or make live API/Keycloak requests.

Production HTTP smoke test on temporary `127.0.0.1:3300`:

- `/dashboard`, `/admin/users`, `/admin/resource-access` and a team-detail route
  returned 200 with the English authentication boundary.
- A nonexistent route returned 404.
- The temporary server was stopped after testing.

The normal Web port 3000 and Keycloak port 18080 were not responding. Therefore
an authenticated, live end-to-end sign-in/user-management test was not performed.
AppHost/Keycloak and persistent data were not restarted, provisioned or changed.
The new build must be used when restarting the application.

## Repeat the checks

```bash
npm --prefix src/DbScanner.Web test
npm --prefix src/DbScanner.Web run lint
npm --prefix src/DbScanner.Web run build
dotnet build DbScanner.sln -c Release --no-restore --disable-build-servers -m:1 /nodeReuse:false
dotnet test tests/DbScanner.Application.Tests/DbScanner.Application.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/DbScanner.Infrastructure.Tests/DbScanner.Infrastructure.Tests.csproj -c Release --no-build --no-restore
python3 -m unittest discover -s tests/ops -v
```

No dependency installation/upgrade/audit, Git commit or push was performed.
