# Archive Report — Jade Trader OS Core Portals (Wave 0)

> **Change**: `jade-trader-os-core-portals`
> **Archived**: 2026-08-15
> **Archive path**: `openspec/changes/archive/2026-08-15-jade-trader-os-core-portals/`
> **Mode**: `openspec` (file-based)
> **Strict TDD**: active per `openspec/config.yaml`

---

## Verdict

**SDD cycle complete.** Wave 0 (slices 0a–0g) closed with all 57 tasks implemented,
verified, and the change folder moved to archive. Delta specs are now the source of
truth under `openspec/specs/`.

---

## Final State (per Final-State Authority hierarchy)

### 1. Native review authority
- `reviewGate` is **structurally absent** in native SDD status (no `reviewGate` key).
- Per the Native Review Receipt Gate: archive proceeds under ordinary repository
  policy. Receipt-driven development was not started for this candidate.
- `gentle-ai sdd-attempt status` (last query before this report) reported
  `complete: true`, `next_action: complete`, `decision_required: false`, and
  three runtime attempts on the ledger (wave-0-verify, wave-0-verify-remediation,
  wave-0-verify-archive), all with `outcome: passed`.

### 2. Persisted tasks artifact
- `openspec/changes/archive/2026-08-15-jade-trader-os-core-portals/tasks.md`
  covers only **Wave 0 slices 0a–0g** (no wave1 references).
- 57/57 tasks checked. **Task Completion Gate passes** — no stale unchecked boxes
  for completed work.

### 3. Explicit final-state facts (from orchestrator launch prompt)
- Verify report envelope (`gentle-ai.verify-result/v1`) prepended and validated
  during Wave 0 close remediation. Schema fields confirmed: `blockers: 0`,
  `critical_findings: 0`, `requirements: 4/4`, `scenarios: 21/21`,
  `test_exit_code: 0`, `build_exit_code: 0`.
- 454 tests passing (400 backend unit + 19 integration + 35 frontend Jest).
- 10 frontend spec files exist and pass.
- `dotnet build JadeCapital.slnx` exits 0.

### 4. Verify report and apply progress (intermediate snapshots)
- `verify-report.md` verdict: **PASS WITH CAVEATS** (caveat: `size:exception`
  pattern on slices 0a/0c/0e/0f/0g acknowledged as historical per ADR-0004).
- `apply-progress-wave1-partial.md` is a **separate, follow-up effort** beyond
  this change's task scope. It documents a slice-0e history-persistence bug
  discovered after Wave 0 closed. It is preserved in the archive for audit
  but is NOT a stale snapshot of Wave 0 close — it is a record of a later
  effort that does not change Wave 0's completion.

---

## Specs Synced

| Domain | Action | Path |
|--------|--------|------|
| `identity-password-recovery` | Created (delta was full spec; main was empty) | `openspec/specs/identity-password-recovery/spec.md` |
| `subscription-administration` | Created (delta was full spec; main was empty) | `openspec/specs/subscription-administration/spec.md` |

Mechanical copy verified with `diff -r` (empty diff = only passing evidence).
The pre-archive main `openspec/specs/` did not exist; this is the first archive
for this repository, so both delta specs were promoted as full specs.

---

## Archive Contents

```
openspec/changes/archive/2026-08-15-jade-trader-os-core-portals/
├── archive-report.md              ← this file (additive; not in pre-move snapshot)
├── apply-progress.md              (Wave 0, 726 lines)
├── apply-progress-0g.md           (Slice 0g detail, 127 lines)
├── apply-progress-slice-0e-1.md   (Slice 0e.1 remediation, 100 lines)
├── apply-progress-wave1-partial.md (follow-up, NOT a Wave 0 task — see note)
├── design.md                      (75 lines)
├── exploration.md                 (532 lines)
├── proposal.md                    (56 lines)
├── tasks.md                       (112 lines, 57/57 checked)
├── verify-report.md               (139 lines, YAML envelope prepended)
└── specs/
    ├── identity-password-recovery/spec.md
    └── subscription-administration/spec.md
```

All 11 source files were moved to the archive via `git mv` (they were tracked).
Mechanical move verified with `diff -r` (empty diff = only passing evidence).

---

## Mechanical Copy Contract — Readback Evidence

Per the Mandatory Mechanical Copy Contract, every copy and move was performed
with native shell commands and verified by a verbatim `diff -r` readback.

**Step 1 — Spec sync (2 domains)**:
- `diff -r openspec/changes/jade-trader-os-core-portals/specs/identity-password-recovery/spec.md openspec/specs/identity-password-recovery/.spec.md.06tdcX` → empty
- `diff -r openspec/changes/jade-trader-os-core-portals/specs/subscription-administration/spec.md openspec/specs/subscription-administration/.spec.md.mAq5IZ` → empty

**Step 2 — Move to archive**:
- `diff -r /tmp/sdd-archive.H4dwfT/source openspec/changes/archive/2026-08-15-jade-trader-os-core-portals` → empty

All empty diffs = the only passing evidence. No `Read → Write` artifact
reproduction through the model path.

---

## SDD Cycle Status

| Phase | Status | Artifact |
|-------|--------|----------|
| explore | ✅ done | `exploration.md` |
| propose | ✅ done | `proposal.md` |
| spec | ✅ done | `specs/{identity-password-recovery,subscription-administration}/spec.md` |
| design | ✅ done | `design.md` |
| tasks | ✅ done | `tasks.md` (57/57) |
| apply | ✅ done | `apply-progress*.md` (4 files) |
| verify | ✅ done | `verify-report.md` (PASS WITH CAVEATS) |
| archive | ✅ done | `openspec/changes/archive/2026-08-15-jade-trader-os-core-portals/` |

**Cycle complete. Ready for the next change.**

---

## Open Follow-ups (NOT part of this change)

These are recorded for the next change's intake. They are NOT Wave 0 tasks and
were never expected to be resolved by this archive.

**Resolved during Wave 0 close (commit `3055dbb`):** items #1, #2, #3 below were
open in the `apply-progress-wave1-partial.md` snapshot but were already fixed in
code by the Wave 0 close remediation. The archive report snapshot itself was
written before that round of fixes landed. Verified 2026-08-15:

1. ~~Slice 0e history persistence bug~~ **RESOLVED**: `BillingAdminUnitOfWork.AddHistoryEntry`
   in `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Persistence/SubscriptionAdminRepository.cs:63-64`
   calls `_db.SubscriptionHistory.Add(entry)` explicitly, bypassing the navigation
   collection-tracking bug. The three handlers (`ChangeTier`, `Cancel`, `ExtendTrial`)
   in `src/2.Modules/Billing/JadeCapital.Billing.Application/Features/Subscriptions/`
   call this UoW method after a successful mutation. `dotnet test tests/UnitTests/JadeCapital.Billing.UnitTests/` → 19/19 PASS.
2. ~~Owner projection returns null~~ **RESOLVED**: `IOwnerProjectionLookup` is
   registered as Scoped in `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/DependencyInjection/BillingModuleRegistration.cs:40`;
   `IdentityOwnerProjectionLookup` reads via `SqlQueryRaw` cross-schema from `identity.users`.
   A `null` return is the by-design fallback for orphan subscriptions (no row in `identity.users`),
   not a DI miss.
3. ~~`Subscription.Version` concurrency token~~ **RESOLVED**: explicitly marked in
   `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Persistence/Configurations/SubscriptionConfiguration.cs:43`
   with `.IsConcurrencyToken()`. EF Core 9 default detection is not relied on.

**Still open** (deferred — not blockers for Wave 0 archive closure):

4. **Stripe integration** (DEFERRED): `Stripe.net 47.0.0` declared but unused.
5. **Public `/api/billing/plans` endpoint** (DEFERRED): to replace hardcoded `PLANS`
   in `frontend/.../pricing/pricing-page.ts` and `landing-page.ts`. No longer gated
   on slice-0e fixes (#1 above) since those are now closed.

---

## Source of Truth Updated

The following specs now reflect Wave 0 behavior:

- `openspec/specs/identity-password-recovery/spec.md`
- `openspec/specs/subscription-administration/spec.md`

The change folder `openspec/changes/jade-trader-os-core-portals/` has been
removed from the active changes directory and moved to the archive.
