# Wave 4 — Ready to Archive

**Change**: `2026-08-19-trader-scanner-marketdata-realtime`
**Status**: ✅ All 5 slices shipped. All tasks marked `[x]`. All artifacts in place.
**Archive trigger**: post-PR merge of `feature/wave4-e2e` → `feature/0a-identity-model`.

---

## Manifest

### Apply-progress files (5)

| Slice | File | Lines |
|---|---|---:|
| 4a | `apply-progress-wave4-partial.md` | 47 |
| 4b | `apply-progress-wave4-slice-4b.md` | 140 |
| 4c | `apply-progress-wave4-slice-4c.md` | 169 |
| 4d | `apply-progress-wave4-slice-4d.md` | 222 |
| 4e | `apply-progress-wave4-slice-4e.md` | (this file's sibling) |

### Final commit hashes per slice

| PR | Slice | Branch | Commit |
|---|---|---|---|
| #1 | 4a Scanner | `feature/wave4-scanner` | `a214fbc` |
| #2 | 4b MarketData | `feature/wave4-marketdata` | `c1d783b` |
| #3 | 4c Realtime | `feature/wave4-realtime` | `4e5535e` |
| #4 | 4d Attachments | `feature/wave4-attachments` | `6ab0cee` |
| #5 | 4e E2E + archive | `feature/wave4-e2e` | (this PR's commit) |

### PR URLs

(To be filled by orchestrator after PR open.)

### Cumulative LOC added (Wave 4 net)

| Slice | Net LOC | Tests added |
|---|---:|---:|
| 4a | 1,471 | +17 |
| 4b | 1,355 | +31 |
| 4c | 1,994 | +75 |
| 4d | 2,753 | +50 |
| 4e | 318 | +10 |
| **Total** | **7,891** | **+183** |

### Test counts (post-Wave 4)

| Suite | Count | Status |
|---|---:|---|
| `JadeCapital.Identity.UnitTests` | 163 | ✅ green |
| `JadeCapital.Trading.UnitTests` | 522 | ✅ green |
| `JadeCapital.Shared.Kernel.UnitTests` | 100 | ✅ green |
| `JadeCapital.Billing.UnitTests` | 22 | ✅ green |
| `JadeCapital.Api.IntegrationTests` (pre-Wave 4) | 14 | ⚠️ blocked by pre-existing migration bug |
| `JadeCapital.Api.IntegrationTests` (Wave 4 NEW) | +5 (compiles clean) | ⚠️ blocked by pre-existing migration bug |
| Frontend (jest) | 146 + 4 Wave 4 = 150 | ✅ green, 36 suites |

**BE total pass** (excl. IntegrationTests): **807/807** ✅
**FE total pass**: **150/150** ✅
**Integration total**: compiles clean, execution blocked by pre-existing migration order bug in `JadeApiFactory.ApplyMigrationAsync` (see 4e.D1).

### Key files to inspect on archive

```
openspec/changes/2026-08-19-trader-scanner-marketdata-realtime/
├── proposal.md
├── design.md
├── tasks.md                                       (all [x])
├── apply-progress-wave4-partial.md                 (4a)
├── apply-progress-wave4-slice-4b.md                (4b)
├── apply-progress-wave4-slice-4c.md                (4c)
├── apply-progress-wave4-slice-4d.md                (4d)
├── apply-progress-wave4-slice-4e.md                (4e — this slice)
├── READY-TO-ARCHIVE.md                            (this file)
└── specs/                                          (delta specs from each slice)
```

### Archive command (orchestrator-side)

After PR #5 merges into `feature/0a-identity-model`, the orchestrator should:

```bash
cd /home/nitro/Proyects/JadeCapitalSuiteOficial
mv openspec/changes/2026-08-19-trader-scanner-marketdata-realtime \
   openspec/changes/archive/2026-08-19-trader-scanner-marketdata-realtime
```

After archive, run `/sdd-archive` skill to sync delta specs from `archive/2026-08-19-trader-scanner-marketdata-realtime/specs/` to the main `openspec/specs/` tree.

### Debt carried into Wave 5+

See `docs/PROJECT-STATUS.md` §7 for the full list. Top 5:

1. **Integration test infrastructure migration order bug** (4e.D1) — affects ALL Testcontainers tests since Wave 0.
2. **`ActiveHours` modeled as `string?`** (4a.D1) — promote to `ActiveHoursWindow` VO.
3. **`CurrentPriceNearStopRule` uses `EntryPrice` as stop proxy** (4c.D2) — needs `Trade.StopLossPrice` column.
4. **`IncrementQuotaUsageBestEffort` is NO-OP** (4d.D4) — needs Identity-side mutator.
5. **AI signal generation + multi-timeframe scanner filters** — Wave 5 features.

---

**DO NOT actually move the directory to `archive/`** — that's the orchestrator's call after PR #5 merges. This file is the manifest.