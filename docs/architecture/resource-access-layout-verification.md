# Sidebar language and project/team layout verification

Date: 2026-10-12.

## Requested scope

Keep the left navigation in English. Restore dashboard and access-management
content to Turkish. Make projects and teams distinct, readable sections rather
than mixing their forms and records inside one tall card.

## Implementation

- Projects and teams each have their own labelled section, creation form, list
  and count. Project rows show their target counts; team rows show their own
  membership counts and link to team details.
- Member management occupies a separate full-width row. The selected team is
  explicit; Keycloak search and current members occupy separate columns.
- Permission grants and target/project assignment sit below, in two columns.
  The existing grant table, access preview and audit trail remain available.
- Narrow screens stack the cards and form controls. Long names/IDs wrap, and
  the capped project list supports keyboard scrolling and a visible focus ring.
- Dashboard, authentication feedback, access-management forms, details, preview
  and audit copy are Turkish again. Content uses `lang="tr"` and `tr-TR` dates;
  the English sidebar retains `lang="en"`.
- Human-facing API copy changed by the earlier translation is restored. Routes,
  role/audit/error keys, permission bits, tenant guards and request payloads are
  unchanged. Existing pre-translation validation messages are out of scope.

Projects group targets; teams group users. This layout does not introduce a new
project/team ownership relationship. Access still comes from explicit grants,
bounded by application roles. Real names and Keycloak IDs are not translated.
Temporary-password user creation and production email capability are preserved.

## Verification

- Web rendering/contract regression suite: 27 passed, 0 failed, 0 skipped.
  The original 23 cases now assert Turkish content and English navigation.
  Four additional cases cover independent sections/forms/lists, retained
  identifiers/membership bindings, per-card counts and empty installation states.
- Web lint, standalone TypeScript check and production build: passed.
- Release solution build: 0 errors, 0 warnings.
- Application/API tests: 191 passed, 0 failed, 0 skipped.
- Repository whitespace check: passed.

Live checks were performed after the user signed in to the in-app browser:

- Desktop viewport 1440 × 1000: project/team cards share the first row; member
  management spans the content width; permission/target cards share the next row.
- Mobile viewport 390 × 844: all five cards stack; document scroll width equals
  viewport width (390), with no horizontal page overflow.
- Keycloak search for the existing administrator returned the real account and
  ID. Selecting it displayed that same ID and enabled the add-member button;
  clearing the search cleared selection and disabled submission again.
- Access Management displayed Turkish content while its sidebar label remained
  English, and the live user directory loaded successfully.

No user, project, team, membership, grant or target was created, removed or
changed during browser verification. No credentials were read. The API/AppHost
and persistent resources were not restarted; compiled API wording changes take
effect at their next normal restart. Infrastructure/ops suites were not rerun
for this presentation-only correction.

## Repeat automated checks

```bash
npm --prefix src/DbScanner.Web test
npm --prefix src/DbScanner.Web run lint
src/DbScanner.Web/node_modules/.bin/tsc --noEmit -p src/DbScanner.Web/tsconfig.json
npm --prefix src/DbScanner.Web run build
dotnet build DbScanner.sln -c Release --no-restore --disable-build-servers -m:1 /nodeReuse:false
dotnet test tests/DbScanner.Application.Tests/DbScanner.Application.Tests.csproj -c Release --no-build --no-restore
```

No dependency installation, migration, Git commit or push was performed.
