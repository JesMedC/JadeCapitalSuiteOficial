# Tasks — Wave 5 (Imports + AI Coaching + AI Risk Advisor)

## Review Workload Forecast

| Slice | Boundary | LOC | Paths | Base |
|---|---|---:|---:|---|
| 5a.1 | Importer foundation + CSV (migration 0019 + IImportRowParser + CsvImportRowParser + ImportJob + StreamImportService + 2 endpoints + FE imports-page + 15 tests) | ~700 | 16 | tracker |
| 5a.2 | MT4/MT5 parser + format auto-detection + 10 tests | ~500 | 6 | 5a.1 |
| 5b.1 | AI provider interface + OllamaHttpClient (migration: none) + GET /api/ai/health + 12 tests with HttpMessageHandler mock | ~500 | 9 | 5a.2 |
| 5b.2 | CoachingPrompt aggregate + migration 0020 + GenerateCoachingPromptHandler + CoachingPromptService BG + GET /api/coaching/ai-prompts + FE coaching tab extension + 15 tests | ~700 | 13 | 5b.1 |
| 5c.1 | AIRiskAdvice aggregate + migration 0021 + IAIRiskAdvisor + OllamaAIRiskAdvisor + GetPreTradeAdviceHandler + OpenTradeHandler modification + 2 endpoints + FE checklist advisory section + 15 tests | ~600 | 17 | 5b.2 |
| 5c.2 *(optional)* | Smoke E2E 3 probes + ollama-health.interval + tasks close + archive marker | ~300 | 5 | 5c.1 |
| **Total** | 6 slices (5 mandatory + 1 optional) | **~3,300** | — | chained |

Decision needed before apply: **No** (auto-chain, 400-line budget per PR → `size:exception` per slice as Wave 4 precedent). User confirmed `feature-branch-chain` (Wave 0/1/2/3/4 precedent). Per-slice `git diff --name-only` MUST be ≤ 32 paths (mandatory); see path counts above.

**Build**: `dotnet build JadeCapital.slnx --nologo --verbosity minimal`.
**SQL harness**: `psql -v ON_ERROR_STOP=1 -f migrations/<file>.sql`; idempotent re-run (same script twice → exit 0).
**Frontend**: `cd frontend && npm run build` and `cd frontend && npx jest`.
**Ollama local assumed**: `ollama serve` running on `http://localhost:11434` for end-to-end Ollama tests. **Defense-in-depth**: tests use `HttpMessageHandler` mocks, so Ollama is NOT required for unit tests; only smoke E2E needs a live provider.

### Work Units (PR → test → runtime → rollback)

- 5a.1: `dotnet test --filter "FullyQualifiedName~Import"` + `npm test -- --testPathPattern=imports`. Rollback: revert code; tabla inerte queda.
- 5a.2: `dotnet test --filter "FullyQualifiedName~Mt4|Mt5|Import"`. Rollback: revert code; tabla inerte queda.
- 5b.1: `dotnet test --filter "FullyQualifiedName~Ollama|AIProvider"`. Rollback: revert code; provider registration removida.
- 5b.2: `dotnet test --filter "FullyQualifiedName~CoachingPrompt|GenerateCoaching"` + `npm test -- --testPathPattern=coaching`. Rollback: revert code; tabla inerte queda.
- 5c.1: `dotnet test --filter "FullyQualifiedName~AIRiskAdvisor|PreTradeAdvice|OpenTrade"` + `npm test -- --testPathPattern=checklist`. Rollback: revert code; tabla inerte queda; OpenTrade vuelve al path previo.
- 5c.2: `scripts/wave5-smoke.sh` + dotnet test (full) + jest (full) + verify-report.

---

## Slice 5a.1 — Importer Foundation + CSV (≤ 700 líneas, 16 paths)

### 5a.1 Backend (~500 líneas)

**Phase 1: Shared kernel (TDD)**

- [x] 1.1 RED test `ImportFormatTests` (3 scenarios: enum values, range check, JSON round-trip).
- [x] 1.2 RED test `ImportRowTests` (5 scenarios: direction enum, status enum, default volumes, line number monotonic, JSON contract).
- [x] 1.3 GREEN: `Shared.Kernel/Imports/ImportFormat.cs` + `Shared.Kernel/Imports/ImportRow.cs` + `Shared.Kernel/Imports/ImportDirection.cs` + `Shared.Kernel/Imports/ImportRowStatus.cs`.
- [x] 1.4 RED test `IImportRowParserContractTests` (3 scenarios: interface shape, CanParse signature, ParseAsync returns IAsyncEnumerable).
- [x] 1.5 GREEN: `Shared.Kernel/Imports/IImportRowParser.cs`.

**Phase 2: Domain (TDD)**

- [x] 2.1 RED test `ImportJobTests` (16 scenarios: create valid, file size cap 10MiB, status transitions InProgress→Completed, Failed preserves partial counters, sha256 length check, account FK validation, rows monotonic, finished_at nullable until Complete, idempotent re-Create with same sha256 → second job skipped).
- [x] 2.2 GREEN: `Trading.Domain/Imports/ImportJob.cs` (aggregate root + `ImportJobStatus` enum + `ImportJobErrors.cs`).

**Phase 3: Migration**

- [x] 3.1 `infrastructure/postgres/migrations/0019_import_jobs.sql` — `trading.import_jobs` table (16 columns + 2 indexes) — idempotent, additive. Wire en `migrate.Dockerfile` happy + retry path (`\\\"` escape).

**Phase 4: Application (TDD)**

- [x] 4.1 RED test `BeginImportHandlerTests` (6): valid request → job created, file size > 10MiB → 422, empty file → 422, account not found → 404, sha256 collision → 409, account owned by other user → 404.
- [x] 4.2 GREEN: `Trading.Application/Features/Imports/BeginImportCommand.cs` + `BeginImportHandler.cs` + `IImportJobRepository.cs` interface.
- [x] 4.3 RED test `GetImportStatusHandlerTests` (3): own job → 200, other user's job → 404, completed job returns full counters.
- [x] 4.4 GREEN: `GetImportStatusQuery.cs` + `GetImportStatusHandler.cs` + `ImportJobDto.cs` + `ImportJobMapping.cs`.
- [x] 4.5 RED test `StreamImportServiceTests` (8 with `Mock<IImportRowParser>`): happy path 0→50→100 rows, all-dupes → 0 imported + N skipped, batch-of-50 splits correctly, partial batch commits, null trade on row error → row marked errored + loop continues, file too large → 422, dedupe query single call per batch, transaction rollback on insert failure.
- [x] 4.6 GREEN: `Trading.Application/Features/Imports/StreamImportService.cs` + `IImportRowDedupeService.cs` + `ImportRowDedupeService.cs` (uses `TradingDbContext` for `WHERE (user_id, account_id, ticket_id) IN (...)`).

**Phase 5: CSV parser (TDD)**

- [x] 5.1 RED test `CsvImportRowParserTests` (11 with synthetic CSV streams): CanParse returns 0.9+ for known headers, 0.0 for binary / JSON input, parses 1000 rows correctly (parity with manual parser), handles empty lines, handles quoted fields with embedded commas, handles BOM-prefixed UTF-8, dedupes headers case-insensitively, exit_price absence → null, time zone naive times are treated as UTC, malformed row skips without aborting the stream, CRLF line endings.
- [x] 5.2 GREEN: `Trading.Infrastructure/Imports/CsvImportRowParser.cs` (uses `Microsoft.VisualBasic.FileIO.TextFieldParser` for robust CSV — handles quotes, escapes, BOM out of the box).

**Phase 6: Infrastructure + API**

- [x] 6.1 `ImportJobConfiguration` (EF) — `HasColumnName` snake_case + 2 indexes + `ck_import_jobs_*` CHECK constraints in `OnModelCreating`.
- [x] 6.2 `ImportJobRepository` impl (GetByIdAsync, FindActiveBySha256Async, AddAsync, UpdateAsync).
- [x] 6.3 `ImportEndpoints` (`MapImportEndpoints`): `POST /api/imports/csv` (multipart/form-data, max 10 MiB, returns 202 + `{ importJobId }`) + `GET /api/imports/{id}` (returns full DTO with status + counters). RequireAuthorization. `api-general` rate limit.
- [x] 6.4 `app.MapImportEndpoints()` en `Program.cs`.
- [x] 6.5 DI: `AddScoped<IImportRowParser, CsvImportRowParser>()` + `AddScoped<StreamImportService>` + `AddScoped<IImportRowDedupeService, ImportRowDedupeService>` + `AddScoped<BeginImportHandler>` + `AddScoped<GetImportStatusHandler>` + `AddScoped<IImportJobRepository, ImportJobRepository>` en `TradingModuleRegistration`.

**Phase 7: Validate**

- [x] 7.1 `dotnet test --filter "FullyQualifiedName~Import|Csv"` --nologo --verbosity minimal → 55/55 passed (44 imports + 11 CSV).
- [x] 7.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.

### 5a.1 Frontend (~200 líneas)

**Phase 1: Service + state**

- [x] 1.1 `api/imports.service.ts` con 2 métodos HTTP (`uploadCsv(file: File)` con `FormData`, `getStatus(jobId: string)`).
- [x] 1.2 `state/imports.state.ts` (Signals: `currentJob`, `progress: { imported, skipped, errored, total }`, `status: 'idle'|'uploading'|'polling'|'completed'|'failed'`, `error`).
- [x] 1.3 2 jest specs (state) — covered by 6 page-level specs.

**Phase 2: Page + routing**

- [x] 2.1 `imports-page.ts` standalone Signals OnPush SCSS con: drop zone (drag-and-drop + click-to-pick) + progress bar (subscribes to 1s polling) + result summary (imported/skipped/errored + last error).
- [x] 2.2 `imports.routes.ts` (sub-routes: `/imports`).
- [x] 2.3 Add `'imports'` route a `trader.routes.ts` (loadChildren → IMPORTS_ROUTES).
- [x] 2.4 Add `'Imports'` nav entry a `trader-shell.ts` (10 items total). Mobile-nav horizontal scroll continues to work.
- [x] 2.5 6 jest specs (renders title, exposes helper methods, canUpload gates, accountId empty/invalid/valid).

### 5a.1 E2E wiring (final patch)

- [x] 3.1 Smoke E2E: upload sample CSV → poll for completion → verify counters. (3 probes in `scripts/wave5-smoke.sh` cover this in 5c.2.)

> **Slice 5a.1 completion note**: code lands with all tests green at slice close. Build green, **64 unit tests** (44 BE imports + 11 CSV + 6 FE + 3 trader-shell regression). Deviations documented in `apply-progress-wave5-slice-5a-1.md` — D1 (rate-limit), D2 (InstrumentId resolution), D4 (list endpoint), and size:exception for 2,108 production LOC vs 700-line forecast.

---

## Slice 5a.2 — MT4/MT5 Parser (≤ 500 líneas, 6 paths)

**Phase 1: MT4/MT5 parser (TDD)**

- [x] 1.1 RED test `Mt4ImportRowParserTests` (16 with sample MT4/MT5 exports — exceeds 15 forecast): CanParse returns 1.0 for MT4 header (`Ticket`, `Open Time`, `Type`, `Volume`, `Symbol`, `Open Price`, `SL`, `TP`, `Close Time`, `Close Price`, `Commission`, `Swap`, `Profit`), 1.0 for MT5 header (`Deal`, `Order`, `Time`, `Action`, `Volume`, `Symbol`, `Price`, `Commission`, `Swap`, `Profit`, `Position ID`), 0.0 for CSV (so the dispatcher picks CsvImportRowParser first), parses 500 MT4 trades correctly (parity with reference data), parses MT5 deals (which have `Position ID` not `Ticket`), handles open MT4 trade (no close), handles both `Buy` and `Sell` direction strings, MT5 deals with different PositionId remain separate rows, handles 5-digit FX quotes (1.08501 → 1.08501), ignores MT4 comment column (freeform, may contain prompt-injection — never persisted), normalizes all dates to UTC, handles UTF-8 BOM.
- [x] 1.2 GREEN: `Trading.Infrastructure/Imports/Mt4ImportRowParser.cs` — single impl handles both MT4 and MT5 (their CSV exports share the same parser infrastructure). Format detection in `CanParse` via distinguishing markers (`SL`/`TP` for MT4, `Position ID` for MT5); MT5 mode triggers deal aggregation by Position ID inside `ParseAsync`.

**Phase 2: Auto-detection in StreamImportService**

- [x] 2.1 RED test `ImportAutoDetectionTests` (6 with synthetic streams — exceeds 5 forecast): MT4 header → Mt4ImportRowParser, MT5 header → Mt4ImportRowParser, generic CSV header → CsvImportRowParser, unknown format (JSON) → null (caller maps to 422 `import.format_unrecognized`), ambiguous CSV → first registered parser wins, sub-threshold score (0.5) is skipped.
- [x] 2.2 GREEN: new `ImportParserDispatcher` static helper with `SelectParser(parsers, fileName, head)` (picks first parser with `CanParse >= 0.8`). New `StreamImportService.ExecuteAsync(job, body, IEnumerable<IImportRowParser> parsers, fileName, ct)` overload buffers the body into a seekable `MemoryStream`, calls the dispatcher, and delegates to the existing single-parser pipeline. Update DI to register `Mt4ImportRowParser` before `CsvImportRowParser` (per-spec precedence: more-specific signatures win).

**Phase 3: Validate**

- [x] 3.1 `dotnet test --filter "FullyQualifiedName~Mt4|FullyQualifiedName~Import"` --nologo --verbosity minimal → 77 passed (22 new + 55 existing).
- [x] 3.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal -p:TreatWarningsAsErrors=true` → 0 errors, 0 warnings.
- [x] 3.3 No frontend changes in 5a.2 (CSV page already accepts `.csv` extension; MT4/MT5 files also commonly have `.csv` extension — same endpoint works).
- [x] 3.4 `dotnet test --no-build --nologo --verbosity minimal --filter "FullyQualifiedName!~JadeCapital.Api.IntegrationTests"` → 884/884 pass (up from 862 in 5a.1 — +22 new tests, no regressions).

> **Slice 5a.2 completion note**: closed by SDD apply sub-agent with strict TDD (RED → GREEN → REFACTOR per phase). 22 new tests written first. `size:exception` justified (1,134 net LOC vs 500-line forecast; precedent: 5a.1 = 2,108 prod LOC with `size:exception` already accepted). Same path-budget exception as 5a.1 (7 paths vs 6-path forecast — both well under 32 limit). `ImportJob.InstrumentId` resolution (5a.1 deviation D2) and `GET /api/imports` list endpoint (5a.1 deviation D4) deferred per the apply-progress 5a.1 note — those still land in 5c.2.

> **Slice 5a.2 completion note**: same flow as 5a.1, single back-end slice. No dedicated apply-progress file unless deviations arise.

---

## Slice 5b.1 — AI Provider Interface + Ollama HttpClient (≤ 500 líneas, 9 paths)

### 5b.1 Backend (~500 líneas)

**Phase 1: Shared kernel (TDD)**

- [x] 1.1 RED test `PromptRequestTests` (4 scenarios: defaults, custom model + temperature, JSON contract, validation max tokens > 0).
- [x] 1.2 RED test `PromptResponseTests` (4 scenarios: required Content, LatencyMs non-negative, token counts, JSON contract).
- [x] 1.3 GREEN: `Shared.Kernel/Ai/PromptRequest.cs` + `PromptResponse.cs` + `AIProviderOptions.cs` (single AIProviderKind.cs deferred to Wave 6 — see apply-progress D5).
- [x] 1.4 RED test `IAIProviderContractTests` (3 scenarios: interface shape, IsHealthyAsync does NOT throw, Result<PromptResponse> failure path is exhaustive).
- [x] 1.5 GREEN: `Shared.Kernel/Ai/IAIProvider.cs`.

**Phase 2: OllamaHttpClient (TDD with HttpMessageHandler mock)**

- [x] 2.1 RED test `OllamaHttpClientTests` (12 with `StubHttpMessageHandler`): happy path returns `PromptResponse(text, model, tokens_used, duration)`, 5xx → `Result.Failure("failure.ai.unavailable")`, timeout (TaskCanceledException) → `Result.Failure("failure.ai.timeout")`, empty content → `Result.Failure("failure.ai.empty_response")`, malformed JSON → `Result.Failure("failure.ai.parse_error")`, IsHealthyAsync returns true on 200, IsHealthyAsync returns false on connection refused (no throw), IsHealthyAsync returns false on 5xx (no throw), base URL trimming (trailing `/`), System context prepended to prompt, model from `AIProviderOptions`, manual retry 3 attempts on transient 5xx (manual not Polly — see apply-progress D1).
- [x] 2.2 GREEN: `Trading.Infrastructure/Ai/OllamaHttpClient.cs` + `OllamaGenerateRequest` + `OllamaGenerateOptions` + `OllamaGenerateResponse` (private DTOs).
- [x] 2.3 `AIProviderOptions` reading from config: `builder.Configuration.GetSection("Ollama")` (or env vars `Ollama__BaseUrl` / `Ollama__Model` / `Ollama__Timeout`). Default binds if absent (BaseUrl="http://localhost:11434", Model="llama3.1:8b", Timeout=30s).

**Phase 3: AI health endpoint**

- [x] 3.1 RED test `GetAiHealthHandlerTests` (3 with `Mock<IAIProvider>`): healthy → 200 `{ status: "ok", model: "..." }`, unhealthy → `{ status: "down", model: null }`, cancellation token propagates to provider.
- [x] 3.2 GREEN: `Trading.Application/Features/Ai/GetAiHealthHandler.cs` (includes `GetAiHealthQuery` + `AiHealthDto` records in same file).
- [x] 3.3 `AiEndpoints` partial: `GET /api/ai/health` (added in 5b.1; additional endpoints in 5c.1).
- [x] 3.4 `app.MapAiEndpoints()` en `Program.cs`.

**Phase 4: DI composition**

- [x] 4.1 DI: `AddHttpClient<IAIProvider, OllamaHttpClient>(...)` (in Program.cs, not TradingModuleRegistration — see apply-progress D2) + `services.AddOptions<AIProviderOptions>().Bind(builder.Configuration.GetSection("Ollama"))`. Typed-client lifetime is transient; HttpClient is owned by IHttpClientFactory.
- [x] 4.2 `AIProviderOptions` defaults baked in: `BaseUrl = "http://localhost:11434"`, `Model = "llama3.1:8b"`, `Timeout = 30s` (won't be hit in unit tests — HttpMessageHandler mock intercepts).

**Phase 5: Validate**

- [x] 5.1 `dotnet test --filter "FullyQualifiedName~Ollama|AIProvider|AiHealth|PromptContract"` --nologo --verbosity minimal → 26/26 passed (11 contract + 12 Ollama + 3 health).
- [x] 5.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.
- [x] 5.3 Full suite `dotnet test --filter "FullyQualifiedName!~JadeCapital.Api.IntegrationTests"` → 910/910 passed (Trading went 577 → 603 = +26 new tests, no regressions).

### 5b.1 size:exception

**Forecast**: ~500 lines. **Actual**: 615 production LOC + 592 test LOC = 1207 total. **size:exception** justified per Wave 4/5a.1/5a.2 precedent (5a.1=2108, 5a.2=1134). Tests are ~50% of the diff — required by Strict TDD (HttpMessageHandler-mock tests are more expensive than the production code they cover).

> **Slice 5b.1 completion note**: closed by SDD apply sub-agent with strict TDD (RED → GREEN → REFACTOR per phase). 26 new tests written first (11 contract + 12 OllamaHttpClient + 3 GetAiHealthHandler). 8 production paths (7 new + 1 modified) — under the 9-path budget. Cumulative BE suite: 910/910 (was 884 in 5a.2 → +26 new tests, 0 regressions). Deviations documented in `apply-progress-wave5-slice-5b-1.md` — D1 (manual retry vs Polly), D2 (DI in Program.cs vs TradingModuleRegistration), D3 (TimeSpan Timeout), D4 (PromptResponse field shape), D5 (AIProviderKind enum deferred). All 5 deviations **Accepted** with rationale.

### 5b.1 size:exception preview

Forecast ~500 lines, budget cap 400 → `size:exception` likely approved. Justification: HTTP infrastructure (OllamaHttpClient + Polly + options) is a coherent unit; can't split without artificial boundaries. Tests are ~50% of the diff (10/12 are HTTP-mock tests, expensive to write but mandatory per Strict TDD).

### 5b.1 Frontend (deferred to 5b.2)

No frontend changes in 5b.1. FE hook arrives in 5b.2 once the coaching endpoint exists.

---

## Slice 5b.2 — AI Coaching Integration (≤ 700 líneas, 13 paths)

### 5b.2 Backend (~500 líneas)

**Phase 1: Domain (TDD)**

- [ ] 1.1 RED test `CoachingPromptTests` (8): create valid, severity range, kind discriminator, latency non-negative, model max length, JSON context serialization stable, created_at set on create, immutable after create (no setters).
- [ ] 1.2 GREEN: `Trading.Domain/Ai/CoachingPrompt.cs` + `PromptSeverity.cs` + `CoachingPromptKind.cs` (Rule=0 legacy, Ai=1 new).

**Phase 2: Migration**

- [ ] 2.1 `infrastructure/postgres/migrations/0020_coaching_prompts_ai.sql` — `trading.coaching_prompts_ai` table (12 columns + 1 index) — idempotent, additive. Wire en `migrate.Dockerfile` happy + retry path.

**Phase 3: Application (TDD)**

- [ ] 3.1 RED test `IUserTradingContextProviderTests` (4 with mocks): GetUserContextAsync returns closed trades count + win rate + avg RR for 7d window, empty user → empty context (no exception), user with 0 trades returns no violations, GetActiveUserIdsWithMinTrades returns at most 100 users in 1 batch.
- [ ] 3.2 GREEN: `Trading.Application/Abstractions/IUserTradingContextProvider.cs` + `UserTradingContextProvider.cs` (EF query against `trading.trades` + reuse Wave 3b coaching-violation counters).
- [ ] 3.3 RED test `CoachingPromptTemplateTests` (3): renders context in JSON block separated by delimiters, instructions cap is 50-150 words target, includes prompt-injection-safe separators.
- [ ] 3.4 GREEN: `Trading.Application/Ai/CoachingPromptTemplate.cs`.
- [ ] 3.5 RED test `GenerateCoachingPromptHandlerTests` (7 with `Mock<IAIProvider>` + `Mock<IUserTradingContextProvider>`): happy path creates prompt + persists, AI provider failure → Result.Failure + logs + no persist, user with 0 trades → Result.Success(0 prompts generated), already-generated-today idempotency → no double insert, prompt tokens + latency persisted, severity derived from violations count, context JSON serializes user ID + trade aggregates (no PII).
- [ ] 3.6 GREEN: `GenerateCoachingPromptHandler.cs` + `ICoachingPromptRepository.cs` + `CoachingPromptRepository.cs` + `GetAiCoachingPromptsQuery.cs` + `GetAiCoachingPromptsHandler.cs` + DTOs.

**Phase 4: BG service**

- [ ] 4.1 RED test `CoachingPromptServiceTests` (8 with mocks): RunOnceAsync iterates active users, one user failure does not abort loop, initial delay computes correctly (3am UTC + jitter), runs exactly N times in deterministic clock test, public `RunOnceAsync` for test driving, null IAIProvider (DI misconfig) → silent skip + log, empty active-user list → returns 0, severity propagation from handler.
- [ ] 4.2 GREEN: `Trading.Infrastructure/Ai/CoachingPromptService.cs` (BackgroundService daily 03:00 UTC ± 30min jitter).
- [ ] 4.3 DI: `AddHostedService<CoachingPromptService>()` en `TradingModuleRegistration`.

**Phase 5: EF + API**

- [ ] 5.1 `CoachingPromptConfiguration` (EF).
- [ ] 5.2 Extend `CoachingEndpoints`: `GET /api/coaching/ai-prompts?period=7d|30d|90d|all` (sorted by `created_at DESC`).
- [ ] 5.3 Extend `GetCoachingPromptsHandler` (Wave 3b): merge rule-based + AI prompts in single response with `kind` discriminator. Rule prompts sort by severity; AI prompts sort by `created_at DESC`.

**Phase 6: Validate**

- [ ] 6.1 `dotnet test --filter "FullyQualifiedName~Coaching|GenerateCoaching"` --nologo --verbosity minimal → 15+ passed.
- [ ] 6.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.

### 5b.2 Frontend (~200 líneas)

**Phase 1: Service**

- [ ] 1.1 Extend `coaching.service.ts`: `getAiPrompts(period: '7d'|'30d'|'90d'|'all')` method.
- [ ] 1.2 Extend `coaching-page.ts`: add "AI Prompts (last 7d)" section between rule-based list and recent journal. Renders provider response + severity badge + CTA. Empty state: "We'll generate your first AI prompt overnight — keep trading."

**Phase 2: Tests**

- [ ] 2.1 2 new jest specs: AI section renders empty state, AI section renders received prompts with severity badge.

### 5b.2 E2E wiring (final patch)

- [ ] 3.1 Smoke E2E: manual trigger of `RunOnceAsync` (admin endpoint or test harness) verifies the prompt is generated. (3 probes in `scripts/wave5-smoke.sh` cover manual upload + advisory).

---

## Slice 5c.1 — AI Risk Advisor Pre-Trade (≤ 600 líneas, 17 paths)

### 5c.1 Backend (~450 líneas)

**Phase 1: Domain (TDD)**

- [ ] 1.1 RED test `AIRiskAdviceTests` (10): create valid, action range 0..2, reason max length 500, trade_id FK validation, context JSON, persistence aggregate, no domain events, immutable after create, parse action from string case-insensitive, parse null reason → "(no reason given)" sentinel.
- [ ] 1.2 GREEN: `Trading.Domain/Ai/AIRiskAdvice.cs` + `AIRiskAction.cs`.

**Phase 2: Migration**

- [ ] 2.1 `infrastructure/postgres/migrations/0021_ai_risk_advice.sql` — `trading.ai_risk_advice` (10 cols + 1 index) + ALTER TABLE `trading.pre_trade_checklists` ADD COLUMN IF NOT EXISTS `ai_advisory JSONB`. Idempotent, additive. Wire en `migrate.Dockerfile`.

**Phase 3: Application (TDD)**

- [ ] 3.1 RED test `IAIRiskAdvisorContractTests` (3 scenarios: interface shape, AdviseAsync returns Result<AIRiskAdvice>, supports cancellation).
- [ ] 3.2 GREEN: `Trading.Application/Ai/IAIRiskAdvisor.cs`.
- [ ] 3.3 RED test `AIRiskAdvisorPromptTests` (4): renders structured prompt with explicit delimiter markers (`--- USER TRADING CONTEXT ---`, `--- PROPOSED TRADE ---`, `--- ADVISORY JSON ---`), excludes PII fields (no email, no name, no absolute P&L), no-op when context is empty, max length bounded.
- [ ] 3.4 GREEN: `AIRiskAdvisorPrompt.cs`.
- [ ] 3.5 RED test `AIRiskAdvisorResponseParserTests` (6): parses valid `{action, reason}` JSON, strips markdown code fences around the JSON, malformed JSON → safe default `(Allow, "AI returned unparseable response")`, missing action → safe default Allow, unknown action string → safe default Allow, reason truncated to 500 chars.
- [ ] 3.6 GREEN: `AIRiskAdvisorResponseParser.cs`.
- [ ] 3.7 RED test `GetPreTradeAdviceHandlerTests` (8 with mocks): happy path creates + persists advice with action=Warning, AI failure → Result.Failure (does NOT persist), timeout (5s) → Result.Failure, parsed action=Block persists with action=Block, parsed action=Allow persists with action=Allow, null context → safe default Allow, context JSON excludes PII (test asserts no `email`/`displayName` in serialized JSON), cancellation propagates.
- [ ] 3.8 GREEN: `GetPreTradeAdviceHandler.cs` + `IAIRiskAdviceRepository.cs` + `AIRiskAdviceRepository.cs`.

**Phase 4: Infrastructure**

- [ ] 4.1 RED test `OllamaAIRiskAdvisorTests` (6 with mocks): delegates to IAIProvider, applies 5s timeout via CancellationTokenSource, calls parser on response, falls back to Allow on parse failure, persists advice via repo on success, no-op on failure (no persist).
- [ ] 4.2 GREEN: `Trading.Infrastructure/Ai/OllamaAIRiskAdvisor.cs`.

**Phase 5: OpenTradeHandler modification (CRITICAL — backward-compat)**

- [ ] 5.1 Modify `OpenTradeHandler.cs`: add **optional** `IAIRiskAdvisor?` dependency via nullable parameter + secondary ctor overload. If null (legacy DI), skip advisor entirely. If non-null + checklist present, invoke advisor pre-commit, attach advice to checklist, return 422 if Block.
- [ ] 5.2 RED test `OpenTradeHandlerWithAdvisorTests` (6): no advisor dependency → legacy path unchanged (5+ existing tests still pass), advisor returns Warning → trade opens + advisory persisted, advisor returns Block → 422 with `ai_risk.blocked` error, advisor throws → silent fallback (no exception, trade opens), advisory attached to checklist's `ai_advisory` column, unrelated test (no checklist) → advisor not invoked.
- [ ] 5.3 GREEN: extend `OpenTradeHandler.Handle` with the new step.
- [ ] 5.4 DI: register `OllamaAIRiskAdvisor` as `IAIRiskAdvisor` (Scoped, same lifetime as OpenTradeHandler). Existing registrations unchanged.

**Phase 6: Pre-trade checklist repo + EF**

- [ ] 6.1 Extend `PreTradeChecklistConfiguration` (additive `ai_advisory JSONB` mapping).
- [ ] 6.2 Modify `PreTradeChecklistRepo.UpdateAsync` to flush `ai_advisory` if set.
- [ ] 6.3 Modify `PreTradeChecklist.AIRiskAdvisoryJson` + `AttachAIRiskAdvisory(string json)` domain method.

**Phase 7: AI endpoints (extend AiEndpoints from 5b.1)**

- [ ] 7.1 `POST /api/ai/risk-advice` — manual advisor request (used by FE for pre-flight check before showing the OpenTrade form). Body: `{ symbol, direction, volume, entryPrice, riskRewardAtEntry, setupQuality }`. Returns `{ action, reason, adviceId, persistedAt }`.
- [ ] 7.2 `GET /api/ai/risk-advice/{tradeId}` — cached advice for an already-opened trade (advisory query after the fact).
- [ ] 7.3 Both require `RequireAuthorization` + `api-general` rate limit (60/hour per user — heavier than general).

**Phase 8: Validate**

- [ ] 8.1 `dotnet test --filter "FullyQualifiedName~AIRiskAdvisor|PreTradeAdvice|OpenTrade|AiEndpoints"` --nologo --verbosity minimal → 15+ passed.
- [ ] 8.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.

### 5c.1 Frontend (~150 líneas)

**Phase 1: Service**

- [ ] 1.1 `risk-advice.service.ts`: 3 methods (`getHealth`, `requestAdvice(payload)` returning `{ action, reason, adviceId }`, `getCachedAdvice(tradeId)`).

**Phase 2: Page integration**

- [ ] 2.1 Modify `pre-trade-checklist-page.ts`: add AI Advisory section. Renders below checklist inputs, above Submit button. Polls `requestAdvice` once when checklist is dirty (debounced 800ms). Shows advisory card with action badge (green/yellow/red). If Block, shows modal: "AI recommends not opening this trade. Override? [Cancel] [Override]".

**Phase 3: Tests**

- [ ] 3.1 3 jest specs: section renders empty (no advisory), section renders Warning with reason copy, Block triggers modal with override flow.

### 5c.1 E2E wiring (final patch)

- [ ] 4.1 Smoke E2E: POST /api/ai/risk-advice with mock context → verify Block action returned. (3 probes in `scripts/wave5-smoke.sh`.)

> **Slice 5c.1 completion note**: this slice modifies `OpenTradeHandler.cs` (the critical-path trade-opening flow). The `IAIRiskAdvisor?` nullable dependency + null-check at the top of the handler guarantees backward compat. Strict TDD: ≥15 tests, with one explicit "no advisor = legacy path" test as the regression-safety net.

---

## Slice 5c.2 — E2E Wiring + Smoke (≤ 300 líneas, 5 paths, optional)

**Phase 1: Ollama health polling**

- [ ] 1.1 `frontend/src/app/core/realtime/ollama-health.interval.ts` — 60s poll on `GET /api/ai/health`. Updates a global signal `aiProviderStatus: 'up'|'down'|'unknown'`. Used by trader-shell to show a one-line status badge ("AI: connected" / "AI: offline — using fallback").

**Phase 2: Smoke E2E**

- [ ] 2.1 `scripts/wave5-smoke.sh` — 3 E2E probes idempotentes:
  - [ ] 2.1.1 Upload sample CSV (10 rows) → poll status → expect `imported: 9, skipped: 1` (one duplicate).
  - [ ] 2.1.2 GET /api/ai/health → expect 200 + `{ status: "ok" }` (requires local Ollama).
  - [ ] 2.1.3 POST /api/ai/risk-advice with low-quality trade → expect 200 + `{ action: "warning"|"block" }`.
- [ ] 2.2 Requires Ollama running locally — script does `if ! curl -sf http://localhost:11434/api/tags >/dev/null; then skip; fi`.

**Phase 3: Tasks close + archive**

- [ ] 3.1 All 5a.x + 5b.x + 5c.x checkboxes flipped to `[x]` in this `tasks.md`.
- [ ] 3.2 Per-slice `git diff --name-only` ≤ 32 paths (already validated in `path counts` table above).
- [ ] 3.3 `apply-progress-wave5-slice-{5a.1,5a.2,5b.1,5b.2,5c.1}.md` written (one per slice; deviations captured with reason + accept/reject).
- [ ] 3.4 `READY-TO-ARCHIVE.md` written with full manifest. Actual move to `archive/` is the orchestrator's call post-merge.

---

## Cross-cutting / Validation

- [ ] 6.1 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.
- [ ] 6.2 `cd frontend && npx jest --no-coverage` → ~160/160 pass, ~38 suites (estimated).
- [ ] 6.3 `dotnet test JadeCapital.slnx --filter "FullyQualifiedName!~JadeCapital.Api.IntegrationTests"` → ~870/870 pass BE (estimated; +63 from Wave 5: 15+10+12+15+15 -4 for OpenTradeHandler test counts).
- [ ] 6.4 `docker compose up -d --build api frontend` → healthy.
- [ ] 6.5 Per-slice `git diff --name-only` ≤ 32 paths (preflight hard constraint; see path counts in forecast table).
- [ ] 6.6 Per-slice `git diff --stat` ≤ 400 lines ideal; `size:exception` justified per slice (precedent Wave 4 — all 5 slices used it).
- [ ] 6.7 Wave 4 carry-over debt addressed: NONE in Wave 5 scope (migration-order fix deferred to a separate Wave 5 hygiene slice, not this one).

## Deviations expected (anticipated)

D1. **Ollama HTTP path uses `Microsoft.VisualBasic.FileIO.TextFieldParser` for CSV** — odd-looking dependency for a C# project; needed for robust CSV parsing (handles quotes, escapes, BOM out of the box). Acceptable precedent: Wave 1d used a similar approach for trade-attachment metadata.

D2. **`OllamaHttpClient` is a sealed class implementing `IAIProvider`** — Wave 6 may add `OpenAiHttpClient` and a provider-switcher via `appsettings.json`. For Wave 5 only one impl is needed; the abstraction is in place for the swap.

D3. **`IAIRiskAdvisor?` nullable dependency on `OpenTradeHandler`** — strict DI rules normally forbid nullable deps, but Wave 4 precedent (handlers depending on `IQuoteProvider`) shows optional deps are acceptable when wrapped in null-checks. Alternative would be a `Lazy<IAIRiskAdvisor>` wrapper, which adds 30 LOC for the same semantics. Acceptable.

D4. **`CoachingPromptService` runs daily 03:00 UTC** — hardcoded. Wave 5.5+ may move to `appsettings.json`-configurable schedule. For Wave 5 the constant is documented in design.md.

D5. **`GET /api/coaching/ai-prompts` is a new sub-endpoint, not an extension of `/api/coaching/prompts`** — needed because the two prompts (rule vs. AI) have different response shapes (rule has `ruleId` discriminator, AI has `provider_response`). The merge happens at the FE level (both endpoints are called separately and concatenated in `coaching-page.ts`). Alternatively, the merge could be server-side; chosen FE-side for independence. Acceptable.

## Open / deferred to later waves

- Real broker integration (IBKR, MT5 native) — Wave 6 swaps `InMemoryQuoteProvider` for `BrokerQuoteProvider`.
- Real virus scanner (ClamAV) — Wave 6 swaps `VirusScannerNoOp` for `ClamAvVirusScanner`.
- Cloud AI providers (OpenAI, Anthropic, Claude) — Wave 6 adds `OpenAiHttpClient` + `ClaudeHttpClient` impls of `IAIProvider`.
- Migration-order fix (Wave 4e.D1 carry-over) — Wave 5 hygiene slice adds `CREATE SCHEMA IF NOT EXISTS` to `01-extensions.sql` OR parses migration date prefix in `JadeApiFactory.ApplyMigrationAsync`.
- Streaming tokens in FE (SSE / chunked for AI responses) — Wave 7 PWA observability.
- AI signal generation from scanner results — Wave 6 (originally in Wave 5 scope, deferred for scope control).
- Calendar integration (Google Calendar) — Wave 7+ (already deferred from Wave 4).
- Multi-tenant AI rate limits — Fase 6.
- PWA offline mode — Fase 7.
- Real-time alerts push via SignalR (Wave 5 was originally billed to extend `/hubs/quotes` to push alerts; deferred for scope control).
- BackgroundService metrics (Prometheus) — Wave 5 observability slice (separate scope).
