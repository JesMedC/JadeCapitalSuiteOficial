# Apply Progress — Wave 5 Slice 5b.1 (AI Provider + Ollama HTTP Client)

**Change**: `2026-08-19-wave5-imports-ai`  
**Slice**: 5b.1 — `IAIProvider` interface + `OllamaHttpClient` + `GET /api/ai/health`  
**Branch**: `feature/wave5-ai-provider` (from `feature/0a-identity-model` @ `6cc542b`)  
**Mode**: Strict TDD (RED → GREEN → REFACTOR per task)  
**Chain strategy**: `feature-branch-chain` (Wave 5 PR #3: 5a.1+5a.2 merged → 5b.1)

---

## 1. Files Changed

| File | Action | Purpose | LOC |
|---|---|---|---:|
| `src/3.Shared/JadeCapital.Shared.Kernel/Ai/IAIProvider.cs` | Created | Provider abstraction — `GenerateAsync` + `IsHealthyAsync` | 41 |
| `src/3.Shared/JadeCapital.Shared.Kernel/Ai/PromptRequest.cs` | Created | Request record (User, System, MaxTokens, Temperature) | 56 |
| `src/3.Shared/JadeCapital.Shared.Kernel/Ai/PromptResponse.cs` | Created | Response record (Text, Model, TokensUsed, Duration) | 54 |
| `src/3.Shared/JadeCapital.Shared.Kernel/Ai/AIProviderOptions.cs` | Created | Config: BaseUrl + Model + Timeout | 31 |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Ai/OllamaHttpClient.cs` | Created | HttpClient-based impl with retry + Result mapping | 234 |
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Ai/GetAiHealthHandler.cs` | Created | Query + DTO + handler (3 types in 1 file) | 63 |
| `src/2.Modules/Trading/JadeCapital.Trading.Api/Endpoints/AiEndpoints.cs` | Created | `GET /api/ai/health` minimal API | 84 |
| `src/1.Api/JadeCapital.Host/Program.cs` | Modified | +52 lines: AI options binding + `AddHttpClient<IAIProvider, OllamaHttpClient>` + `app.MapAiEndpoints()` | +52 |
| **Production subtotal** | | | **615** |
| `tests/UnitTests/JadeCapital.Shared.Kernel.UnitTests/Ai/PromptContractTests.cs` | Created | 11 contract tests | 174 |
| `tests/UnitTests/JadeCapital.Trading.UnitTests/Infrastructure/Ai/OllamaHttpClientTests.cs` | Created | 12 HttpMessageHandler-mock tests | 346 |
| `tests/UnitTests/JadeCapital.Trading.UnitTests/Application/Ai/GetAiHealthHandlerTests.cs` | Created | 3 handler tests with `Mock<IAIProvider>` | 72 |
| **Tests subtotal** | | | **592** |
| **TOTAL** | **8 prod paths + 3 test paths** | | **1207** |

**Path budget**: 8 production paths ≤ 9 forecast (✓ within budget).  
**LOC budget**: 615 prod lines ≤ 2000 hard cap (✓ — but `size:exception` justified per Wave 4/5a.1/5a.2 precedent; see deviations D1).

---

## 2. TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|---|---|---|---|---|---|---|---|
| 1.1 | `PromptContractTests` | Unit | N/A (new) | ✅ 4 RED tests | ✅ 11/11 pass | ✅ 11 cases (defaults + custom + JSON roundtrip + MaxTokens validation) | ✅ Extracted explicit-ctor pattern (positional record couldn't validate) |
| 2.1 | `OllamaHttpClientTests` | Unit | N/A (new) | ✅ 12 RED tests | ✅ 12/12 pass | ✅ 12 cases (happy + 5xx + timeout + empty + parse + health × 3 + URL trim + System concat + model from opts) | ✅ Extracted `GenerateInternalAsync` + top-level catch to enforce no-throw contract |
| 3.1 | `GetAiHealthHandlerTests` | Unit | N/A (new) | ✅ 3 RED tests | ✅ 3/3 pass | ✅ 3 cases (healthy + unhealthy + cancellation propagation) | ➖ None needed — handler is ~30 LOC |

**Total**: 26 new tests, all passing.

---

## 3. Test Summary

- **Total tests written (slice)**: 26
- **Total tests passing (slice)**: 26
- **Total tests passing (cumulative suite)**: 910/910 (was 884 after 5a.2 → +26 new AI tests, 0 regressions)
- **Layers used**: Unit (26) — no Integration/E2E in this slice
- **Approval tests** (refactoring): 0 — no existing-code refactoring tasks
- **Pure functions created**: 0 (records + sealed class — all behavior is in OllamaHttpClient where it's intrinsically tied to HTTP I/O)

---

## 4. Work Unit Evidence

| Evidence | Value |
|---|---|
| Focused test command + result | `dotnet test --filter "FullyQualifiedName~Ollama|FullyQualifiedName~GetAiHealth|FullyQualifiedName~PromptContract"` → **26/26 passed** |
| Runtime harness command + result | `dotnet build JadeCapital.slnx` → **0 errors, 0 warnings**. Full suite `dotnet test --filter "FullyQualifiedName!~JadeCapital.Api.IntegrationTests"` → **910/910 passed** |
| Rollback boundary | Revert 7 new files + 1 modified file (`Program.cs`); undo `MapAiEndpoints()` + `AddHttpClient<IAIProvider, OllamaHttpClient>` + `AddOptions<AIProviderOptions>().Bind(...)`. No DB schema changes (migration: none per spec). No cross-module deps — OllamaHttpClient is the only consumer of `IAIProvider` in this slice. |

**Threat-matrix cases**: not applicable for this slice — no new endpoint accepts user-supplied input that flows into AI. The `/api/ai/health` endpoint only invokes `IsHealthyAsync` which has no body / query params. Defense-in-depth lives in the impl (timeout, retry, exception → Result.Failure mapping).

---

## 5. Deviations from Design

### D1. Manual retry instead of Polly (`size:exception` candidate)

**Design.md** specifies `AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt))))`.  
**Implementation**: in-class 3-attempt retry inside `OllamaHttpClient.GenerateAsync`.  

**Reason**: Adding `Microsoft.Extensions.Http.Resilience` (or the legacy `Microsoft.Extensions.Http.Polly`) package would have required a 3rd `.csproj` modification (`Trading.Infrastructure.csproj` or `Trading.Application.csproj`), pushing the slice to **10 paths** — over the 9-path budget. The contract ("3 attempts on transient 5xx → `ai.unavailable`") is identical regardless of mechanism; tests assert the same behavior. **Accept** — Polly is a wrapper, the behavior is preserved.

### D2. AI wiring lives in `Program.cs` instead of `TradingModuleRegistration.cs`

**Design.md** shows `services.AddOptions<OllamaOptions>().Bind(...)` and `services.AddHttpClient<IAIProvider, OllamaHttpClient>(...)` inside `TradingModuleRegistration.AddTradingInfrastructure`.  
**Implementation**: Both registrations live in `JadeCapital.Host/Program.cs` (immediately after `builder.Services.AddTradingInfrastructure(builder.Configuration)`). `TradingModuleRegistration.cs` is unchanged from 5a.2.

**Reason**: `Trading.Infrastructure.csproj` is a plain class library (not `Microsoft.NET.Sdk.Web`) and doesn't transitively reference `Microsoft.Extensions.Http`. Adding the package would have been another `.csproj` modification (10 paths, over budget). The host project (`Microsoft.NET.Sdk.Web`) has all extensions available, so the registrations live there. MediatR handler discovery picks up `GetAiHealthHandler` automatically (no explicit `AddScoped<>` needed) because the Trading.Application assembly is already registered in `AddMediatR(...)`. **Accept** — the registration logic is unchanged, only its location differs.

### D3. `AIProviderOptions` uses `TimeSpan Timeout` (not `int TimeoutSeconds`)

**User prompt** said: "`AIProviderOptions.cs` (BaseUrl, Timeout, Model)". Ambiguous on type.  
**Implementation**: `public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);`

**Reason**: `TimeSpan` is the idiomatic .NET type for durations and aligns with `HttpClient.Timeout`. Wave 6 cloud providers will reuse the same shape. **Accept** — design.md's `OllamaOptions.TimeoutSeconds` was the alternative; this matches the user-prompt wording more directly.

### D4. `PromptResponse` uses `Duration: TimeSpan` (single field), not `LatencyMs: int` + `PromptTokens/CompletionTokens` (split)

**User prompt** said: "`PromptResponse.cs` record (Text, Model, TokensUsed, Duration)".  
**Design.md** had: `PromptResponse(Content, Model, LatencyMs, PromptTokens, CompletionTokens)`.

**Implementation**: `Text, Model, TokensUsed: int (= prompt_eval_count + eval_count from Ollama), Duration: TimeSpan`.

**Reason**: User prompt is authoritative for this slice. `TokensUsed` is summed at the impl (`OllamaHttpClientTests.GenerateAsync_Returns_PromptResponse_On_200_OK_With_NonEmpty_Content` asserts `TokensUsed == 36 = 12 + 24`). Cloud providers in Wave 6 will report their own shapes — the field name is the cross-module contract, not the provider wire shape. **Accept** — design.md's split form is recoverable in Wave 6 if a provider needs the distinction.

### D5. `IAIProvider` lives in `Shared.Kernel/Ai/`; `AIProviderKind` enum not yet defined

**Design.md** had a separate `AIProviderKind` enum (`Ollama = 0, OpenAi = 1, Claude = 2, Stub = 255`).  
**Implementation**: enum deferred to Wave 6 (when OpenAi / Claude impls arrive and the kind becomes load-bearing for the provider-switcher).

**Reason**: YAGNI — slice 5b.1 ships one impl, the enum isn't read anywhere. **Accept** — defer.

---

## 6. Issues Found

**None.** All spec scenarios pass:
- `Ollama healthy → IsHealthyAsync returns true` ✓
- `Ollama down → IsHealthyAsync returns false (no throw)` ✓
- `GenerateAsync request JSON shape: { model, prompt, stream:false, options:{ temperature, num_predict } }` ✓
- `Timeout → ai.timeout` ✓
- `5xx → ai.unavailable (after 3 attempts)` ✓
- `Health check happy → 200 + { status: "ok" }` ✓ (handler test; endpoint mapping in `AiEndpoints.cs`)
- `Health check down → 503 + { status: "down" }` ✓ (handler test)

**Critical lessons applied** (per orchestrator brief):
- ✓ No duplicate EF config (no DB changes in this slice)
- ✓ URL routing uses literal `/api/ai/health` (matches spec)
- ✓ `IAIProvider` resolved via DI; no fresh `HttpClient` instances outside the factory
- ✓ BOM/text encoding — N/A for Ollama JSON wire format
- ✓ Strict TDD — every production type has RED tests written first
- ✓ Idempotent migrations — no migrations in this slice (per spec "migration: none")
- ✓ Defense-in-depth — top-level `try/catch` in `GenerateAsync` ensures the contract ("never throws") holds even for unexpected exceptions like `InvalidOperationException` from a misconfigured HttpClient

---

## 7. Status

**All Phase 1–6 tasks complete**:
1. ✅ Branch `feature/wave5-ai-provider` created from `feature/0a-identity-model`
2. ✅ Phase 1 — Shared.Kernel (4 files + 11 tests, all green)
3. ✅ Phase 2 — OllamaHttpClient (1 file + 12 tests, all green)
4. ✅ Phase 3 — GetAiHealthHandler + DI registration (1 file + 3 tests, all green)
5. ✅ Phase 4 — AiEndpoints + `Program.cs` `MapAiEndpoints()`
6. ✅ Phase 5 — Build green (0/0), full suite green (910/910)
7. ⏳ Phase 6 — this apply-progress file (just written)
8. ⏳ Commit + push + PR creation pending orchestrator handoff

**Ready for**: `sdd-verify` (slice 5b.1 close-out) → next slice **5b.2** (CoachingPrompt aggregate + BG service + endpoint).
