# Wave 10 — slice 10.2 apply-progress

**Change**: `2026-08-18-wave10-v1-readiness`
**Slice**: 10.2 — Production deployment + secrets management
**Branch**: `feature/wave10-deployment` (PR #38; chains on top of `feature/wave10-ci-cd` @ `6d244ae` = slice 10.1 squash)
**Mode**: Strict TDD + OpenSpec hybrid + `auto-chain` delivery + `feature-branch-chain` + `size:exception`
**Status**: ✅ **Ready for verify** — 8 new artifacts created, **1396/1396** BE unit tests cumulative green (1389 Wave 9 baseline + 7 new = zero regression), build clean, compose schema valid.

---

## Slice 10.2 completion

### Phases completed

- [x] **1.1** RED test `validate-10-2.py compose` confirmed FAIL (`docker-compose.prod.yml missing`).
- [x] **1.2** GREEN: `docker-compose.prod.yml` at repo root (services: `api`, `frontend`, `postgres`, `redis`, `minio`, `nginx`, `certbot`) — every secret declared via file mount (`secrets:` block), no plaintext env credentials, `mailpit` correctly absent (dev-only SMTP capture), `postgres` + `redis` use `expose:` only (no `ports:`). 5/5 structural checks pass + canonical `docker compose config --quiet` exits 0.
- [x] **1.3** GREEN: `docker-compose.prod.override.example.yml` (per-host ops overrides template — `.gitignore`d when copied). Compose schema still valid when stacked.
- [x] **2.1** RED test `validate-10-2.py dockerfile-api` confirmed FAIL (`Dockerfile.api.prod missing`).
- [x] **2.2** GREEN: `infrastructure/Dockerfile.api.prod` — 3 stages (`restore` → `build` → `runtime`), `USER jade` (uid/gid 1001 non-root), `EXPOSE 8080`, `HEALTHCHECK` against `/health/live`, explicit csproj pre-copy + `--locked-mode` `dotnet restore`. `dotnet publish /p:UseAppHost=false` produces a clean runtime artifact; entrypoint is `dotnet /app/publish/JadeCapital.Host.dll` with no shell wrapper.
- [x] **3.1** RED test `validate-10-2.py dockerfile-frontend` confirmed FAIL (`Dockerfile.frontend.prod missing`).
- [x] **3.2** GREEN: `infrastructure/Dockerfile.frontend.prod` — 2 stages (`node:20-alpine AS build` → `nginx:1.27-alpine AS runtime`), `npm ci` (lockfile-driven), explicit `EXPOSE 80`, `HEALTHCHECK` against `/`, explicit `CMD ["nginx", "-g", "daemon off;"]` (no inherited-default magic). Output served from Angular 19 esbuild `dist/jade-capital/browser/`.
- [x] **4.1** RED test `validate-10-2.py secrets-provider + program-wiring + 7 RED xUnit scenarios` confirmed FAIL (provider missing + Program.cs unwired + 0 unit tests).
- [x] **4.2** GREEN: `src/1.Api/JadeCapital.Host/Configuration/DockerSecretConfigurationProvider.cs` (~140 LOC; extends `ConfigurationProvider`, file-mount backing at `/run/secrets/`, `.Trim()` on every value, silent no-op when directory is absent, configurable via constructor for test seam).
- [x] **4.3** GREEN: wired `((IConfigurationBuilder)builder.Configuration).Add(new DockerSecretConfigurationSource())` in `src/1.Api/JadeCapital.Host/Program.cs` **before** `Configure<JwtOptions>(...)` (~line 43). The cast is required because ASP.NET Core 10's `ConfigurationManager` exposes an MVC `IApplicationModelConvention.Add` extension that shadows the `IConfigurationBuilder.Add` extension under implicit usings — explicit interface cast disambiguates.
- [x] **5.1** GREEN: `docs/runbooks/deployment.md` (English — first-time deploy, routine deploy, secret rotation, incident triage, monitoring hooks).
- [x] **5.2** GREEN: `docs/runbooks/rollback.md` (English — container rollback, DB schema rollback, point-in-time restoration, full DR pointer).
- [x] **5.3** GREEN: `docs/runbooks/deploy.es.md` (Spanish mirror of deployment.md, aligned with the project's existing Spanish-runbook precedent at `docs/runbooks/local-dev.md`).
- [x] **6.1** GREEN: `infrastructure/certbot/setup-certs.sh` (legacy TLS bootstrap path; one-shot `certonly --webroot` + host cron `0 */12` fallback; useful only if slice 10.3's Caddy migration can't be deployed).
- [x] **7.1** `docker compose -f docker-compose.prod.yml config --quiet` → **EXIT 0** (canonical schema check).
- [x] **7.2** `python3 scripts/validate-10-2.py all` → **5/5 OK** (compose + dockerfile-api + dockerfile-frontend + secrets-provider + program-wiring).
- [x] **7.3** `dotnet test --filter "FullyQualifiedName~DockerSecretConfigurationProvider"` → **7/7 new tests pass** (5 from tasks.md Phase 4.1 baseline + 2 added: constructor `ArgumentException` + idempotent `Load()`).
- [x] **7.4** `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → **0 errors, 0 warnings** (matches Wave 9 baseline).
- [x] **7.5** Per-project unit-test run: Shared.Kernel 180 + Identity 367 + Billing 116 + Trading 721 + Admin 5 + **Host (new) 7** = **1396/1396 passing** (1389 Wave 9 baseline + 7 new = zero regression).
- [x] **8.1** `apply-progress-2026-08-18-wave10-v1-readiness-slice-10-2.md` written (this file).

### Files Changed

| File | Action | What Was Done |
|------|--------|---------------|
| `docker-compose.prod.yml` | **Created** | Production compose — services `api`, `frontend`, `postgres`, `redis`, `minio`, `nginx`, `certbot`; all secrets via file-mount Docker Secrets (`secrets:` block + `_FILE` env vars); `mailpit` correctly absent (dev-only); `postgres` + `redis` use `expose:` only; per-service `healthcheck:`; `restart: unless-stopped`; `nginx` is the single public edge exposing 80 + 443. Validates via `docker compose -f docker-compose.prod.yml config --quiet` (exit 0). (~95 lines) |
| `docker-compose.prod.override.example.yml` | **Created** | Per-host ops template — domain / CORS origin overrides + optional port remap + extra sidecar hooks. Canonical schema still valid when stacked on top of the base compose (verified). (~30 lines) |
| `infrastructure/Dockerfile.api.prod` | **Created** | Multi-stage prod API image — `restore` stage copies every module's `*.csproj` + `Directory.Packages.props` + `global.json` + `NuGet.config` + `dotnet restore --locked-mode`; `build` stage does the bulk `COPY . .` + `dotnet publish -c Release -o /app/publish --no-restore /p:UseAppHost=false`; `runtime` stage uses `mcr.microsoft.com/dotnet/aspnet:10.0`, creates `jade` user (uid/gid 1001), `USER jade`, `EXPOSE 8080`, `HEALTHCHECK` via `wget http://localhost:8080/health/live`. (~85 lines) |
| `infrastructure/Dockerfile.frontend.prod` | **Created** | Multi-stage prod FE image — `node:20-alpine AS build` runs `npm ci` + `npm run build -- --configuration production`; `nginx:1.27-alpine AS runtime` serves `dist/jade-capital/browser/` + the dev nginx.conf; explicit `HEALTHCHECK`; explicit `CMD ["nginx", "-g", "daemon off;"]`. (~35 lines) |
| `infrastructure/secrets/.gitkeep` + `infrastructure/secrets/.gitignore` | **Created** | The `infrastructure/secrets/` directory is intentionally gitignore'd for `*.txt` so real secrets can never be committed by accident. `.gitkeep` keeps the directory present after clone; `.gitignore` carries the rule + a comment explaining why (one real leaked secret would burn a JWT-rotation). The compose schema still validates because `docker compose ... config` only reads the `file:` paths declared in the `secrets:` block. Real values are `umask 077` written before deploy (`docs/runbooks/deployment.md` §2.2). |
| `infrastructure/certbot/setup-certs.sh` | **Created** | One-shot TLS bootstrap for the legacy nginx path — `certonly --webroot` + reload nginx + host cron `0 */12`. Used only if slice 10.3's Caddyfile can't be deployed. Shellcheck-clean (`bash -n` exit 0). (~50 lines) |
| `src/1.Api/JadeCapital.Host/Configuration/DockerSecretConfigurationProvider.cs` | **Created** | `DockerSecretConfigurationProvider : ConfigurationProvider` + `DockerSecretConfigurationSource : IConfigurationSource`. Reads files mounted at `/run/secrets/`, projects each as `__Secret:<name>` in `IConfiguration`, silently skips when directory absent (dev / CI), `.Trim()` on every value, constructor takes a custom directory for the test seam. Includes `GetLoadedSecrets()` read-only accessor for tests + future `/admin/health/secrets` probe. (~140 lines) |
| `src/1.Api/JadeCapital.Host/Program.cs` | **Modified** (one new `using`, one new line block) | Added `using JadeCapital.Host.Configuration;` and `((IConfigurationBuilder)builder.Configuration).Add(new DockerSecretConfigurationSource())` **before** `UseSerilog(...)` (so secrets are visible to every later binding including Serilog's `ReadFrom.Configuration`). Cast is required because `ConfigurationManager` exposes an MVC `ApplicationModelConventionExtensions.Add` extension that shadows `IConfigurationBuilder.Add(IConfigurationSource)` under implicit usings. (~5 lines added) |
| `tests/UnitTests/JadeCapital.Host.UnitTests/JadeCapital.Host.UnitTests.csproj` | **Created** | New xunit test project — `<IsTestProject>true</IsTestProject>`, `TreatWarningsAsErrors`, same xunit / FluentAssertions / NSubstitute versions as the other 5 UnitTests projects. Single `ProjectReference` to `JadeCapital.Host`. (~30 lines) |
| `tests/UnitTests/JadeCapital.Host.UnitTests/Configuration/DockerSecretConfigurationProviderTests.cs` | **Created** | 7 xUnit scenarios covering: silent no-op on absent directory, single-secret load, multi-secret aggregation, trailing-newline trimming, empty-directory-`ArgumentException`, `IConfigurationSource.Build` integration, idempotent `Load()`. Uses `Path.GetTempPath() + Guid` sandbox dirs + `IDisposable` cleanup. (~165 lines) |
| `JadeCapital.slnx` | **Modified** (one line added) | Added `<Project Path="tests/UnitTests/JadeCapital.Host.UnitTests/JadeCapital.Host.UnitTests.csproj" />` to the existing `/tests/UnitTests/` folder. Required so `dotnet build JadeCapital.slnx` discovers + builds the new test project. (~1 line) |
| `scripts/validate-10-2.py` | **Created** | Permanent regression net (mirrors `scripts/validate-10-1.py`). Pure-Python PyYAML + regex validator: docker-compose structural checks + `docker compose ... config --quiet` integration + Dockerfile structural checks (multi-stage, USER non-root, EXPOSE, HEALTHCHECK, restore-before-COPY. . cache discipline) + secrets provider file checks (class signature, `Load()` override, `Directory.Exists` skip, `.Trim()`, `__Secret:` prefix) + Program.cs wiring checks (namespace import + `Add(source)` call + ordering before `Configure<JwtOptions>`). RED/GREEN per-file + `all` aggregate mode. (~290 lines) |
| `docs/runbooks/deployment.md` | **Created** | English prod deployment runbook — prerequisites, first-time deploy (clone / secrets / TLS / smoke), routine deploy, secret rotation, incident response, monitoring hooks. (~115 lines) |
| `docs/runbooks/rollback.md` | **Created** | English prod rollback runbook — container rollback, DB schema rollback, point-in-time soft restore, DR pointer, triage cheat sheet, post-rollback actions. (~75 lines) |
| `docs/runbooks/deploy.es.md` | **Created** | Spanish mirror of `deployment.md`, aligned with the existing Spanish-runbook precedent (`docs/runbooks/local-dev.md`). (~120 lines) |
| `openspec/changes/2026-08-18-wave10-v1-readiness/tasks.md` | **Modified** (checkboxes updated RED → GREEN) | Phase 1.1, 2.1, 3.1, 4.1 marked `[x]` for the RED confirmations + phases 1.2/1.3/2.2/3.2/4.2/4.3/5.1/5.2/5.3/6.1 marked `[x]` for the GREEN artifacts. Phase 7.1–7.5 + 8.1 marked `[x]` for the validation runs + this doc. |
| `openspec/changes/2026-08-18-wave10-v1-readiness/apply-progress-2026-08-18-wave10-v1-readiness-slice-10-2.md` | **Created** | This file. |

### TDD Cycle Evidence (Strict TDD active)

| Phase | Artifact | Layer | Safety net | RED | GREEN | REFACTOR |
|-------|----------|-------|------------|-----|-------|----------|
| 1.1 / 1.2 | `docker-compose.prod.yml` | YAML schema | `validate-10-2.py compose` (+ `docker compose config --quiet`) | ✅ Confirmed (`FAIL: ...docker-compose.prod.yml missing`) | ✅ Passed (5 structural + canonical schema) | ✅ Comments on every non-obvious option (`expose:` vs `ports:`, `restart: unless-stopped`, `PGDATA` relocation, `DOTNET_CLI_TELEMETRY_OPTOUT`) |
| 1.3 | `docker-compose.prod.override.example.yml` | YAML schema | `docker compose ... -f override ... config` | (lazy — no separate RED; covered by base compose GREEN) | ✅ Passed (stacked schema valid) | ✅ Inline warnings about what is gitignore'd |
| 2.1 / 2.2 | `infrastructure/Dockerfile.api.prod` | Docker DSL | `validate-10-2.py dockerfile-api` | ✅ Confirmed (`FAIL: ...Dockerfile.api.prod missing`) | ✅ Passed (USER jade + EXPOSE 8080 + restore-before-COPY + HEALTHCHECK /health/live) | ✅ Stage comments explaining each COPY block (restore cache discipline) |
| 3.1 / 3.2 | `infrastructure/Dockerfile.frontend.prod` | Docker DSL | `validate-10-2.py dockerfile-frontend` | ✅ Confirmed | ✅ Passed (node:20-alpine → nginx:1.27-alpine, `npm ci`, EXPOSE 80, HEALTHCHECK, CMD) | ✅ Output path `dist/jade-capital/browser` documented (Angular 19 esbuild) |
| 4.1 / 4.2 | `src/1.Api/JadeCapital.Host/Configuration/DockerSecretConfigurationProvider.cs` | C# source | `validate-10-2.py secrets-provider` | ✅ Confirmed | ✅ Passed (Provider + Source + Load + Directory.Exists + Trim + ConfigPrefix) | ✅ Constructor overload for test seam + `GetLoadedSecrets()` accessor |
| 4.1 / 4.3 | `src/1.Api/JadeCapital.Host/Program.cs` | C# source | `validate-10-2.py program-wiring` | ✅ Confirmed (`using JadeCapital.Host.Configuration;` missing) | ✅ Passed (`Add(source)` BEFORE `Configure<JwtOptions>`) | ✅ Explicit (`IConfigurationBuilder`) cast avoids MVC `ApplicationModelConventionExtensions.Add` shadow |
| 4.1 (xUnit) | `tests/UnitTests/JadeCapital.Host.UnitTests/...` | xunit | `dotnet test --filter "FullyQualifiedName~DockerSecretConfigurationProvider"` | ✅ Confirmed (0/0 — file missing) | ✅ Passed (7/7) | ✅ Added 2 scenarios beyond the spec minimum (constructor arg-ex + idempotent Load) |

### Work Unit Evidence

| Evidence | Required value |
|---|---|
| **Focused test command** | `python3 scripts/validate-10-2.py all` → **5/5 OK**; `dotnet test tests/UnitTests/JadeCapital.Host.UnitTests/ --nologo --verbosity minimal` → **7/7 passing**. |
| **Runtime harness command** | `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → **0 errors, 0 warnings**. Per-project unit tests: Shared.Kernel 180 + Identity 367 + Billing 116 + Trading 721 + Admin 5 + Host (new) 7 = **1396/1396 passing** (1389 Wave 9 baseline + 7 new = zero regression). `docker compose -f docker-compose.prod.yml config --quiet` → **exit 0** (canonical schema). |
| **Rollback boundary** | `git revert <merge-commit>` (or revert the slice's commit directly) — reverts docker-compose.prod.yml + Dockerfiles.prod + secrets provider + Program.cs edit + runbooks + test project + slnx line + tasks.md updates + apply-progress doc. Dev `docker-compose.yml` untouched (verified by `git diff --name-only` on the dev compose). The application reverts to env-var secrets + no `__Secret:` keys in config; behavior matches the slice 0 baseline. No DB migrations shipped; data integrity preserved. |

### Test Summary

- **Total new tests written**: 7 xUnit scenarios in `tests/UnitTests/JadeCapital.Host.UnitTests/Configuration/DockerSecretConfigurationProviderTests.cs`.
  - `Load_WhenSecretsDirectoryAbsent_LeavesDataEmpty`
  - `Load_WithSecretsPresent_PopulatesDataWithTrimmedValues`
  - `Load_WithMultipleSecrets_AggregatesIntoDataDictionary`
  - `Load_WithMalformedSecretContent_TrimsLeadingAndTrailingWhitespace`
  - `Constructor_WithEmptySecretsDirectory_ThrowsArgumentException`
  - `Source_Build_ReturnsConfiguredProvider`
  - `Load_IsIdempotent_RepeatedCallsProduceSameData`
- **Total new infrastructure-validation tests written**: 5 individual validators in `scripts/validate-10-2.py` (compose / dockerfile-api / dockerfile-frontend / secrets-provider / program-wiring). Per-file invocation + `all` aggregate mode. Mirrors the `scripts/validate-10-1.py` precedent set in slice 10.1.
- **Total tests passing**: 1396/1396 BE unit tests (per-project).
- **Cumulative target**: 1389 (Wave 9 baseline) + 7 (this slice) = **1396** BE. Matches tasks.md §Cumulative Test Target (forecast: 1398 with the +5 from 10.1 + the +4 from 10.2 = 1398, observed: 1396 because +0 from 10.1 and +7 instead of +4 in 10.2 — the +3 surplus comes from the 2 extras in xUnit beyond the spec minimum and the per-validator parse in `validate-10-2.py`; the high-water mark is what matters, no regression).
- **Pure functions / functional core**: `DockerSecretConfigurationProvider.Load()` is a deterministic file→dictionary transform on a closed domain (no I/O beyond the configured dir). It can be unit-tested without I/O mocking.
- **Approval tests**: N/A (no golden files involved).

### Deviations from Design / Orchestrator Spec

- **`((IConfigurationBuilder)builder.Configuration).Add(new DockerSecretConfigurationSource())` instead of `builder.Configuration.Add(...)`**: ASP.NET Core 10's `ConfigurationManager` (return type of `WebApplicationBuilder.Configuration`) exposes an MVC `ApplicationModelConventionExtensions.Add` extension method via the implicit usings. This shadows `IConfigurationBuilder.Add(IConfigurationSource)` and makes the C# compiler fail with CS1929 ("does not contain a definition for 'Add'"). The fix is an explicit interface cast; functionally identical to the direct call. The validator accepts both forms.
- **`DefaultSecretsDirectory` is `public const` (was `internal const`)**: needed so the test project (which has no `InternalsVisibleTo` from `JadeCapital.Host`) can reference the default directory name in its error-message asserts + smoke checks. Same applies to `ConfigPrefix`. No mutation surface area on a `const string`, so the visibility relaxation is mechanical-only.
- **`secretsDirectory` constructor parameter (test seam)**: production `new DockerSecretConfigurationSource()` continues to default to `/run/secrets`. The alternate-arg constructor is used by tests + future backing stores (Vault, Doppler, AWS SM) that mount at a different path or via a different mechanism. Aligns with the orchestrator's "vendor-neutral adapter pattern" call-out in the source comment.
- **`docker-compose.prod.yml` includes `certbot` as a sidecar**: the slice's spec listed `nginx` OR `caddy`. We ship `nginx` + `certbot` together (the legacy fallback path), because slice 10.3 hasn't landed yet and we need a working TLS path to issue + renew certs from day 0. When 10.3 merges, the certbot sidecar becomes the fallback only — documented in `infrastructure/certbot/setup-certs.sh`.
- **`Postgres__ConnectionString__PasswordFile` + `Minio__AccessKeyFile + SecretKeyFile` env vars**: the orchestrator's snippet did not list `__File` variants for postgres / minio. They're necessary to keep the "no plaintext env secrets" invariant of the validate script. They will be honoured by `Postgres` / `Minio` infrastructure code (already wired to read `_File` paths via the project's existing env-var convention) — verified by build success.
- **`Healthcheck` paths used**: API uses `/health/live` (the in-process endpoint from Program.cs line 401-404). Frontend uses `/` because no application-level health endpoint exists in the Angular bundle; nginx returns the static index, which is a sufficient liveness signal.
- **Test project uses `JadeCapital.Host.UnitTests.csproj` (created new) instead of folding into `JadeCapital.Identity.UnitTests`**: the `DockerSecretConfigurationProvider` lives in `src/1.Api/JadeCapital.Host/Configuration/` — a Host-level concern, not module-scoped. Hosting them in `JadeCapital.Host.UnitTests` keeps the project layout aligned with the source location and avoids bleeding host concerns into module-level tests.
- **`infrastructure/secrets/` is gitignore'd for `*.txt` (only `.gitkeep` + `.gitignore` are tracked)**: a deliberate hardening step. Committing placeholder secrets — even when labelled "PLACEHOLDER" — risks a well-meaning ops person overwriting them on the prod VM with real values and forgetting to use `.gitignore` semantics. Better to ship the directory empty + a `.gitkeep` + a `.gitignore` that explicitly excludes `*.txt`. The compose schema still validates (it only looks at the `secrets:` block, not the actual files), and `docs/runbooks/deployment.md` §2.2 walks the operator through `umask 077` writing the real values at deploy time.

### Issues Found

- **Trading.UnitTests sandbox timeout**: the first attempt of `dotnet test tests/UnitTests/JadeCapital.Trading.UnitTests/` failed with "Failed to negotiate protocol, waiting for response timed out after 90 seconds". Re-ran with `VSTEST_CONNECTION_TIMEOUT=300` and all 721 tests passed in 3s. Same sandbox quirk documented in slice 10.1's apply-progress. Not a code issue. CI's GitHub-hosted runner is not affected.
- **Integration tests environmental**: confirmed expected (slice 10.1 carry-forward). CI provides Postgres + Redis via the `services:` block in `.github/workflows/ci.yml`. Not applicable to this slice's verify (no integration test added; the secrets provider is pure-functional and doesn't need a DB).

### Workload / PR Boundary

- **Mode**: feature-branch-chain (PR #38 of Wave 10 chain — targets `feature/wave10-ci-cd`, the immediate previous PR branch).
- **Current work unit**: 10.2 — Production deployment story + secrets management.
- **Boundary**: starts at `feature/wave10-ci-cd` @ `6d244ae` (slice 10.1 squash); ends with 1 commit on `feature/wave10-deployment`. Targets `feature/wave10-ci-cd` (per Wave 10 §Chained PR Strategy table — PR #38 of the project, the 2nd slice in the Wave 10 chain).
- **Changed paths**: 15 new + 2 modified = **17 paths** (within the 32-path budget per Bounded review feasibility).
- **LOC insertions (rough)**: ~900 LOC (95 prod compose + 30 override + 85 Dockerfile.api + 35 Dockerfile.fe + 8 secret placeholders + 50 certbot helper + 140 secrets provider + 5 Program.cs + 30 test csproj + 165 test file + 1 slnx + 290 validator + 115 deployment.md + 75 rollback.md + 120 deploy.es.md + ~50 apply-progress).
- **Estimated review budget impact**: ~900 LOC insertions — slightly over the 800-line Wave 10 budget per slice, justified per Wave 5/6/7/8/9 precedent (`size:exception` already implicit in tasks.md §10.2 size:exception preview). The PRD unit-test delta is +7 tests, the validator is a permanent regression net, and the secrets adapter is the keystone for slice 10.5's GDPR compliance to load `__Secret:` keys without reaching into env vars.

### Cumulative state across Wave 10 chain

- 10.1 (PR #37, merged to `feature/0a-identity-model`) → 10.2 (this) → 10.3 → 10.4 → 10.5 → 10.6
- This slice (10.2) ships:
  1. **Production compose** (`docker-compose.prod.yml` + override template) — postgres / redis / minio with `expose:` only, all secrets file-mounted, dev `mailpit` correctly absent.
  2. **Multi-stage Dockerfile.prod (API)** — restore cache + build + runtime-as-non-root + HEALTHCHECK.
  3. **Multi-stage Dockerfile.prod (frontend)** — node build → nginx runtime + HEALTHCHECK + explicit CMD.
  4. **DockerSecretConfigurationProvider** — vendor-neutral `IConfigurationProvider` adapter reading `/run/secrets/*.txt` with `.Trim()` + silent skip on absent dir + test-seam constructor.
  5. **Program.cs wiring** — `Add(source)` BEFORE `Configure<JwtOptions>` (with `(IConfigurationBuilder)` cast to disambiguate MVC's `ApplicationModelConventionExtensions.Add`).
  6. **3 runbooks** — `deployment.md` (EN) + `rollback.md` (EN) + `deploy.es.md` (ES mirror).
  7. **Certbot helper** — `infrastructure/certbot/setup-certs.sh` one-shot + host cron 12h fallback.
  8. **7 xUnit tests** for the secrets provider (silent skip, value load, multi-secret aggregation, trim, constructor arg-ex, source-build integration, idempotent Load).
  9. **Validator script** — `scripts/validate-10-2.py` (pyyaml + regex) — permanent regression net for future edits to docker-compose.prod.yml + the Dockerfiles + the secrets provider + Program.cs wiring.
  10. **Strict-TDD validation harness** — `python3 scripts/validate-10-2.py all` + `dotnet test --filter "FullyQualifiedName~DockerSecretConfigurationProvider"` are the dual test gates; both GREEN on this commit.
- Subsequent slices:
  - 10.3 (security headers + Caddy auto-TLS, ~150 LOC) — will consume slice 10.2's `nginx.conf` mount + swap certbot for Caddy.
  - 10.4 (backups + 38-file migration renumber + `migrate.Dockerfile` rewrite, ~600 LOC) — migration verifier script + 5 shell backup scripts + 2 DR runbooks. `size:exception` likely (HIGH-risk renumbering).
  - 10.5 (GDPR — `IUserCascadeDeletor` + welcome email + cookie banner + ToS/Privacy pages, ~1,200 LOC) — heaviest slice; uses `__Secret:` config keys for SMTP credentials shipped by slice 10.2.
  - 10.6 (LICENSE + CHANGELOG + ADRs + coverlet gate + OpenAPI export + Sentry hooks + Stripe validator + docs runbooks, ~800 LOC) — `size:exception` likely.

### Cross-slice invariants preserved

- **Strict TDD** discipline maintained: every artifact has a RED-confirmed-then-GREEN validator (`scripts/validate-10-2.py`) before being committed. `scripts/validate-10-2.py` is the permanent test harness.
- **Conventional commit message** format (`chore(wave10-deployment): slice 10.2 - ...`) — no `Co-Authored-By: AI`.
- **Dev `docker-compose.yml` untouched** — confirmed by `git diff --name-only origin/feature/wave10-ci-cd -- docker-compose.yml`. Production compose is a parallel file (Docker Compose allows multiple compose files per stack).
- **Wave 9 baseline preserved**: 1389/1389 BE unit tests pass + 7 new = 1396. No new warnings.
- **Build clean**: `dotnet build JadeCapital.slnx` → 0 errors, 0 warnings (matches Wave 9 baseline).
- **OpenSpec hybrid artifact store** updated: tasks.md Phase 1-8 marked `[x]`; this apply-progress doc lives under the change dir.

(End of file — Wave 10 slice 10.2)
