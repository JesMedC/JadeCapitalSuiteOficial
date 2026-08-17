# Wave 5 — Apply Progress (Slice 5a.2 — MT4/MT5 Importer)

**Change**: `2026-08-19-wave5-imports-ai`
**Slice closed**: 5a.2 (MT4/MT5 Parser + format auto-detection)
**Closed by**: SDD orchestrator (apply sub-agent)
**Date**: 2026-08-17
**Branch**: `feature/wave5-importer-mt4` (branched from `feature/0a-identity-model` at HEAD `3205790`)
**PR base**: `feature/0a-identity-model` (per Wave 5 `feature-branch-chain` convention)
**Strict TDD**: ON (RED → GREEN → REFACTOR per phase)
**Delivery strategy**: `auto-chain` (Wave 5 chain: 5a.1 merged → 5a.2 → 5b.1 → 5b.2 → 5c.1 → 5c.2)

---

## Slice 5a.2 — APPLIED

### Verification

| Check | Result |
|---|---|
| `dotnet build JadeCapital.slnx --nologo --verbosity minimal` | 0 errors, 0 warnings |
| `dotnet build JadeCapital.slnx --nologo --verbosity minimal -p:TreatWarningsAsErrors=true` | 0 errors, 0 warnings (regression-checked) |
| `dotnet test --filter "FullyQualifiedName~Mt4\|FullyQualifiedName~Import"` | 77/77 pass (22 new + 55 existing) |
| `dotnet test --filter "FullyQualifiedName!~JadeCapital.Api.IntegrationTests"` (full BE suite) | 884/884 pass |
| `dotnet test JadeCapital.Shared.Kernel.UnitTests` | 111/111 pass |
| `dotnet test JadeCapital.Trading.UnitTests` | 588/588 pass (566 baseline + 22 new) |
| `dotnet test JadeCapital.Identity.UnitTests` | 163/163 pass |
| `dotnet test JadeCapital.Billing.UnitTests` | 22/22 pass |
| FE tests | unchanged (no FE changes in 5a.2) |

### Test counts (5a.2-specific)

| Layer | Tests | Path |
|---|---:|---|
| Trading.Infrastructure — `Mt4ImportRowParser` (RED-first) | 16 | `tests/UnitTests/JadeCapital.Trading.UnitTests/Infrastructure/Mt4ImportRowParserTests.cs` |
| Trading.Application — `ImportParserDispatcher` (RED-first) | 6 | `tests/UnitTests/JadeCapital.Trading.UnitTests/Application/Imports/ImportAutoDetectionTests.cs` |
| **Total 5a.2 tests** | **22** | (+ 862 existing BE tests still green) |

#### Mt4ImportRowParserTests (16 cases)

| # | Test | Phase |
|---|---|---|
| 1 | `Format_Returns_Mt4` | Format contract |
| 2 | `CanParse_Returns_High_Score_For_MT4_Header` | Tab-delimited MT4 signature |
| 3 | `CanParse_Returns_High_Score_For_MT5_Header` | Tab-delimited MT5 signature |
| 4 | `CanParse_Returns_Low_Score_For_Generic_Csv_Header` | Distinguishing marker gate |
| 5 | `CanParse_Returns_Zero_For_Empty_Stream` | Defensive — null head |
| 6 | `Parses_Simple_MT4_Single_Trade` | Happy path |
| 7 | `Parses_MT4_500_Trades_Quickly` | Parity with reference data |
| 8 | `Handles_Open_MT4_Trade_With_No_Close` | Status = Open path |
| 9 | `MT4_Sell_Direction_Maps_To_Short` | Direction mapping |
| 10 | `Parses_MT5_Single_Entry_Deal_As_Open_Position` | MT5 single-deal → open |
| 11 | `Parses_MT5_Two_Deals_For_Same_Position_Aggregates_Into_One_Row` | MT5 aggregation |
| 12 | `MT5_Positions_With_Different_PositionIds_Remain_Separate_Rows` | MT5 multi-position |
| 13 | `Handles_5_Digit_FX_Quotes_Without_Loss` | Decimal precision |
| 14 | `Ignores_Freeform_Comment_Column_For_Prompt_Injection_Defense` | Defense-in-depth |
| 15 | `Normalizes_All_MT4_Dates_To_UTC` | UTC invariant |
| 16 | `Parses_MT4_With_BOM_Header` | UTF-8 BOM stripping (5a.1 lesson) |

#### ImportAutoDetectionTests (6 cases)

| # | Test | Phase |
|---|---|---|
| 1 | `SelectParser_MT4_File_Returns_Mt4_Parser` | Real MT4 header |
| 2 | `SelectParser_MT5_File_Returns_Mt4_Parser_Since_Single_Impl_Handles_Both` | MT5 → single parser |
| 3 | `SelectParser_CSV_File_Returns_Csv_Parser` | Real CSV header |
| 4 | `SelectParser_Unknown_Format_Returns_Null` | JSON → 422 `import.format_unrecognized` |
| 5 | `SelectParser_Ambiguous_First_Registered_Wins` | DI order tiebreaker |
| 6 | `SelectParser_Score_Below_Threshold_Is_Skipped` | 0.5 < 0.8 threshold |

### Code surface (created + modified)

- **Trading.Infrastructure** (1 file created, ~498 LOC):
  - `Imports/Mt4ImportRowParser.cs` — single impl for MT4 + MT5 (distinguishing markers: `SL`/`TP` for MT4, `Position ID` for MT5). MT5 mode buffers deals and aggregates by `Position ID`. BOM stripping. UTC date normalization. Prompt-injection defense via dropped `Comment` column.

- **Trading.Application** (1 file created, 1 file modified, ~140 LOC):
  - `Features/Imports/ImportParserDispatcher.cs` — pure static helper. `SelectParser(parsers, fileName, head)` returns first parser with `CanParse >= 0.8` (threshold constant). Defensive try/catch — a buggy parser never breaks the dispatcher.
  - `Features/Imports/StreamImportService.cs` (extended) — new `ExecuteAsync(job, body, IEnumerable<IImportRowParser> parsers, fileName, ct)` overload. Buffers the body into a seekable `MemoryStream`, calls the dispatcher, fails-fast with `import.format_unrecognized` if no parser matches.

- **Trading.Api** (1 file modified, ~70 LOC effective):
  - `Endpoints/ImportEndpoints.cs` (modified) — `DetectFormat` now resolves `IEnumerable<IImportRowParser>` from DI and runs the dispatcher (replaces 5a.1 filename heuristic). Returns 422 `import.format_unrecognized` if no parser matches. `RunImportInBackgroundAsync` resolves the same collection and calls the new overload.

- **Trading.Infrastructure DI** (1 file modified, +5 LOC):
  - `DependencyInjection/TradingModuleRegistration.cs` — `Mt4ImportRowParser` registered BEFORE `CsvImportRowParser` (per-spec precedence: more-specific signatures win; dispatcher tiebreaker).

- **Tests** (2 files created, ~448 LOC):
  - `Mt4ImportRowParserTests.cs` — 16 scenarios (exceeds 15 forecast by 1; BOM-stripping added per 5a.1 lesson).
  - `ImportAutoDetectionTests.cs` — 6 scenarios (exceeds 5 forecast by 1; sub-threshold-score test added per the threshold constant).

### Budget check

| Metric | Value |
|---|---:|
| Production code (new + modified) | ~720 lines |
| Test code (new) | 448 lines |
| **Net diff** | **1,159 lines added, 25 deleted → 1,134 net** |
| Forecast (tasks.md) | ~500 lines |
| Budget cap (per Wave 4 precedent) | 2,000 lines |
| Status | **OVER BUDGET — `size:exception` justified** |
| Path count (created + modified) | **7** (4 created + 3 modified) |
| Path forecast (tasks.md) | 6 |
| Path limit | 32 |

#### `size:exception` justification

1. **Strict TDD overhead** (~50% of diff, same as Wave 4):
   - 448 test lines / 720 prod lines = 62% (Wave 4 4b was 51%, 4d was 47%). The MT4 parser test surface is large because the parser handles two distinct formats (MT4 + MT5) with different aggregation rules, plus the dispatcher's threshold edge case.

2. **Mt4ImportRowParser complexity** (498 LOC):
   - Distinguishing-marker scoring (`SL`/`TP` for MT4, `Position ID` for MT5) — prevents generic CSV from being mis-detected as MT4 (early GREEN phase failed at this exact boundary; the strict-marker gate was the fix).
   - Tab + comma dual-delimiter support (TextFieldParser with `Delimiters = ["\t", ","]`).
   - MT5 deal aggregation: buffer all deals, group by `Position ID`, emit one `ImportRow` per position with entry deal (first) + exit deal (last). First/Last indexing is the simplest deterministic aggregation.
   - BOM stripping (5a.1 lesson — `Microsoft.VisualBasic.FileIO.TextFieldParser` does not strip the BOM itself).
   - Robust date parsing (MT4 standard `yyyy.MM.dd HH:mm:ss` + ISO-ish + last-resort generic).
   - Prompt-injection defense: `Comment` column is dropped, never propagated to `ImportRow.Notes`.

3. **StreamImportService overload** (78 LOC):
   - Body-buffering into seekable `MemoryStream` (multipart bodies are already buffered in memory by ASP.NET, max 10 MiB per `ImportJob.MaxFileSizeBytes`).
   - Head-snapshot for sniffing (first KiB into a separate `MemoryStream`, then rewind body to position 0).
   - Fail-fast `import.format_unrecognized` path that marks the job `Failed` and persists the error.

4. **Wave 4 + 5a.1 precedent**:
   - Wave 4: 4a=1471, 4b=1355, 4c=1994, 4d=2753 — all accepted with `size:exception`.
   - Wave 5 5a.1: 2,108 prod LOC — `size:exception` accepted. Same precedent extends to 5a.2.
   - 5b.1/5b.2/5c.1/5c.2 follow the same model.

5. **Sub-cutting analysis**:
   - 5a.2.a (MT4 parser only, no MT5): would save ~150 LOC but break the spec requirement to handle MT5 deals aggregation (Position ID grouping).
   - 5a.2.b (auto-detection only, no MT4 parser): would skip the actual implementation — useless on its own.
   - Verdict: natural boundary is "MT4/MT5 parser + auto-detection" as defined in tasks.md.

### Deviations from spec (deliberate, ACCEPTED)

#### D1. CanParse score uses strict distinguishing markers instead of plain token frequency

- **Spec said** (tasks.md §5a.2 Phase 1.1): "CanParse returns 0.9+ for MT4 header / 0.9+ for MT5 header".
- **Actual**: MT4 parser requires BOTH `SL` and `TP` columns before returning a non-zero score; MT5 parser requires `Position ID`. Without these guards, the generic CSV header (which contains 9 of 11 MT4 tokens like `ticket`, `open time`, `type`, `volume`, `symbol`, `open price`, `close time`, `profit`) scored 0.818 — just above the 0.8 threshold — and would have stolen CSV files from `CsvImportRowParser`. The strict-marker gate was the RED-failure-driven fix.
- **Why accepted**: matches the Wave 4 lesson ("Use real distinguishing signals, not token-frequency heuristics that overlap with adjacent formats"). The spec score (`>= 0.8`) is still met — MT4 headers score 1.0, MT5 headers score 1.0, generic CSV scores 0.0.
- **Action**: documented in the parser's class-level comment + the distinguishing-marker constants.

#### D2. Auto-detection `ImportParserDispatcher` is a static helper, not a class injected via DI

- **Spec said** (design.md §"Streaming import"): dispatcher picks first parser with `CanParse >= 0.8`.
- **Actual**: implemented as `public static class ImportParserDispatcher` with a single `SelectParser(parsers, fileName, head)` method.
- **Why accepted**: the dispatcher is pure logic — no state, no DI dependencies. A static helper is testable (the tests exercise it directly with synthetic streams + mock parsers), avoids a needless DI registration, and matches the precedent set by `DomainGuard` and `MoneyErrors` in Shared.Kernel. The streaming service consumes the dispatcher via direct method call.
- **Action**: none — the design is the cleanest fit for the slice's scope.

#### D3. `ImportJob.InstrumentId` resolution (5a.1 D2) deferred to 5c.2 — NOT closed in 5a.2

- **5a.1 deviation D2**: `TradeImportExtensions.ImportFromRow` uses `Guid.NewGuid()` for `instrumentId`, which the DB FK rejects. The 5a.1 apply-progress notes that 5a.2 was a candidate for closing this.
- **5a.2 actual**: `IInstrumentRepository.FindBySymbolAsync` was already wired in `StreamImportService.PersistBatchAsync` by 5a.1 (line 215 in the current code) — symbol resolution happens BEFORE `ImportFromRow`. The 5a.1 deviation is therefore **already closed** at the streaming-pipeline level; no new code needed in 5a.2.
- **Why noted here**: the 5a.1 apply-progress listed this as a possible 5a.2 follow-up. The audit confirmed it's a non-issue (already done in 5a.1). Re-flagging here so the 5c.2 reviewer doesn't re-investigate.

#### D4. `GET /api/imports` list endpoint (5a.1 D4) deferred to 5c.2 — NOT closed in 5a.2

- **5a.1 deviation D4**: list endpoint deferred per scope control.
- **5a.2 actual**: no list endpoint added. 5a.2 is parser-only; list endpoint is a thin repository wrapper that lands naturally alongside the smoke probes in 5c.2.
- **Why accepted**: matches the 5a.1 deviation note.

### Wave 4 + 5a.1 lessons applied

1. **No duplicate EF config registration** — no new EF entity in 5a.2. `Mt4ImportRowParser` is a pure infrastructure parser (no EF mapping).
2. **URL `{id:guid}` used correctly** — endpoint URL unchanged (`/api/imports/csv`); the auto-detection is internal. No new endpoint.
3. **Use `GetByIdAsync` for single-fetch** — no new repository method.
4. **Resolve `InstrumentId` via `IInstrumentRepository.FindBySymbolAsync`** — already wired in `StreamImportService.PersistBatchAsync` (5a.1); no new code. D3 above.
5. **Strip BOM in CSV/text parsers** — `Mt4ImportRowParser.ParseAsync` strips UTF-8 BOM before handing the stream to `TextFieldParser` (the parser does not strip it itself). Tested by `Parses_MT4_With_BOM_Header`.
6. **Strict TDD** — RED → GREEN → REFACTOR per phase. All 22 new tests written BEFORE the production code. The distinguishing-marker gate was a RED-failure-driven fix (4 → 16 iterations of the parser to reach GREEN).
7. **Idempotent migrations** — no new migration in 5a.2 (parser-only slice).
8. **Defense-in-depth** — `ImportParserDispatcher.SelectParser` wraps `CanParse` in try/catch (a buggy parser never breaks the dispatcher); `Mt4ImportRowParser` swallows per-row exceptions and continues streaming (matches `CsvImportRowParser` precedent).

### TDD Cycle Evidence

| Phase | Test File | Layer | RED | GREEN | REFACTOR |
|---|---|---|---|---|---|
| 1 (Mt4 parser) | `Mt4ImportRowParserTests.cs` | Unit | Written (16 cases) | Iterated (1 fix: strict-marker gate to prevent CSV/Mt4 ambiguity) | Clean (no further changes) |
| 2 (Auto-detection) | `ImportAutoDetectionTests.cs` | Unit | Written (6 cases) | Passed first iteration | Clean (DRY in `ImportEndpoints.DetectFormat` — collapsed two near-identical seekable/non-seekable branches into one) |

### Build & test gates passed

- `dotnet build JadeCapital.slnx --nologo --verbosity minimal -p:TreatWarningsAsErrors=true` → 0 errors / 0 warnings.
- `dotnet test` (full BE suite, integration excluded) → 884/884 pass.
- `dotnet test --filter "FullyQualifiedName~Mt4|FullyQualifiedName~Import"` → 77/77 pass (22 new + 55 existing).
- No `dotnet-ef migrations add` divergence (no new migration).
- `git diff --name-only` = 7 paths (under 32 limit).

### Workload / PR Boundary

- **Mode**: chained PR slice (5a.2 in the Wave 5 `feature-branch-chain`).
- **Current work unit**: MT4/MT5 parser + auto-detection (slice 5a.2 scope only).
- **Boundary**: starts from `feature/0a-identity-model` HEAD `3205790` (5a.1 merged); ends with `Mt4ImportRowParser` + dispatcher + endpoint changes; does NOT touch 5b/5c work.
- **Net diff**: 1,134 LOC. `size:exception` justified (same precedent as 5a.1 = 2,108; well under 2,000 cap).

### Next slice

- **5b.1 — AI Provider Interface + Ollama HttpClient** (≈ 500 LOC, 9 paths):
  - `IAIProvider` interface in `Shared.Kernel/Ai/`.
  - `OllamaHttpClient` impl in `Trading.Infrastructure/Ai/`.
  - `GET /api/ai/health` endpoint.
  - Tests with `HttpMessageHandler` mock.
  - Will be the first slice that adds a `Microsoft.Extensions.Http` registration (`AddHttpClient<IAIProvider, OllamaHttpClient>` with Polly retry).

### Files changed (final list)

```
src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Imports/ImportParserDispatcher.cs  [NEW, 61 LOC]
src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Imports/StreamImportService.cs    [MODIFIED, +78 LOC]
src/2.Modules/Trading/JadeCapital.Trading.Api/Endpoints/ImportEndpoints.cs                       [MODIFIED, +64/-25 LOC]
src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs  [MODIFIED, +9/-1 LOC]
src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Imports/Mt4ImportRowParser.cs          [NEW, 498 LOC]
tests/UnitTests/JadeCapital.Trading.UnitTests/Application/Imports/ImportAutoDetectionTests.cs   [NEW, 116 LOC]
tests/UnitTests/JadeCapital.Trading.UnitTests/Infrastructure/Mt4ImportRowParserTests.cs         [NEW, 332 LOC]
```

Total: **7 paths** (4 new + 3 modified). 1,159 inserted, 25 deleted → **1,134 net LOC**.
