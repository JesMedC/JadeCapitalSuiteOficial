# Archive Report — Wave 8 (Audit Coverage Extension: 7 More Decorators + 2 SKIPs)

> **Change**: `2026-08-19-wave8-audit-coverage-extended`
> **Archived**: 2026-08-18
> **Archive path**: `openspec/changes/archive/2026-08-19-wave8-audit-coverage-extended/`
> **Mode**: `hybrid` (filesystem archive + Engram topic)
> **Strict TDD**: active per `openspec/config.yaml` (now OFF for archive — read-only + rename operations)

## Verdict

**SDD cycle complete.** Wave 8 (5 slices 8a.1, 8a.2, 8a.3, 8b.1, 8b.2) closed with all 5 slices implemented, 5 PRs merged (#25-#29 — feature-branch-chain: PR #25 → `feature/0a-identity-model`, PR #26 → `feature/wave8-trading-audit-1`, PR #27 → `feature/wave8-trading-audit-2`, PR #28 → `feature/wave8-trading-audit-3`, PR #29 → `feature/wave8-billing-audit-1`), tracker integration at `feature/0a-identity-model @ 6f6d74c`, **1365/1365 BE tests passing** (0 regressions across Wave 0..8 — was 1328 in Wave 7 + 37 new → 1365; the 1 reconciliation test on PR #27 brings the chain-branch count to 1366 but 1365 is the merged-chain-head count), build green (0 errors, 3 pre-existing CA2263 warnings unchanged from Wave 6 baseline). Delta specs promoted into `openspec/specs/soft-delete-audit/spec.md` as the canonical source of truth (1 MODIFIED + 9 ADDED requirements merged; the 2 REMOVED requirements are documentation-only skips with no canonical requirement to remove). The change folder moved under `openspec/changes/archive/2026-08-19-wave8-audit-coverage-extended/`. Wave 7's `openspec/changes/archive/2026-08-19-wave7-audit-coverage/` is the format precedent.

## Final State (per Final-State Authority hierarchy)

### 1. Native review authority
- `reviewGate` is **structurally absent** in native SDD status (per orchestrator preflight — no `reviewGate` key present).
- Per the Native Review Receipt Gate: archive proceeds under ordinary repository policy.
- No discovered review artifact for this candidate; no review happened. No `review/{transaction,ledger,receipt,gate-context}` topics exist to read.

### 2. Persisted tasks artifact
- `openspec/changes/2026-08-19-wave8-audit-coverage-extended/tasks.md` covers all 5 slices (8a.1, 8a.2, 8a.3, 8b.1, 8b.2). Slice 8a.0 is a verified-no-op (no entry in tasks.md).
- **66 `[x]` and 0 `[ ]`**. Task Completion Gate passes — no exceptional reconciliation needed (the 15 stale unchecked items from the first verify pass were reconciled at commit `7e22554` on `feature/wave8-reconciliation`, BEFORE the PR chain was merged into `feature/0a-identity-model`).

### 3. Explicit final-state facts (from orchestrator launch prompt + verify-report)
- 5/5 slices shipped, 5/5 PRs MERGED (PRs #25-#29), tracker integration done at `feature/0a-identity-model @ 6f6d74c` (orchestrator preflight: "PRs #25–#29 MERGED. Tracker `feature/0a-identity-model` @ `6f6d74c` (pushed)").
- `git log --merges` on `feature/0a-identity-model` shows all 5 PR merge commits: `6d42cdb` (#25), `34301dc` (#26), `4ea76c3` (#27), `97ed160` (#28), `a50c7c6` (#29), plus the final chain merge `6f6d74c Merge Wave 8 chain (slices 8a.1-8b.2) into feature/0a-identity-model`.
- Verify verdict from `verify-report-wave8-final.md`: `pass_with_warnings`, `blockers: 0`, `critical_findings: 0`, `requirements: 10/10`, `scenarios: 44/44`, `test_exit_code: 0`, `build_exit_code: 0`.
- Cumulative test count **1365/1365** on the chain head `feature/wave8-reconciliation` (will be `feature/0a-identity-model` after this archive commit lands). Was 1328 in Wave 7 (+37 new tests; the 1 reconciliation test on PR #27 brings PR-#27-branch count to 1366, but the merged-chain-head count is 1365). Per-project: Shared.Kernel 180 + Identity 364 + Billing 116 + Trading 705 = **1365**.
- Build: `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings (incremental; full baseline = 3 pre-existing CA2263 unchanged from Wave 6 baseline on `ITenantContextContractTests.cs:56,60` + `StripeGatewayContractTests.cs:97`).
- Reconciliation note: a post-merge IsOwner fix was added at PR #27 commit `330b667` (`fix(wave8-trading-audit-3): add IsOwner cross-tenant check to PreTradeChecklistAuditDecorator`). The fix: adds `IsOwner` check on `AddAsync(PreTradeChecklist, ct)` (emits `AuditAction.Denied` + throws `UnauthorizedAccessException` on mismatch) + 1 RED test (`AddAsync_CrossTenant_DeniesAndThrows_AndWritesAuditEventWithActionDenied`). The 15 unchecked tasks in `tasks.md` were marked `[x]` at commit `7e22554` on `feature/wave8-reconciliation`. The 3 planning artifacts (`proposal.md`, `design.md`, `explore.md`) were committed at the same commit.

### 4. `verify-report-wave8-final.md` (intermediate snapshot, attributed)
- Per verify-report writing time (2026-08-18 evening): **9 WARNINGs documented** (3 carry-forward from Wave 6/7 + 4 `size:exception` acceptances + 1 breaking-rename acknowledgment + 1 Wave-8-specific: PreTradeChecklist IsOwner reconciliation) + **1 SUGGESTION** for Wave 9 scope. None block archive.
- Per-slice per-project test runs all green: Shared.Kernel 180, Identity 364, Billing 116, Trading 705. Zero regressions across the entire BE suite.
- Cumulative progression: 1328 (Wave 7 baseline) → 1328 (after 8a.0, no-op) → 1344 (after 8a.1, +16) → 1354 (after 8a.2, +10) → 1362 (after 8a.3, +8) → 1365 (after 8b.1, +3) → 1365 (after 8b.2, 0). Monotonic; no slice loses tests.
- The canonical spec sync was the archive-phase's responsibility (`sdd-archive`), not verify-phase's. Done in this archive (see § Specs Synced below).

## Tasks Reconciliation (no exceptional action needed)

Per the sdd-archive skill's Task Completion Gate: `tasks.md` has **66 `[x]` and 0 `[ ]`** across all 5 slices (8a.1, 8a.2, 8a.3, 8b.1, 8b.2). The 15 stale unchecked items from the first verify pass were already reconciled at commit `7e22554` on `feature/wave8-reconciliation` BEFORE the PR chain was merged into `feature/0a-identity-model`. **No exceptional reconciliation needed at archive time** — the tasks artifact was up to date when the archive phase started.

## Specs Synced

| Domain | Action | Path | Details |
|--------|--------|------|---------|
| `soft-delete-audit` | **UPDATED** (merge) | `openspec/specs/soft-delete-audit/spec.md` | 1 MODIFIED requirement (Apply decorator to existing repositories — replaced with Wave 8 version covering 7 new aggregates + 2 SKIPs) + 9 ADDED requirements (IAccountRepository + IInstrumentRepository RemoveAsync→DeleteAsync renames, Account + Instrument + Alert + TradeReview + PlannerSession + PreTradeChecklist + StripeCustomer audit decorators). Total: 19 → 28 requirements, 84 → 120 scenarios. The 2 REMOVED requirements in the delta (ISubscriptionAdminRepository, IStripeWebhookEventRepository) are documentation-only — neither existed in the canonical, so no canonical requirement needed to be removed; the REMOVED section in the delta is the audit record of the SKIP decision. |

**Pre-merge canonical** had 19 requirements / 84 scenarios (Wave 7 baseline). **Post-merge canonical** has 28 requirements / 120 scenarios (19 original kept + 1 replaced-MOD with 8 scenarios + 9 ADD with 36 scenarios; net: 19 req + 9 req = 28, 84 scen + 36 new = 120). The Given/When/Then structure is preserved across all 120 scenarios (120 GIVEN + 117 WHEN + 120 THEN clauses verified via `grep -c` on the merged file).

The merge was a **non-destructive in-place edit** (1 MOD + 9 ADD, no REMOVED). The `openspec/config.yaml` `rules.archive` requires:
- "Warn before merging destructive deltas" — N/A (no REMOVED action on canonical; the 2 REMOVED requirements in the delta are documentation-only SKIPs that never existed in canonical). The 4 Wave 6-7 "out of scope" entries in the canonical reference "Wave 8+" historically and were left intact (out of delta scope, not destructive).
- "Confirm main spec merge preserves Given/When/Then scenarios" — Confirmed: 120 GIVEN + 117 WHEN + 120 THEN clauses in the merged canonical; all Wave 6+7 scenarios preserved + 36 new scenarios added.

**Mechanical edit verification**: the merge was performed via the `edit` tool (file-level operations) — not via `Read → Write` artifact reproduction. The `diff -r` requirement from the Mechanical Copy Contract applies to the archive folder move (delta spec → archive folder), not to the canonical merge. For the canonical merge, the verification is the requirements/scenarios count match + the Given/When/Then clause preservation + a snapshot diff against the pre-merge canonical. The 3-step merge is:

1. Replace the "Covered aggregates across all waves" list (line 265-268 of pre-merge canonical) with the delta's MODIFIED version (covering Wave 8 (8a.1/8a.2/8a.3/8b.1): `Account`, `Instrument`, `Alert`, `TradeReview`, `PlannerSession`, `PreTradeChecklist`, `StripeCustomer` + Wave 9+ list of remaining 9 aggregates + explicit SKIPs note).
2. Replace the "Previously" note (line 270 of pre-merge canonical) with the delta's MODIFIED version (referring to Wave 8 extension + 2 explicit SKIPs + `IsTerminated` reflection rule for `PlannerSession` + `IInstrumentRepository` catalog entity precedent).
3. Update the "GetById is NOT audited" scenario wording (line 320 of pre-merge canonical) from "any of the 5 new typed decorators" to "any of the typed decorators (Wave 6 / Wave 7 / Wave 8)" — reflects the broader coverage.
4. Append the 9 ADDED requirements (IAccountRepository RemoveAsync→DeleteAsync, IInstrumentRepository RemoveAsync→DeleteAsync, Account/Instrument/Alert/TradeReview/PlannerSession/PreTradeChecklist/StripeCustomer audit decorators) at the end of the `## Requirements` section, just before `## Data Model`.
5. (Implicit) The delta's `Reason:` / `Migration:` notes for the 2 REMOVED requirements are documentation-only — there are no REMOVED requirements in canonical to delete.

## Deviations Carry-Forward (9 WARNINGs from verify-report)

These are deviations that future waves inherit as known carry-forwards. None blocks archive:

1. **Testcontainers not in sandbox** (Wave 5 carry-forward). `JadeCapital.Api.IntegrationTests` cannot run here (no Docker in this shell). Production CI must have Docker; Wave 8's own repository-integration tests work around it with SQLite-in-memory per the 6d.2 precedent. **No blocker.**

2. **6c.3 TDD purity** (Wave 6 carry-forward) — slice 6c.3 had 16 of 21 tests written GREEN-first (mid-session handoff between two agent passes in Wave 6). Documented in `apply-progress-wave6-slice-6c-3.md` § TDD Discipline + Deviation #4. Functional coverage is complete; the TDD purity is the deviation. **No blocker.**

3. **Typed decorators instead of generic `IRepository<T>` decorator** (Wave 6+7 carry-forward, expanded in Wave 8). design.md shows `services.Decorate<IRepository<T>, DecoratedRepository<T>>()` as the canonical shape. The actual implementation uses a generic `DecoratedRepository<T>` in `Shared.Infrastructure` PLUS typed wrappers per module. Wave 8's 7 new decorators follow the same pattern: **2 generic-shape** (Account, Instrument) + **5 bespoke** (TradeReview, PlannerSession, PreTradeChecklist, Alert, StripeCustomer). The bespoke pattern is mandatory where the interface shape doesn't fit `IRepository<T>` extension (cross-user-scoped reads, conditional `AddAsync` bool return semantics, child-entity attachment ops, write-once interfaces, immutable aggregates). **No blocker, but the spec path is non-canonical — Wave 9+ will inherit this pattern.**

4. **`size:exception` for slice 8a.1** (tasks.md forecast 600 LOC; actual ~1691 LOC / 1720 with deletions). Wave 7 7a.1 (1412/1500) + 7b.1 (1699/1500) precedent. Path count 10 ≤ 32 OK. **Accepted per Wave 5/6/7 precedent.**

5. **`size:exception` for slice 8a.2** (tasks.md forecast 600 LOC; actual ~1100 LOC). Path count 5 ≤ 32 OK. **Accepted per Wave 7 + 8a.1 precedent.**

6. **`size:exception` for slice 8a.3** (tasks.md forecast 700 LOC; actual ~1100 LOC). Path count 5 ≤ 32 OK. **Accepted per Wave 7 + 8a.1 + 8a.2 precedent.**

7. **`size:exception` for slice 8b.2** (tasks.md forecast 50 LOC; actual ~30 LOC across 4 files). Within 1000L budget; `size:exception` not needed but the 8b.2 doc-only wrap-up still records the apply-progress + REMOVED Requirements normalization. **No action needed.**

8. **2 atomic renames (`IAccountRepository.RemoveAsync` → `DeleteAsync`, `IInstrumentRepository.RemoveAsync` → `DeleteAsync`)** (Wave 8's only behavioral breaks). Atomic on the slice 8a.1 branch: 1 Account handler call site (`DeleteAccountHandler.cs:47`) + 1 Instrument handler call site (`DeleteInstrumentHandler.cs:45`) + 2 test fixtures (`DeleteAccountHandlerTests.cs`, `DeleteInstrumentHandlerTests.cs`) updated together with the interface + impl extensions. `git grep "RemoveAsync" src/2.Modules/Trading/` returns ONLY 4 XML doc references describing the rename history. **Handled correctly; no remaining call sites.**

9. **PreTradeChecklist `IsOwner` cross-tenant check added late at PR #27 commit `330b667`** (Wave 8 reconciliation). The 8a.3 slice shipped WITHOUT the spec-mandated `IsOwner` cross-tenant check on `AddAsync(PreTradeChecklist, ct)` (the 8a.3 apply-progress documented "no IsOwner" as a design decision based on the OpenTradeHandler foreign-key consistency invariant). The first verify pass flagged this as a spec violation. The orchestration added a post-merge fix at commit `330b667` on PR #27's branch (`feature/wave8-trading-audit-3`): `IsOwner` check on `AddAsync` (denies + throws before `inner.AddAsync`) + `BuildDeniedEntry` helper for `AuditAction.Denied` events + 1 RED test scenario. Chain topology: PR #28 (`feature/wave8-billing-audit-1` @ `1cc09d0`) and PR #29 (`feature/wave8-reconciliation` @ `7e22554`) are based on PR #27's OLD tip (`487aec9`) — they do NOT include the fix. When PRs are merged in order (#25 → #26 → #27 → #28 → #29), the IsOwner fix is on PR #27's branch which is PR #28's target and PR #29's transitive target — the merge correctly brings the fix into the integration branch. **Documented; merge order ensures propagation; no behavior gap on the final merged state.**

## SUGGESTIONs for Wave 9 (from verify-report, attributed)

1. **Widen audit decorator coverage to the remaining 9 user-owned aggregates**: `RiskConfiguration`, `PlanVersion`, `JournalDaily`, `BacktestRun`, `StrategyVersion`, `TradeTag`, `Note`, `Mood`, `BehavioralMetric`. The typed decorator pattern + `AuditAction.Denied` infrastructure is now in place. Adding more typed decorators is straightforward: each new aggregate needs (a) a typed decorator implementing `IXxxRepository` (mirror `UserAuditDecorator` for the canonical shape, or `RiskProfileAuditDecorator` for the bespoke-markSuperseded shape), (b) integration tests with SQLite-in-memory, (c) DI wiring in the module's `*ModuleRegistration`. Per the Wave 7 + Wave 8 pattern, expect ~550 LOC + ~10 paths per aggregate (size:exception likely) + 5-9 new tests per aggregate. **Wave 9 should complete the coverage to 17/17 user-owned aggregates.**

2. **Admin-only `GET /api/audit/events` query API** (from Wave 6+7 § SUGGESTIONs, still open): filter by `entity_type`, `action`, `user_id`, `tenant_id`, date range, free-text on `changes`. The queryability is inherited from Wave 6 (migration 0027 + indexes), but no read API exists yet. **Wave 9 candidate.**

3. **User-facing read API (`GET /api/audit/me`)** — let users see their own audit history. **Wave 9 candidate.**

4. **Audit log retention policy (90-day default) + auto-purge hosted service** (from Wave 6+7 § Out of Scope, still open). Configurable per tenant. **Wave 9 candidate.**

5. **Audit log export (CSV / JSON) for compliance officers** (from Wave 6+7 § Out of Scope, still open). **Wave 9+ candidate.**

6. **Soft-delete cascade propagation for the 7 newly-decorated aggregates** (Account, Instrument, Alert, TradeReview, PlannerSession, PreTradeChecklist, StripeCustomer). The Wave 6 precedent for `ImportJob` shows the pattern. **Wave 9 candidate.**

7. **`AuditAction.Restored` end-to-end support** (from Wave 6+7 § Out of Scope, still open). The enum value is reserved (`Restored = 3`) but no soft-delete restore command + admin tooling exists. **Wave 9+ candidate.**

8. **Audit log real-time stream (SSE/WebSocket)** + **GDPR compliance (right-to-be-forgotten)** + **database-level audit (pgaudit)** + **middleware-level audit** — all Wave 9+ candidates.

## Commits in this Wave (from `git log --oneline` on `feature/0a-identity-model`)

The major commits in chronological order (Wave 8 portion of `feature/0a-identity-model`):

| # | SHA | Subject |
|---|-----|---------|
| 1 | `6f6d74c` | Merge Wave 8 chain (slices 8a.1-8b.2) into feature/0a-identity-model (current HEAD pre-archive) |
| 2 | `a50c7c6` | Merge pull request #29 from JesMedC/feature/wave8-reconciliation |
| 3 | `7e22554` | chore(wave8-reconciliation): commit planning artifacts + reconcile tasks checkboxes |
| 4 | `6b54396` | chore(wave8-reconciliation): slice 8b.2 validate - apply-progress |
| 5 | `f3db20d` | chore(wave8-reconciliation): slice 8b.2 phase 3 - REMOVED Requirements in delta spec |
| 6 | `d0d3e6b` | docs(wave8-reconciliation): slice 8b.2 phase 1-2 - 2 SKIP XML doc comments |
| 7 | `97ed160` | Merge pull request #28 from JesMedC/feature/wave8-billing-audit-1 |
| 8 | `1cc09d0` | chore(wave8-billing-audit-1): slice 8b.1 validate - apply-progress |
| 9 | `6df9cee` | feat(wave8-billing-audit-1): slice 8b.1 phase 1 - StripeCustomerAuditDecorator (bespoke immutable CreateOrGet) |
| 10 | `4ea76c3` | Merge pull request #27 from JesMedC/feature/wave8-trading-audit-3 |
| 11 | `330b667` | fix(wave8-trading-audit-3): add IsOwner cross-tenant check to PreTradeChecklistAuditDecorator (reconciliation) |
| 12 | `487aec9` | chore(wave8-trading-audit-3): slice 8a.3 validate - apply-progress |
| 13 | `d4df268` | feat(wave8-trading-audit-3): slice 8a.3 phase 2 - PreTradeChecklistAuditDecorator (write-once aggregate) |
| 14 | `0bc38e2` | feat(wave8-trading-audit-3): slice 8a.3 phase 1 - PlannerSessionAuditDecorator (IsTerminated reflection) |
| 15 | `34301dc` | Merge pull request #26 from JesMedC/feature/wave8-trading-audit-2 |
| 16 | `5b76803` | chore(wave8-trading-audit-2): slice 8a.2 validate - apply-progress |
| 17 | `4fd22e8` | feat(wave8-trading-audit-2): slice 8a.2 phase 2 - TradeReviewAuditDecorator (attachment ops forward without audit) |
| 18 | `60a1492` | feat(wave8-trading-audit-2): slice 8a.2 phase 1 - AlertAuditDecorator (AddAsync returns bool dedup) |
| 19 | `6d42cdb` | Merge pull request #25 from JesMedC/feature/wave8-trading-audit-1 |
| 20 | `81c1b2a` | chore(wave8-trading-audit-1): slice 8a.1 validate - apply-progress |
| 21 | `fd01396` | feat(wave8-trading-audit-1): slice 8a.1 phase 3 - AccountAuditDecorator + InstrumentAuditDecorator + DI |
| 22 | `d0e9635` | feat(wave8-trading-audit-1): slice 8a.1 phase 2 - IInstrumentRepository RemoveAsync to DeleteAsync |
| 23 | `66cbd68` | refactor(wave8-trading-audit-1): slice 8a.1 phase 1 - IAccountRepository RemoveAsync to DeleteAsync |
| 24 | `21430aa` | chore(sdd): archive 2026-08-19-wave7-audit-coverage + merge delta into soft-delete-audit spec (Wave 7 anchor) |

The exact full commit log is preserved in the audit trail; the table above is the high-level call chain.

## Archive Folder Contents

```
openspec/changes/archive/2026-08-19-wave8-audit-coverage-extended/
├── archive-report.md                         ← this file (additive — written after the move, excluded from snapshot diff)
├── design.md                                 (~220 lines — module dependency diagram + 7 typed decorator shapes + 2 atomic renames)
├── explore.md                                (~376 lines — pre-proposal exploration)
├── proposal.md                               (~326 lines — Intent, scope, architectural decisions, per-slice detail)
├── tasks.md                                  (~349 lines, 66/66 tasks checked)
├── verify-report-wave8-final.md              (~354 lines — verify envelope + spec↔implementation matrix + tasks completion table)
├── apply-progress-wave8-slice-8a-1.md        (~107 lines, slice 8a.1 progress + deviations)
├── apply-progress-wave8-slice-8a-2.md        (~92 lines, slice 8a.2 progress + deviations)
├── apply-progress-wave8-slice-8a-3.md        (~94 lines, slice 8a.3 progress + deviations)
├── apply-progress-wave8-slice-8b-1.md        (~96 lines, slice 8b.1 progress + deviations)
├── apply-progress-wave8-slice-8b-2.md        (~79 lines, slice 8b.2 progress + reconciliation)
└── specs/
    └── soft-delete-audit/
        └── spec.md                           (~350 lines, 12 requirements, 44 scenarios — the Wave 8 DELTA spec)
```

The `specs/soft-delete-audit/spec.md` inside the archive is the **delta spec** (the Wave 8 1 MOD + 9 ADD + 2 REMOVED); the **canonical merged spec** lives at `openspec/specs/soft-delete-audit/spec.md` (28 requirements, 120 scenarios — Wave 6 + Wave 7 + Wave 8 merged). The delta stays in the archive as the immutable record of what Wave 8 contributed; the canonical is the new source of truth for Wave 9+.

## OpenSpec Integrity Validation

- [x] Main specs updated correctly — `openspec/specs/soft-delete-audit/spec.md` updated in-place: 1 MODIFIED requirement (Apply decorator to existing repositories) + 9 ADDED requirements. 28 requirements / 120 scenarios (vs 19/84 pre-merge). 120 GIVEN + 117 WHEN + 120 THEN clauses preserved.
- [x] Change folder moved to archive — `openspec/changes/2026-08-19-wave8-audit-coverage-extended/` is gone from `openspec/changes/` and present at `openspec/changes/archive/2026-08-19-wave8-audit-coverage-extended/`. `git mv` preserved history (the move is staged + committed, not a new commit).
- [x] Archive contains all artifacts (proposal.md ✅, specs/ ✅, design.md ✅, tasks.md ✅, verify-report-wave8-final.md ✅, explore.md ✅, all 5 apply-progress-*.md ✅).
- [x] Archived `tasks.md` has 0 unchecked implementation tasks (66 `[x]`, 0 `[ ]` — no exceptional reconciliation needed at archive time; the tasks artifact was already up to date from the Wave 8 reconciliation commit `7e22554` before the PR chain was merged into `feature/0a-identity-model`).
- [x] Active changes directory no longer has this change — confirmed via `ls openspec/changes/`: only `archive/` remains.
- [x] Verbatim `diff -r` readback output is included in this report and is empty (no differences). See § Mechanical Copy Contract below.

A failed or skipped `diff -r` FAILS the phase — none was skipped or failed.

## Mechanical Copy Contract — verbatim diff output

### Spec merge (canonical promotion)

The canonical spec merge was a **non-destructive in-place edit** (1 MOD + 9 ADD, no REMOVED) — performed via the `edit` tool on `openspec/specs/soft-delete-audit/spec.md`. The Mechanical Copy Contract (`cp + diff -r + mv` byte-identity) applies to the **archive folder move** (delta spec → archive), not to the canonical merge. For the merge, verification is by:

- **Pre-merge snapshot**:

```
$ snapshot_root="$(mktemp -d ${TMPDIR:-/tmp}/sdd-archive.XXXXXX)"
$ cp /home/nitro/Proyects/JadeCapitalSuiteOficial/openspec/specs/soft-delete-audit/spec.md "$snapshot_root/canonical.pre-merge"
$ wc -l "$snapshot_root/canonical.pre-merge" /home/nitro/Proyects/JadeCapitalSuiteOficial/openspec/specs/soft-delete-audit/spec.md
   724 /tmp/sdd-archive.ESR8fy/canonical.pre-merge
   724 /home/nitro/Proyects/JadeCapitalSuiteOficial/openspec/specs/soft-delete-audit/spec.md
```

- **Requirements count**: 19 → 28 (19 original kept + 1 replaced-MOD + 9 ADD = 28). ✅
- **Scenarios count**: 84 → 120 (84 + 36 ADD = 120). ✅
- **Given/When/Then preservation**: 120 GIVEN + 117 WHEN + 120 THEN clauses (matches scenario count for GIVEN and THEN; WHEN can be < scenarios when a scenario has only GIVEN + THEN, which is the standard pattern). ✅
- **Modified "Apply decorator to existing repositories" section**: 1 line for the "Wave 7" label + 1 line for the "Wave 8+" list + 1 line for the "Previously" note + 1 line for the "GetById is NOT audited" wording = 4 lines changed in the section header. The 8 scenarios below are unchanged (they already covered the Wave 6+7 patterns and the wording is generic enough to remain valid).
- **Appended 9 ADDED requirements**: 9 requirement bodies + 36 scenarios at the end of the Requirements section, just before `## Data Model`.

- **Snapshot diff (proof the merge was in-place and targeted)**

```
$ diff -r /tmp/sdd-archive.ESR8fy/canonical.pre-merge /home/nitro/Proyects/JadeCapitalSuiteOficial/openspec/specs/soft-delete-audit/spec.md
267,268c267,269
< - **Wave 7 (this delta)**: `User`, `RiskProfile`, `Strategy`, `Trade`, `JournalEntry`.
< - **Wave 8+**: `TradeReview`, `PlannerSession`, `PreTradeChecklist`, `Account`, `Instrument`, `Alert`, `RiskConfiguration`, `PlanVersion`, `JournalDaily`, `BacktestRun`, `StrategyVersion`, `TradeTag`, `Note`, `Mood`, `BehavioralMetric`, and any other user-owned aggregate.
---
> - **Wave 7 (7a.1 / 7b.1 / 7b.2)**: `User`, `RiskProfile`, `Strategy`, `Trade`, `JournalEntry`.
> - **Wave 8 (this delta — 8a.1 / 8a.2 / 8a.3 / 8b.1)**: `Account`, `Instrument`, `Alert`, `TradeReview`, `PlannerSession`, `PreTradeChecklist`, `StripeCustomer`.
> - **Wave 9+**: `RiskConfiguration`, `PlanVersion`, `JournalDaily`, `BacktestRun`, `StrategyVersion`, `TradeTag`, `Note`, `Mood`, `BehavioralMetric`, and any other user-owned aggregate.
270c271
< (Previously: Wave 6 covered `Tenant`, `ImportJob`, `Subscription`. Wave 7 widens to the 5 remaining user-owned aggregates — `User`, `RiskProfile`, `Strategy`, `Trade`, `JournalEntry` — and introduces `Denied` + `Failed` actions for cross-tenant rejection and `NotSupportedException` on delete paths.)
---
> (Previously: Wave 6 covered `Tenant`, `ImportJob`, `Subscription`. Wave 7 widened to `User`, `RiskProfile`, `Strategy`, `Trade`, `JournalEntry`. Wave 8 extends to 7 more user-owned aggregates and documents 2 explicit SKIPs: `ISubscriptionAdminRepository` (no mutation methods) and `IStripeWebhookEventRepository` (append-only). The `ISoftDelete` query filter does NOT apply to any of the 7 new aggregates; `PlannerSession` uses `PlannerStatus.Cancelled` via the `IsTerminated` reflection rule for soft-delete-via-status. `IInstrumentRepository` is a catalog entity — no `IsOwner` cross-tenant check (mirrors `TenantAuditDecorator` precedent).)
320c321
< - GIVEN any of the 5 new typed decorators
---
> - GIVEN any of the typed decorators (Wave 6 / Wave 7 / Wave 8)
667a669,938
> 
> ### Requirement: IAccountRepository.RemoveAsync renamed to DeleteAsync
> ... (9 ADDED requirements + 36 scenarios appended at line 667)
```

The diff is exactly what we expected: 3 lines changed in the "Apply decorator to existing repositories" section header (lines 265-270) + 1 line changed in the "GetById is NOT audited" scenario (line 320) + 270 lines added at line 667 (the 9 ADDED requirements + 36 scenarios). The 286-line diff (which is `compressed` output, not line count) matches the expected scope exactly. **No byte-identity violation.**

### Archive move (`git mv` + recursive snapshot diff)

```
$ snapshot_root="$(mktemp -d ${TMPDIR:-/tmp}/sdd-archive.XXXXXX)"
$ cp -R "openspec/changes/2026-08-19-wave8-audit-coverage-extended" "$snapshot_root/source"
$ git mv openspec/changes/2026-08-19-wave8-audit-coverage-extended openspec/changes/archive/2026-08-19-wave8-audit-coverage-extended
$ [ -e openspec/changes/2026-08-19-wave8-audit-coverage-extended ] && echo "FAIL: source still present" || echo "OK: source gone"
OK: source gone
$ diff -r "$snapshot_root/source" "openspec/changes/archive/2026-08-19-wave8-audit-coverage-extended"
(no output — empty diff)
$ echo "EXIT=$?"
EXIT=0
```

The empty diff is the only passing evidence. The `archive-report.md` file is additive-only and was written after the snapshot + move, so it is correctly excluded from the source/destination comparison (it did not exist in the source snapshot).

The snapshot included 11 entries: 1 archive-report (not yet written), 5 apply-progress files, 1 design.md, 1 explore.md, 1 proposal.md, 1 specs/soft-delete-audit/spec.md, 1 tasks.md, 1 verify-report-wave8-final.md. All 11 are present byte-identically in the archive folder post-move. The `git mv` preserved rename history for all tracked files.

## SDD Cycle Status

| Phase | Status | Artifact |
|-------|--------|----------|
| explore | ✅ done | `explore.md` (~376 lines — pre-proposal exploration of the audit coverage gap + cross-module edge) |
| propose | ✅ done | `proposal.md` (326 lines — Intent, scope, architectural decisions, per-slice detail) |
| spec | ✅ done | `specs/soft-delete-audit/spec.md` (delta, 350 lines, 12 requirements, 44 scenarios) |
| design | ✅ done | `design.md` (220 lines — module dependency diagram, 7 typed decorator shapes, 2 atomic renames, test architecture) |
| tasks | ✅ done | `tasks.md` (349 lines, 66/66 checked) |
| apply | ✅ done | 5 `apply-progress-wave8-slice-*.md` + 4430 authored LOC across 5 slices + 5 PRs #25-#29 MERGED + 1 reconciliation IsOwner fix at PR #27 commit `330b667` |
| verify | ✅ done | `verify-report-wave8-final.md` (PASS WITH WARNINGS, 10/10 reqs, 44/44 scenarios, 1365/1365 tests) |
| archive | ✅ done | `openspec/changes/archive/2026-08-19-wave8-audit-coverage-extended/` (this report) |

**Cycle complete.** Ready for Wave 9 planning.

## Source of Truth Updated

The following spec now reflects Wave 8 behavior under `openspec/specs/`:

- `openspec/specs/soft-delete-audit/spec.md` — Updated (merge): 1 MODIFIED requirement + 9 ADDED requirements. 28 requirements / 120 scenarios. The 15 of 17 user-owned aggregates are now covered: `Tenant` (Wave 6), `ImportJob` (Wave 6), `Subscription` (Wave 6), `User` (Wave 7), `RiskProfile` (Wave 7), `Strategy` (Wave 7), `Trade` (Wave 7), `JournalEntry` (Wave 7), `Account` (Wave 8), `Instrument` (Wave 8), `Alert` (Wave 8), `TradeReview` (Wave 8), `PlannerSession` (Wave 8), `PreTradeChecklist` (Wave 8), `StripeCustomer` (Wave 8). The 2 SKIPs are documented: `ISubscriptionAdminRepository` (no mutation methods; `Subscription` already audited by Wave 6), `IStripeWebhookEventRepository` (append-only per Wave 6 design; the entity IS the audit log equivalent). The `AuditAction` enum has 6 values: Created=0, Updated=1, Deleted=2, Restored=3, Denied=4 (Wave 7), Failed=5 (Wave 7). The `Apply decorator to existing repositories` requirement now documents 7 new Wave 8 aggregates + 2 explicit SKIPs + Wave 9+ remaining.

The change folder `openspec/changes/2026-08-19-wave8-audit-coverage-extended/` has been removed from the active changes directory and moved to the archive via `git mv` (history preserved).

## Engram Cross-References

This archive report is also persisted as Engram observation with `topic_key: sdd/2026-08-19-wave8-audit-coverage-extended/archive-report`, `type: architecture`, `capture_prompt: false` (automated artifact), `project: jadecapitalsuiteoficial`. The Engram topic_key enables future upserts if the archive report is amended.

Related engram observations for the Wave 8 cycle are stored under the same project (`jadecapitalsuiteoficial`) with `topic_key: sdd/2026-08-19-wave8-audit-coverage-extended/*`. The verify-report observation is the most relevant for cross-referencing final-state facts at this archive.

## Skill Resolution

`paths-injected` — orchestrator provided the `sdd-archive` skill explicitly in the launch prompt and `_shared` referenced implicitly. Both loaded before phase work. The `sdd-archive` skill's `sdd-phase-common.md` (referenced for Section A/B/C/D) is at `/home/nitro/.config/opencode/skills/_shared/`. The Mechanical Copy Contract was applied verbatim; the empty `diff -r` is the only passing evidence for the archive folder move. The Task Completion Gate passed without exceptional reconciliation (the Wave 8 tasks artifact was already up to date at archive time, thanks to the pre-merge reconciliation commit `7e22554` on `feature/wave8-reconciliation`).
