# Proposal: Wave 8 — Audit Coverage Extension (7 More Decorators)

**Change**: `2026-08-19-wave8-audit-coverage-extended`
**Branch**: `feature/0a-identity-model` @ `21430aa` (Wave 7 just archived)
**Stack**: ASP.NET Core 10 / .NET SDK 10.0.400 via `mise exec -- dotnet …`
**Strategy**: `feature-branch-chain` (carries the Wave 7 chain)

## Intent and Problem

Wave 7 closed the audit decorator rollout to **8 user-owned aggregates** (Tenant, ImportJob, Subscription, User, RiskProfile, Strategy, Trade, JournalEntry). Wave 7's verify-report flagged **9 remaining candidate repositories** for Wave 8 (suggestion #1 in `verify-report-wave7-final.md`). Of those 9:

- **7 need audit decorators** (4 bespoke + 1 write-once + 1 immutable + 2 standard-with-rename).
- **2 are documented SKIPs** with explicit rationale (no mutation surface to audit; would emit duplicate / noise rows).

The compliance gap is real and bounded: every mutation on `TradeReview` / `PlannerSession` / `PreTradeChecklist` / `Account` / `Instrument` / `Alert` / `StripeCustomer` is currently invisible to `audit.events`. Two atomic renames (`RemoveAsync` → `DeleteAsync` for Account + Instrument) are required to bring the interfaces into the canonical Wave 7 shape that `DecoratedRepository<T>` expects.

**No new infrastructure**: no enum extension, no migration, no `Shared.Kernel` change. Wave 7's `AuditAction.Denied = 4` + `AuditAction.Failed = 5` + migration 0029 already cover all 7 new decorators' audit-action needs. `DecoratedRepository<T>.IsTerminated` reflection (Wave 6) covers `PlannerSession` (via `PlannerStatus.Cancelled`).

## Goals

- **Coverage**: 7 new typed audit decorators spanning Trading (6) + Billing (1). Every user-owned aggregate in the 9-repo list now emits `AuditEvent` writes through the Wave 7 pattern.
- **Surface surgery**: `IAccountRepository.RemoveAsync(Account)` → `DeleteAsync(Account)` + `IInstrumentRepository.RemoveAsync(Instrument)` → `DeleteAsync(Instrument)` (both breaking renames, 1 handler each, atomic in slice 8a.1).
- **Documented SKIPs**: explicit rationale baked into spec REMOVED Requirements + tasks + this proposal's Out of Scope for `ISubscriptionAdminRepository` (no mutations) and `IStripeWebhookEventRepository` (append-only; entity IS the audit log).
- **Zero regression**: all 1313 Wave 7 tests still pass after the wave.

## Scope Boundaries

### In Scope

| Sub-scope | Boundary | Deliverable |
|---|---|---|
| **A. Trading coverage (6 decorators)** | Bespoke + standard-with-rename | `AccountAuditDecorator` + `InstrumentAuditDecorator` (8a.1, standard); `AlertAuditDecorator` + `TradeReviewAuditDecorator` (8a.2, bespoke); `PlannerSessionAuditDecorator` + `PreTradeChecklistAuditDecorator` (8a.3, bespoke) |
| **B. Billing coverage (1 decorator)** | Bespoke immutable | `StripeCustomerAuditDecorator` (8b.1) |
| **C. Surface surgery** | 2 atomic renames | `IAccountRepository.RemoveAsync` → `DeleteAsync` + `IInstrumentRepository.RemoveAsync` → `DeleteAsync`; 1 handler call site each |
| **D. SKIP reconciliation** | Doc-only | Explicit rationale in proposal Out of Scope + spec REMOVED Requirements for `ISubscriptionAdminRepository` + `IStripeWebhookEventRepository` (8b.2) |
| **Tests** | SQLite-in-memory | 35 new tests in `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/` (14 in 8a.1: 10 integration + 4 contract; 10 in 8a.2; 8 in 8a.3; 3 in 8b.1; 0 in 8b.2 — see slice detail) |

### Out of Scope (deferred to Wave 9+)

- **Audit for `RiskConfiguration`, `PlanVersion`, `JournalDaily`, `BacktestRun`, `StrategyVersion`, `TradeTag`, `Note`, `Mood`, `BehavioralMetric`, etc.** — Wave 9 widens coverage to the long-tail user-owned aggregates.
- **Reporting / Query API for `audit.events`** — admin-only `GET /api/audit/events` (with filters by entity_type, action, user_id, tenant_id, date range, free-text on `changes`) is Wave 9.
- **User-facing read API (`GET /api/audit/me`)** — "my mutation history" tab in mobile + admin is Wave 9.
- **Audit UI / Export (CSV/JSON)** — compliance officer tooling is Wave 9.
- **Audit log retention / auto-purge** — 90-day retention hosted service (Wave 6 explicit deferral) is Wave 9.
- **Soft-delete cascade propagation** beyond `ImportJob` (Wave 6 precedent) for the 7 newly-decorated aggregates — Wave 9.
- **`AuditAction.Restored` end-to-end support** — the enum value is reserved; restore command + admin tooling is Wave 9.
- **Bulk audit events for `AddRangeAsync`** — out of scope (Wave 6/7 precedent).
- **Migration to a different audit sink** (Kafka / S3 / external SIEM) — post-1.0.

### Out of Scope (Wave 8 SKIPs — documented, not deferred)

- **`ISubscriptionAdminRepository`**: NO mutation methods (`Task (Add|Update|Delete)Async` — none on the interface). All subscription mutations flow through `ISubscriptionAdminUnitOfWork.SaveChangesAsync` + the `Subscription` aggregate's own `ChangeTier`/`Cancel`/`ExtendTrial`/`SyncFromStripe` — already audited by Wave 6's `SubscriptionAuditDecorator` (which wraps `ISubscriptionRepository`, not the admin surface). Adding an audit decorator here would be a no-op on the audit-write path; the only thing left to forward is reads (which Wave 7 precedent says are never audited). **Skip with documented rationale. No code change.**
- **`IStripeWebhookEventRepository`**: append-only per Wave 6 `ISoftDelete` docstring ("`StripeWebhookEvent` are append-only and never deleted"). The entity docstring says: "append-only after Record; EventId/EventType/PayloadJson/ReceivedAt are NOT publicly settable." The `UpdateAsync` path (`MarkProcessed`/`MarkFailed`) is system-internal dispatch bookkeeping — no user intent, no audit value. The `billing.stripe_webhook_events` table IS the webhook audit trail (compliance officers query it directly). Adding `AuditEvent` rows on top would be doubly-recorded noise. **Skip with documented rationale. No code change.**

## Capabilities (modified)

- **`soft-delete-audit`** (Wave 6/7, `openspec/specs/soft-delete-audit/spec.md`) — MODIFIED. The Wave 7 spec covers `ISoftDelete` + `IAuditLogger` + `DecoratedRepository<T>` + the 8 typed decorators (Tenant, ImportJob, Subscription, User, RiskProfile, Strategy, Trade, JournalEntry). Wave 8 extends the same requirement:

  > **Requirement: Decorator coverage for the remaining 7 aggregates**
  > Every `IXxxRepository` that mutates state on a user-owned aggregate MUST have a typed audit decorator registered in its module's `*ModuleRegistration`. The decorator MUST forward `AddAsync` / `UpdateAsync` / `DeleteAsync` (or the aggregate-specific mutation method like `MarkSupersededAsync`) to a `DecoratedRepository<T>` instance and emit an `AuditEvent` with `EntityType = typeof(T).Name`. Covered aggregates after Wave 8: `Tenant`, `ImportJob`, `Subscription`, `User`, `Strategy`, `Trade`, `RiskProfile`, `JournalEntry`, `TradeReview`, `PlannerSession`, `PreTradeChecklist`, `Account`, `Instrument`, `Alert`, `StripeCustomer`. New aggregates added in Wave 9+ extend this list.

  This is a **capability expansion**, NOT a new capability. The sdd-spec phase writes a delta spec for `soft-delete-audit` (~37 sub-scenarios across the 7 new decorators + the 2 SKIP reconciliations, per the existing Wave 7 shape).

  The delta spec also adds a **REMOVED Requirements** section per OpenSpec convention:
  - `### REMOVED: ISubscriptionAdminRepository` — Reason: interface has no mutation methods; `Subscription` aggregate already audited by Wave 6.
  - `### REMOVED: IStripeWebhookEventRepository` — Reason: append-only per Wave 6 `ISoftDelete` docstring; entity IS the audit log equivalent.

## Capabilities (new)

- None. The 2 renames are versioned widens of existing contracts. The 2 SKIPs are reconciliation docs only.

## Architectural Decisions

- **Bespoke vs generic per repo** (per Explore analysis):

  | Repo | Pattern | Why |
  |---|---|---|
  | TradeReview | Bespoke | Cross-user scope on every read; secondary attachment ops (`AddAttachmentAsync` / `UpdateAttachmentAsync` / `RemoveAttachmentAsync`); cannot extend `IRepository<T>` without security regression |
  | PlannerSession | Bespoke | Cross-user scope; bespoke read methods (`ListByUserAndWeekAsync`, `ExistsForDateAsync`, `GetWeekComparisonAsync`); needs `IsTerminated` reflection on `PlannerStatus.Cancelled` |
  | PreTradeChecklist | Bespoke write-once | Only `AddAsync` + `ListByUserIdAsync`; no Update/Delete to wrap (entity docstring: "UNA fila por trade — enforced por UNIQUE INDEX"); generic shape adds noise |
  | Account | Generic (`DecoratedRepository<Account>`) | After `RemoveAsync → DeleteAsync` rename + `IRepository<Account>` extension, fits the canonical Wave 7 shape. Includes `IsOwner` cross-tenant check on `account.UserId`. |
  | Instrument | Generic (`DecoratedRepository<Instrument>`) | Same as Account; **NO `IsOwner` cross-tenant check** — entity is catalog data ("NO es Aggregate Root: es una Entity compartida por todos los usuarios"). Mirrors `TenantAuditDecorator` precedent (Tenant IS the tenant boundary; Instrument is catalog data). |
  | Alert | Bespoke | Cross-user scope; dedup-bounded `AddAsync` returns `bool` (`true` = inserted, `false` = deduped by `ux_alerts_user_rule_day` UNIQUE INDEX); bespoke read methods |
  | StripeCustomer | Bespoke immutable | Only `AddAsync` + 2 reads; aggregate docstring: "Aggregate is immutable after Create — no mutators" |

- **`IAlertRepository.AddAsync` returns `bool` audit semantics**: when `true` (inserted), emit `Created` audit. When `false` (deduped by `ux_alerts_user_rule_day`), emit **NO** audit row (the row was not created; the existing row's audit history is preserved). Per orchestrator preflight decision.

- **`PlannerSession.IsTerminated` reflection rule**: `PlannerStatus.Cancelled` matches the Wave 6 rule (`Status ∈ {Cancelled, Terminated, Expired}`). The decorator upgrades `Updated → Deleted` when the new `Status == PlannerStatus.Cancelled`. The other 6 audited entities have no enum Status field that matches — they emit straight `AuditAction.Updated` on mutations.

- **`TradeAttachment` is NOT audited separately** — child entity of `TradeReview`. Forward `AddAttachmentAsync` / `UpdateAttachmentAsync` / `RemoveAttachmentAsync` without audit. The review's Update events + the handler-side MinIO cleanup log already capture attachment lifecycle. Per orchestrator preflight decision.

- **`Instrument` catalog-entity precedent**: NO `IsOwner` check. All users see the same instrument catalog; admin mutations on the catalog are legitimate. Mirrors `TenantAuditDecorator` (Tenant IS the tenant boundary, not subject to it). Per orchestrator preflight decision.

- **`RemoveAsync → DeleteAsync` rename (Account + Instrument, breaking)**: 1 handler call site each — `DeleteAccountHandler.cs:47` + `DeleteInstrumentHandler.cs:45`. Atomic in-slice rename per Wave 7 7b.1 Trade pattern. No tests expected to break (handler tests don't assert the method name). The new `DeleteAsync(Account, ct)` / `DeleteAsync(Instrument, ct)` signatures match `IRepository<T>.DeleteAsync(T, ct)`.

- **8a.0 collapses to verified-no-op**: Wave 7's 7a.0 already added explicit `<PackageReference Include="Scrutor" Version="4.2.2" />` to Trading + Billing csprojs (lines 25-29 + 19-21 respectively). Identity.Infrastructure was never modified because Identity has no `services.Decorate` calls. Slice 8a.0 is omitted from the PR chain; the 4 sanity checks live in slice 8a.1's Phase 0 (`git diff --stat 21430aa..HEAD -- src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/JadeCapital.Identity.Infrastructure.csproj` → zero expected).

- **`Shared.Infrastructure` ownership unchanged**: `DecoratedRepository<T>` lives at `src/3.Shared/JadeCapital.Shared.Infrastructure/Persistence/DecoratedRepository.cs` (Wave 7 7a.0). The 7 new decorators import it from their own module's `Audit/` folder (Trading → Trading, Billing → Billing). No `Identity.Infrastructure` → `Trading.Infrastructure` circular dep risk.

- **Cross-tenant `IsOwner` check on user-owned decorators** (same as Wave 7 7a.1 / 7b.1 / 7b.2): TradeReview / PlannerSession / Alert / PreTradeChecklist / Account / StripeCustomer check `entity.UserId != tenant.CurrentUserId` BEFORE delegating to the inner. On mismatch: log a `Denied` audit row + throw `UnauthorizedAccessException`. **Instrument is the exception** (catalog entity).

- **Test fixture per aggregate**: TradeReview + PlannerSession + Alert use a focused helper `DbContext` (mirrors Wave 7's `TestTradingDbContext` pattern — `TradingDbContext` has Npgsql-specific array converters that fail on SQLite). PreTradeChecklist uses the existing SQLite-friendly helper. Account + Instrument use the same TradingDbContext fixture as Wave 7. StripeCustomer uses a focused BillingDbContext fixture. **No Testcontainers Postgres** (sandbox constraint — carry-forward from Wave 5/6/7 per verify-report WARNING #1).

- **`size:exception` precedent**: Wave 5/6/7 all needed `size:exception` per slice. Wave 8 follows the model — expect `size:exception` for 8a.1 + 8a.2 + 8a.3 (the 3 coverage slices); 8b.1 is borderline; 8b.2 is doc-only.

## Forecasting Rule Applied

Per Wave 7 verify-report SUGGESTION #2:

```
forecast = integration_scenarios + 2 * contract_scenarios_per_interface_surgery
```

**Interface surgery** = `RemoveAsync` → `DeleteAsync` rename, additive overload (like `IJournalEntryRepository.DeleteAsync(JournalEntry, ct)` from Wave 7), or any other interface-shape change that requires handler/test call-site updates.

| Slice | Decorators | Integration | Contract | Surgery | Forecast |
|---|---|---:|---:|---:|---:|
| **8a.0** | (refactor — verified-no-op) | 0 | 0 | 0 | **0** |
| **8a.1** | Account + Instrument (2 renames) | 10 | 4 | 2 | **14** |
| **8a.2** | Alert + TradeReview (bespoke) | 10 | 0 | 0 | **10** |
| **8a.3** | PlannerSession + PreTradeChecklist (bespoke) | 8 | 0 | 0 | **8** |
| **8b.1** | StripeCustomer (bespoke immutable) | 3 | 0 | 0 | **3** |
| **8b.2** | Reconciliation — SKIP SubscriptionAdmin + StripeWebhookEvent | 0 | 2 | 0 | **2** |
| **Total** | 7 audited decorators | **31** | **6** | **2** | **37 scenarios** |

PreTradeChecklist + StripeCustomer each audit only one mutation method (`AddAsync`), reducing their scenarios to ~3 vs. the standard ~5. No enum extension, no migration (no migration scenarios). Total ~37 spec scenarios vs. the orchestrator's ~44 estimate — delta explained by the write-once / immutable repos having smaller audit surfaces.

## Chained Delivery, Validation, and Rollback

| Slice | Boundary | LOC | Paths | Tests | size:exception | Validate | Rollback |
|---|---|---:|---:|---:|---|---|---|
| **8a.1** | Trading: Account + Instrument (standard + 2 renames) | ~600 | 12 | 14 | likely | `dotnet test --filter "FullyQualifiedName~AccountAudit\|InstrumentAudit\|AccountRepositoryIntegration\|InstrumentRepositoryIntegration\|IAccountRepositoryContractTests\|IInstrumentRepositoryContractTests"` + full cumulative suite | `git revert` the slice. `RemoveAsync` returns as the public surface on both interfaces. Both handlers revert. `audit.events` has no rows for Account / Instrument. |
| **8a.2** | Trading: Alert + TradeReview (bespoke) | ~600 | 10 | 10 | likely | `dotnet test --filter "FullyQualifiedName~AlertAudit\|TradeReviewAudit\|AlertRepositoryIntegration\|TradeReviewRepositoryIntegration"` + full cumulative suite | `git revert` the slice. DI registration removed. `audit.events` has no rows for Alert / TradeReview. |
| **8a.3** | Trading: PlannerSession + PreTradeChecklist (bespoke) | ~700 | 12 | 8 | likely | `dotnet test --filter "FullyQualifiedName~PlannerSessionAudit\|PreTradeChecklistAudit\|PlannerSessionRepositoryIntegration\|PreTradeChecklistRepositoryIntegration"` + full cumulative suite | `git revert` the slice. DI registration removed. `audit.events` has no rows for PlannerSession / PreTradeChecklist. |
| **8b.1** | Billing: StripeCustomer (bespoke immutable) | ~700 | 12 | 3 | unlikely | `dotnet test --filter "FullyQualifiedName~StripeCustomerAudit\|StripeCustomerRepositoryIntegration"` + full cumulative suite | `git revert` the slice. DI registration removed. `audit.events` has no rows for StripeCustomer. |
| **8b.2** | Reconciliation — SKIP SubscriptionAdmin + StripeWebhookEvent | ~50 | 2 | 0 | no | `git grep` 4 verification commands (see Phase 1 of slice 8b.2) | `git revert` the slice. Doc-only changes revert. Zero behavior change. |
| **Total** | 5 slices chained | **~2,650** | **48** | **~35 tests / ~37 spec scenarios** | 3 likely + 1 unlikely | — | — |

**Chain strategy**: `feature-branch-chain`. PR base = previous PR branch. Each PR merges into the previous PR's branch (or `feature/0a-identity-model` for the first slice). PR #1 targets `feature/0a-identity-model` (the Wave 7 archive commit `21430aa`).

| # | Branch | Base | Title |
|---|---|---|---|
| **#25** | `feature/wave8-trading-audit-1` (8a.1) | `feature/0a-identity-model` | Slice 8a.1 — `AccountAuditDecorator` + `InstrumentAuditDecorator` (incl. 2× `RemoveAsync` → `DeleteAsync` renames) |
| **#26** | `feature/wave8-trading-audit-2` (8a.2) | `feature/wave8-trading-audit-1` | Slice 8a.2 — `AlertAuditDecorator` + `TradeReviewAuditDecorator` |
| **#27** | `feature/wave8-trading-audit-3` (8a.3) | `feature/wave8-trading-audit-2` | Slice 8a.3 — `PlannerSessionAuditDecorator` + `PreTradeChecklistAuditDecorator` |
| **#28** | `feature/wave8-billing-audit` (8b.1) | `feature/wave8-trading-audit-3` | Slice 8b.1 — `StripeCustomerAuditDecorator` |
| **#29** | `feature/wave8-skip-reconciliation` (8b.2) | `feature/wave8-billing-audit` | Slice 8b.2 — Reconciliation doc: SKIP SubscriptionAdmin + StripeWebhookEvent |

Chain integrity: 5 PRs total. Each PR targets the previous PR's branch. Order matches slice order. No PR targets `main` directly.

### Per-slice detail

#### Slice 8a.1 — Trading: Account + Instrument (~600 LOC, ~12 paths, ~12 tests)

- **Phase 0** (Scrutor verification, ~0 LOC): confirm `Trading.Infrastructure.csproj` lines 25-29 + `Billing.Infrastructure.csproj` lines 19-21 still carry explicit `<PackageReference Include="Scrutor" Version="4.2.2" />`. Confirm Identity csproj unchanged via `git diff --stat 21430aa..HEAD -- src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/JadeCapital.Identity.Infrastructure.csproj`. If zero changes, **8a.0 is omitted from the PR chain** (verified-no-op).
- **Phase 1** (RED — contract tests, 4 tests): `IAccountRepositoryContractTests.RemoveAsync_IsNotOnInterface_AfterRename` + `DeleteAsync_Exists_OnInterface` (2). `IInstrumentRepositoryContractTests.RemoveAsync_IsNotOnInterface_AfterRename` + `DeleteAsync_Exists_OnInterface` (2).
- **Phase 2** (Surface surgery, atomic rename): `IAccountRepository.RemoveAsync(Account, ct)` → `DeleteAsync(Account, ct)`. Update `AccountRepository` impl in `Repositories.cs:147` + `DeleteAccountHandler.cs:47` call site. Same rename for `IInstrumentRepository.RemoveAsync` → `DeleteAsync`; update `InstrumentRepository` impl in `Repositories.cs:194` + `DeleteInstrumentHandler.cs:45` call site.
- **Phase 3** (RED — integration tests, 10 tests): `AccountRepositoryIntegrationTests` (5 — Created, Updated with diff + `IsOwner`, DeleteAsync writes Deleted, FindByIdAsync not audited, cross-tenant Denied). `InstrumentRepositoryIntegrationTests` (5 — same shape, no `IsOwner`).
- **Phase 4** (GREEN): `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AccountAuditDecorator.cs` (~100 LOC, generic `DecoratedRepository<Account>` shape + `IRepository<Account>` extension). `InstrumentAuditDecorator.cs` (~100 LOC, generic `DecoratedRepository<Instrument>` shape, **no IsOwner**).
- **Phase 5** (DI wiring): `services.Decorate<IAccountRepository, AccountAuditDecorator>()` + `services.Decorate<IInstrumentRepository, InstrumentAuditDecorator>()` in `TradingModuleRegistration.cs`.
- **Phase 6** Validate: 14 new tests pass (10 integration + 4 contract). `git grep "RemoveAsync" src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/ src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Persistence/Repositories.cs` → no results.
- **Dependencies**: none (first slice in chain).
- **Rollback**: `git revert` the slice. `RemoveAsync` returns on both interfaces. Both handlers revert. `audit.events` has no rows for Account / Instrument.

#### Slice 8a.2 — Trading: Alert + TradeReview (~600 LOC, ~10 paths, ~10 tests)

- **Phase 1** (RED — integration tests, 10 tests): `AlertRepositoryIntegrationTests` (5 — Created on `AddAsync` returns `true`, NO audit on `AddAsync` returns `false` (dedup), Updated with diff + `IsOwner` on `Acknowledge`, FindByIdAsync not audited, cross-tenant Denied). `TradeReviewRepositoryIntegrationTests` (5 — Created, Updated with diff + `IsOwner`, attachment ops forwarded without audit, FindByIdAsync not audited, FindByTradeIdAsync not audited).
- **Phase 2** (GREEN): `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AlertAuditDecorator.cs` (~200 LOC, bespoke — mirrors `TradeAuditDecorator` shape; critical detail: `AddAsync` returning `bool` audit semantics). `TradeReviewAuditDecorator.cs` (~250 LOC, bespoke — mirrors `JournalEntryAuditDecorator` shape; forward attachment ops without audit).
- **Phase 3** (DI wiring): `services.Decorate<IAlertRepository, AlertAuditDecorator>()` + `services.Decorate<ITradeReviewRepository, TradeReviewAuditDecorator>()`.
- **Phase 4** Validate: 10 new tests pass. Full cumulative suite (1327 + 10 = 1337) zero regression.
- **Dependencies**: 8a.1 must be merged (shares `TestTradingDbContext` fixture).
- **Rollback**: `git revert` the slice. DI registration removed. `audit.events` has no rows for Alert / TradeReview.

#### Slice 8a.3 — Trading: PlannerSession + PreTradeChecklist (~700 LOC, ~12 paths, ~12 tests)

- **Phase 1** (RED — integration tests, 12 tests): `PlannerSessionRepositoryIntegrationTests` (5 — Created, Updated with diff, `Status == PlannerStatus.Cancelled` upgrades to `Deleted` via `IsTerminated` reflection, `IsOwner` check, reads not audited: `GetByIdAsync` / `ListByUserAndWeekAsync` / `GetWeekComparisonAsync` / `ExistsForDateAsync`). `PreTradeChecklistRepositoryIntegrationTests` (3 — Created on `AddAsync`, `ListByUserIdAsync` not audited, no Update/Delete methods exist — contract pin). **Total: 8 RED scenarios.**
- **Phase 2** (GREEN): `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/PlannerSessionAuditDecorator.cs` (~200 LOC, bespoke — reimplement `IsTerminated` reflection for `PlannerStatus.Cancelled`). `PreTradeChecklistAuditDecorator.cs` (~100 LOC, bespoke write-once — only `AddAsync` wraps; reads forwarded without audit).
- **Phase 3** (DI wiring): `services.Decorate<IPlannerSessionRepository, PlannerSessionAuditDecorator>()` + `services.Decorate<IPreTradeChecklistRepository, PreTradeChecklistAuditDecorator>()`.
- **Phase 4** Validate: 8 new tests pass. Full cumulative suite (1333 + 8 = 1341) zero regression.
- **Dependencies**: 8a.2 must be merged.
- **Rollback**: `git revert` the slice. DI registration removed. `audit.events` has no rows for PlannerSession / PreTradeChecklist.

#### Slice 8b.1 — Billing: StripeCustomer (~700 LOC, ~12 paths, ~12 tests)

- **Phase 1** (RED — integration tests, 3 tests): `StripeCustomerRepositoryIntegrationTests` (3 — Created on `AddAsync` + `IsOwner` cross-tenant, `GetByUserIdAsync` + `GetByStripeCustomerIdAsync` not audited, no Update/Delete methods exist — contract pin).
- **Phase 2** (GREEN): `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Audit/StripeCustomerAuditDecorator.cs` (~120 LOC, bespoke — mirrors simplified `TenantAuditDecorator` shape; only `AddAsync` wrapped).
- **Phase 3** (DI wiring): `services.Decorate<IStripeCustomerRepository, StripeCustomerAuditDecorator>()` in `BillingModuleRegistration.cs`.
- **Phase 4** Validate: 3 new tests pass. Full cumulative suite (1341 + 3 = 1344) zero regression.
- **Dependencies**: 8a.3 must be merged.
- **Rollback**: `git revert` the slice. DI registration removed. `audit.events` has no rows for StripeCustomer.

#### Slice 8b.2 — Billing: SKIP reconciliation (~50 LOC, ~2 paths, 0 tests)

Doc-only slice. NO code changes; NO new tests; just the rationale baked into `tasks.md` + the spec REMOVED Requirements + this proposal's Out of Scope.

- **Phase 1** (4 verification commands):
  - [ ] 1.1 `git grep -E "Task (Add|Update|Delete)Async" src/2.Modules/Billing/JadeCapital.Billing.Application/Features/Subscriptions/ISubscriptionAdminRepository.cs` → no matches (proves SKIP #1: no mutations to audit).
  - [ ] 1.2 `git grep "class SubscriptionAuditDecorator" src/` → single file (proves Wave 6 still covers Subscription mutations).
  - [ ] 1.3 `grep -n "append-only" src/2.Modules/Billing/JadeCapital.Billing.Domain/Stripe/StripeWebhookEvent.cs` → matches (proves SKIP #2: entity docstring still says append-only).
  - [ ] 1.4 Add the 2 SKIP rationales to `openspec/changes/2026-08-19-wave8-audit-coverage-extended/proposal.md` §"Out of Scope (Wave 8 SKIPs — documented, not deferred)" + `specs/soft-delete-audit/spec.md` §"## REMOVED Requirements" (with `Reason:` block per OpenSpec convention).
- **Validate**: 4 commands pass + spec REMOVED Requirements present. Zero behavior change.
- **Dependencies**: 8b.1 must be merged.
- **Rollback**: `git revert` the slice. Doc-only changes revert. Zero behavior change.

## Critical Questions for User — RESOLVED (per orchestrator preflight)

The 8 decisions below were resolved before the proposal was written. Recording the resolutions here so future readers / Wave 9 sessions know the decisions were deliberate.

| # | Decision | Resolution | Source |
|---|---|---|---|
| 1 | `ISubscriptionAdminRepository` SKIP rationale | **SKIP**. No mutation methods on interface. `Subscription` aggregate already audited by Wave 6's `SubscriptionAuditDecorator` (wraps `ISubscriptionRepository`, not the admin surface). Adding an audit decorator here would be a no-op on the audit-write path; reads are never audited. | Explore §"7. ISubscriptionAdminRepository" |
| 2 | `IStripeWebhookEventRepository` SKIP rationale | **SKIP**. Append-only per Wave 6 `ISoftDelete` docstring. The `billing.stripe_webhook_events` table IS the webhook audit trail (compliance officers query it directly). Adding `AuditEvent` rows would be doubly-recorded noise. | Explore §"9. IStripeWebhookEventRepository" |
| 3 | `IAccountRepository.RemoveAsync` rename vs keep + overload? | **Rename** to `DeleteAsync(Account, ct)`. Breaking. Atomic in slice 8a.1. 1 handler call site. No overload, no deprecation period. Mirrors Wave 7 7b.1 Trade rename. | Orchestrator preflight decision 3 |
| 4 | `IInstrumentRepository.RemoveAsync` rename vs keep + overload? | **Rename** to `DeleteAsync(Instrument, ct)`. Breaking. Atomic in slice 8a.1. 1 handler call site. Mirrors Wave 7 7b.1 Trade rename. | Orchestrator preflight decision 4 |
| 5 | `IInstrumentRepository` `IsOwner` cross-tenant check? | **NO**. Instrument is a catalog entity ("NO es Aggregate Root: es una Entity compartida por todos los usuarios"). Mirrors `TenantAuditDecorator` precedent (Tenant IS the tenant boundary; Instrument is catalog data). All users see the same catalog; admin mutations are legitimate. | Orchestrator preflight decision 5 |
| 6 | `IAlertRepository.AddAsync` returns `bool` audit semantics? | **Conditional**. When `true` (inserted), emit `Created` audit. When `false` (deduped by `ux_alerts_user_rule_day` UNIQUE INDEX), emit **NO** audit row (the row was not created; the existing row's audit history is preserved). | Orchestrator preflight decision 6 |
| 7 | `TradeAttachment` separately audited? | **NO**. Child entity of `TradeReview`. Forward `AddAttachmentAsync` / `UpdateAttachmentAsync` / `RemoveAttachmentAsync` without audit. The review's Update events + handler-side MinIO cleanup log already capture attachment lifecycle. Adding per-attachment rows would be noisy without proportional compliance value. | Orchestrator preflight decision 7 |
| 8 | `PlannerSession.IsTerminated` reflection rule? | **YES**. `PlannerStatus.Cancelled` matches the Wave 6 rule (`Status ∈ {Cancelled, Terminated, Expired}`). The decorator upgrades `Updated → Deleted` when the new `Status == PlannerStatus.Cancelled`. The other 6 audited entities have no enum Status field that matches. | Orchestrator preflight decision 8 |

**No new ambiguities**: the 2 SKIPs (ISubscriptionAdminRepository, IStripeWebhookEventRepository) are documented in the proposal's Out of Scope + the spec's REMOVED Requirements. All 8 design decisions above were resolved before the proposal was written.

## Migration Path

**No schema migration required.** The decorator pattern is additive — `audit.events` table already exists (Wave 6 migration 0027). Wave 7's migration 0029 (widening the `ck_audit_events_action` CHECK constraint to `IN (0,1,2,3,4,5)`) already covers all 7 new decorators' audit-action needs. No new columns.

**No data migration required.** The `audit.events` table starts receiving TradeReview / PlannerSession / PreTradeChecklist / Account / Instrument / Alert / StripeCustomer rows from the moment the deployment completes. Historical mutations (pre-Wave 8) are not backfilled — the audit log is forward-only.

**No interface-level deprecation period.** The 2 renames (`RemoveAsync` → `DeleteAsync` on Account + Instrument) are atomic in slice 8a.1. No consumers outside the 1 trading handler each are affected (verified via `git grep` BEFORE the rename). The 7 new decorators register via `services.Decorate<IXxxRepository, XxxAuditDecorator>()` — no DB schema changes.

## Dependencies and Risks

### Dependencies (non-negotiable, from Wave 6/7 precedent)

- **Strict TDD** per `openspec/config.yaml` `apply.tdd: true`. RED tests FIRST for every new decorator. The 7 integration test files follow the existing `ImportJobRepositoryIntegrationTests` pattern (SQLite-in-memory + raw SQL `CREATE TABLE IF NOT EXISTS events ...` workaround for EF Core 9 SQLite `EnsureCreated` all-or-nothing gotcha).
- **`size:exception`** likely needed for 8a.1 + 8a.2 + 8a.3 (Wave 5/6/7 precedent — all accepted).
- **400-line PR budget**: 8a.1 = 12 paths, 8a.2 = 10, 8a.3 = 12, 8b.1 = 12, 8b.2 = 2. The 4 coverage slices exceed the 400-line PR review budget per Wave 7 7a.1 (1412 LOC) / 7b.1 (1699 LOC) / 7b.2 (991 LOC) precedent. `size:exception` per slice is the expected resolution; the `feature-branch-chain` strategy keeps each PR's reviewer-load bounded at the PR-level scope of work, not the cumulative chain scope.
- **Scrutor 4.2.2** is already explicit in Trading + Billing csprojs (Wave 7 7a.0). No csproj changes needed in Wave 8 (verified via Phase 0 of slice 8a.1).

### Risks

| # | Risk | Likelihood | Mitigation |
|---|---|---|---|
| 1 | **`RemoveAsync` → `DeleteAsync` rename breaks the 2 handlers** | Med | `git grep -n "RemoveAsync" src/2.Modules/Trading/` BEFORE the rename. Known handlers: `DeleteAccountHandler.cs:47` + `DeleteInstrumentHandler.cs:45`. Both updated in slice 8a.1 atomically. The rename is atomic on the branch. |
| 2 | **2 SKIPs need explicit rationale to survive future review** | Low | Rationale baked into this proposal's Out of Scope + spec REMOVED Requirements + tasks.md slice 8b.2 verification. Each SKIP has a 4-line rationale grounded in Wave 6 `ISoftDelete` docstring or aggregate docstring. |
| 3 | **Bespoke decorator template count** (4 bespoke + 1 write-once + 1 immutable = 6 of 7) | Med | Each ~200 LOC. Largest bespoke-dec-decorator wave since the pattern was introduced (Wave 7 had 3: RiskProfile, Trade, JournalEntry). The `TradeAuditDecorator` + `JournalEntryAuditDecorator` + `RiskProfileAuditDecorator` are the templates — Wave 8 reuses their structure verbatim. Per-slice LOC tracking with `size:exception` per Wave 7 precedent. |
| 4 | **`IAlertRepository.AddAsync` returns `bool` audit semantics** (insert vs dedup) | Low | The decorator checks the return value after the inner call. Only emits `Created` when `true`. NO audit when `false` (deduped) — preserves the existing row's audit history. Documented in slice 8a.2 Phase 1 + Decision #6 above. |
| 5 | **`PlannerSession.IsTerminated` reflection rule** | Low | `PlannerStatus.Cancelled` matches the Wave 6 rule (`Status ∈ {Cancelled, Terminated, Expired}`). The decorator re-implements the reflection locally (the generic `DecoratedRepository<T>.IsTerminated` already covers it, but the bespoke decorator needs the same logic for consistency). Per-decorator test fixture verifies the Cancelled transition upgrades `Updated → Deleted`. |
| 6 | **`IInstrumentRepository` lacks `IsOwner` — admin mutations on catalog** | Low | Catalog entity precedent (`TenantAuditDecorator` is the analog). All users see the same instrument catalog; admin mutations are legitimate. The decorator emits audit rows for Add/Update/Delete but does NOT enforce user-scope. Documented in Decision #5 above. |
| 7 | **EF Core 9 SQLite `EnsureCreated` all-or-nothing gotcha** | Med | Every new integration test must include the `CREATE TABLE IF NOT EXISTS events ...` raw SQL workaround (Wave 6 6d.2 fixture fix lines 102-128 of `TenantRepositoryIntegrationTests.cs`). The 4 new test files (Account, Instrument, Alert, TradeReview, PlannerSession, PreTradeChecklist, StripeCustomer) copy the pattern verbatim. |
| 8 | **`AuditDbContext` schema collision risk** | Low | All 7 new typed decorators share `audit.events` table. `EntityType` is the discriminator (string `nameof(TradeReview)`, `nameof(PlannerSession)`, etc.). No collision as long as each `EntityType` is unique per aggregate (verified — all 7 are distinct and no collision with the 8 Wave 6/7 entities). |
| 9 | **`TradingDbContext` array mapping fails on SQLite** | Med | TradeReview + PlannerSession + Alert + PreTradeChecklist + Account + Instrument integration tests use focused helper `DbContext`s (mirrors Wave 7's `TestTradingDbContext` pattern). Npgsql-specific array columns (e.g., `JournalEntry.Tags`) are omitted from the test mapping. |
| 10 | **Testcontainers NOT available in sandbox** | Carry-forward | All new integration tests use SQLite-in-memory per Wave 6/7 precedent. Zero Docker dependency. |
| 11 | **`AuditAction.Denied` + `AuditAction.Failed` enum values** | Low | Already present (Wave 7 7a.1 added `Denied = 4` + `Failed = 5`; migration 0029 widened the CHECK constraint). The 7 new decorators reuse these values for cross-tenant isolation. **NO new enum extension, NO new migration.** |
| 12 | **`size:exception` precedent** | Likely | Wave 5/6/7 all accepted. Wave 8 follows — expect `size:exception` for 8a.1 + 8a.2 + 8a.3. 8b.1 is borderline; 8b.2 is doc-only (no exception needed). |
| 13 | **8a.0 collapses to verified-no-op** | Low | Wave 7 7a.0 already added explicit `<PackageReference Include="Scrutor" Version="4.2.2" />` to Trading + Billing csprojs. Slice 8a.0 is omitted from the PR chain; Phase 0 of slice 8a.1 verifies via `git diff --stat`. If verification fails, restore the missing reference and proceed. |

## Affected Areas

| Area | Impact | Description |
|---|---|---|
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/Repositories/IAccountRepository.cs` | Modified | `RemoveAsync(Account)` → `DeleteAsync(Account, ct)` rename (8a.1). Extend `IRepository<Account>` shape (add `GetByIdAsync(Guid, ct)` if missing — keep both, decorator forwards `FindByIdAsync` to inner). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Persistence/Repositories.cs:147` | Modified | `AccountRepository.RemoveAsync` impl → `DeleteAsync` (8a.1). |
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Accounts/DeleteAccount/DeleteAccountHandler.cs:47` | Modified | Update call site from `_accounts.RemoveAsync(account)` → `_accounts.DeleteAsync(account, ct)` (8a.1). |
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Abstractions/Repositories/IInstrumentRepository.cs` | Modified | `RemoveAsync(Instrument)` → `DeleteAsync(Instrument, ct)` rename (8a.1). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Persistence/Repositories.cs:194` | Modified | `InstrumentRepository.RemoveAsync` impl → `DeleteAsync` (8a.1). |
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Features/Instruments/DeleteInstrument/DeleteInstrumentHandler.cs:45` | Modified | Update call site from `_instruments.RemoveAsync(instrument)` → `_instruments.DeleteAsync(instrument, ct)` (8a.1). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AccountAuditDecorator.cs` | New | Typed decorator — generic `DecoratedRepository<Account>` + `IsOwner` (8a.1). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/InstrumentAuditDecorator.cs` | New | Typed decorator — generic `DecoratedRepository<Instrument>`, NO `IsOwner` (catalog entity) (8a.1). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/AlertAuditDecorator.cs` | New | Typed decorator — bespoke; `AddAsync` returns `bool` audit semantics (8a.2). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/TradeReviewAuditDecorator.cs` | New | Typed decorator — bespoke; forward attachment ops without audit (8a.2). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/PlannerSessionAuditDecorator.cs` | New | Typed decorator — bespoke; `IsTerminated` reflection on `PlannerStatus.Cancelled` (8a.3). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/Audit/PreTradeChecklistAuditDecorator.cs` | New | Typed decorator — bespoke write-once; only `AddAsync` wraps (8a.3). |
| `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Audit/StripeCustomerAuditDecorator.cs` | New | Typed decorator — bespoke immutable; only `AddAsync` wraps (8b.1). |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs` | Modified | 5× `services.Decorate<IXxxRepository, XxxAuditDecorator>()` calls (8a.1, 8a.2, 8a.3). |
| `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/DependencyInjection/BillingModuleRegistration.cs` | Modified | 1× `services.Decorate<IStripeCustomerRepository, StripeCustomerAuditDecorator>()` call (8b.1). |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/AccountRepositoryIntegrationTests.cs` | New | 5 scenarios (8a.1). |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/InstrumentRepositoryIntegrationTests.cs` | New | 5 scenarios (8a.1). |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/IAccountRepositoryContractTests.cs` | New | 2 contract tests (rename pin) (8a.1). |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/IInstrumentRepositoryContractTests.cs` | New | 2 contract tests (rename pin) (8a.1). |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/AlertRepositoryIntegrationTests.cs` | New | 5 scenarios (8a.2). |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/TradeReviewRepositoryIntegrationTests.cs` | New | 5 scenarios (8a.2). |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/PlannerSessionRepositoryIntegrationTests.cs` | New | 5 scenarios (8a.3). |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/PreTradeChecklistRepositoryIntegrationTests.cs` | New | 3 scenarios (8a.3). |
| `tests/UnitTests/JadeCapital.Identity.UnitTests/Persistence/StripeCustomerRepositoryIntegrationTests.cs` | New | 3 scenarios (8b.1). |
| `openspec/specs/soft-delete-audit/spec.md` | Modified | Delta spec: extend "Decorator coverage" requirement to include the 7 new aggregates + add REMOVED Requirements section for `ISubscriptionAdminRepository` + `IStripeWebhookEventRepository` (sdd-spec phase). |

## Success Criteria

1. **Account audit (8a.1)**: `CreateAccountHandler` → `AccountAuditDecorator.AddAsync` → `audit.events` row with `Action = Created`, `EntityType = "Account"`, `UserId = …`. `UpdateAccountHandler` (`UpdateMetadata` / `Deactivate` / `Reactivate`) → `Updated` with diff + `IsOwner` check. `DeleteAccountHandler` (now `DeleteAsync(Account, ct)`) → `Deleted`. Cross-tenant update → `Denied` event + `UnauthorizedAccessException`. `AccountRepositoryIntegrationTests` (5 scenarios) + 2 contract tests all pass.
2. **Instrument audit (8a.1)**: `CreateInstrumentHandler` → `Created`. `UpdateInstrumentHandler` → `Updated` with diff (no `IsOwner`). `DeleteInstrumentHandler` (now `DeleteAsync(Instrument, ct)`) → `Deleted`. `InstrumentRepositoryIntegrationTests` (5 scenarios) + 2 contract tests all pass. The `RemoveAsync` signature no longer exists on either interface.
3. **Alert audit (8a.2)**: `AddAsync(alert)` returns `true` → `Created`. Returns `false` (deduped by `ux_alerts_user_rule_day`) → NO audit row. `UpdateAsync(alert)` (after `Acknowledge()`) → `Updated` with `AcknowledgedAt: null → now` diff + `IsOwner` check. Cross-tenant update → `Denied`. `AlertRepositoryIntegrationTests` (5 scenarios) all pass.
4. **TradeReview audit (8a.2)**: `AddAsync(review)` → `Created`. `UpdateAsync(review)` → `Updated` with diff + `IsOwner` check on `review.UserId`. `AddAttachmentAsync` / `UpdateAttachmentAsync` / `RemoveAttachmentAsync` forwarded without audit. Cross-tenant update → `Denied`. `TradeReviewRepositoryIntegrationTests` (5 scenarios) all pass.
5. **PlannerSession audit (8a.3)**: `AddAsync(session)` → `Created`. `UpdateAsync(session)` → `Updated` with diff + `IsOwner` check. When `Status == PlannerStatus.Cancelled`, the reflection upgrades `Updated → Deleted`. Reads (`GetByIdAsync` / `ListByUserAndWeekAsync` / `GetWeekComparisonAsync` / `ExistsForDateAsync`) not audited. `PlannerSessionRepositoryIntegrationTests` (5 scenarios) all pass.
6. **PreTradeChecklist audit (8a.3)**: `AddAsync(checklist)` → `Created`. `ListByUserIdAsync` not audited. No Update/Delete methods exist (contract pin test). `PreTradeChecklistRepositoryIntegrationTests` (3 scenarios) all pass.
7. **StripeCustomer audit (8b.1)**: `AddAsync(customer)` → `Created` + `IsOwner` check on `customer.UserId`. `GetByUserIdAsync` / `GetByStripeCustomerIdAsync` not audited. No Update/Delete methods exist (contract pin test). `StripeCustomerRepositoryIntegrationTests` (3 scenarios) all pass.
8. **SKIP reconciliation (8b.2)**: `ISubscriptionAdminRepository` has no mutation methods (verified via `git grep`). `SubscriptionAuditDecorator` still wraps `ISubscriptionRepository` (verified). `StripeWebhookEvent` docstring still says "append-only after Record" (verified). Spec REMOVED Requirements section present with `Reason:` blocks for both SKIPs.
9. **Build green**: `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings (vs Wave 7 baseline of 3 pre-existing CA2263).
10. **Test cumulative**: 1313 (Wave 7) + 35 (Wave 8 integration + contract tests) = ~1348 tests pass. The ~37 spec scenarios vs. the 35 implementation tests reflects that PreTradeChecklist + StripeCustomer each audit only one mutation method (AddAsync) — the spec scenario count exceeds the test count when factoring in `Given/When/Then` decompositions. Zero regressions across the entire BE suite.
11. **Audit log queryable**: `SELECT * FROM audit.events WHERE entity_type IN ('TradeReview', 'PlannerSession', 'PreTradeChecklist', 'Account', 'Instrument', 'Alert', 'StripeCustomer') AND tenant_id = ...` returns the full history of mutations, newest first. Same queryability inherited from Wave 6 (migration 0027 + indexes).
12. **All 5 slices under 48 paths** (mandatory). 8a.1=12, 8a.2=10, 8a.3=12, 8b.1=12, 8b.2=2. `size:exception` expected for 8a.1 + 8a.2 + 8a.3 (Wave 5/6/7 precedent).
13. **PR chain intact**: 5 PRs (#25-#29), each targeting the previous PR's branch. Order matches slice order. No PR targets `main` directly.

## Non-Goals and Later Waves

- **Wave 9**: Audit decorator coverage for `RiskConfiguration`, `PlanVersion`, `JournalDaily`, `BacktestRun`, `StrategyVersion`, `TradeTag`, `Note`, `Mood`, `BehavioralMetric`, etc. — the long-tail user-owned aggregates.
- **Wave 9**: Admin-only `GET /api/audit/events` query API (filter by `entity_type`, `action`, `user_id`, `tenant_id`, date range, free-text on `changes`). User-facing read API (`GET /api/audit/me`) for "my mutation history" tab.
- **Wave 9**: Audit log retention policy (90-day default) + auto-purge hosted service. Configurable per tenant.
- **Wave 9**: Audit log export (CSV / JSON) for compliance officers.
- **Wave 9**: Soft-delete cascade propagation for the 7 newly-decorated aggregates (similar to `ImportJob` Wave 6 precedent).
- **Wave 9**: `AuditAction.Restored` end-to-end support (soft-delete restore command + admin tooling).
- **Wave 9+**: Migration to a different audit sink (Kafka, S3, external SIEM). Post-1.0.
- **Wave 9+**: Bulk audit events for `AddRangeAsync` (currently 1 row per entity; bulk path emits 1 batch row with `EntityCount = N`).
- **Wave 10+**: Cross-tenant audit log access (today: tenant-isolated; future: admin tooling for cross-tenant forensics).

## Out of Scope (explicit scope boundary)

- **NOT** adding `ISoftDelete` to any of the 7 newly-decorated aggregates. `PlannerSession` uses `PlannerStatus.Cancelled` via the `IsTerminated` reflection rule (covers soft-delete-via-status). The other 6 emit straight `AuditAction.Updated` on mutations — no soft-delete semantics to audit differently.
- **NOT** changing the `audit.events` schema. Migration 0027 from Wave 6 + migration 0029 from Wave 7 already cover `EntityType`, `EntityId`, `Action`, `TenantId`, `UserId`, `Changes JSONB`, `OccurredAt`. No new columns.
- **NOT** changing the `AuditAction` enum. Wave 7's `Denied = 4` + `Failed = 5` already cover all 7 new decorators' audit-action needs.
- **NOT** changing the `DecoratedRepository<T>` core implementation. The helper is unchanged from Wave 7 7a.0.
- **NOT** changing `AuditDbContext` / `AuditLogger` / `NoOpAuditLogger` ownership (stays in `Identity.Infrastructure`).
- **NOT** addressing the `JournalEntry.Tags` array mapping in production code (stays as Npgsql-specific). The Wave 8 integration tests use focused helper `DbContext`s that omit the array column.
- **NOT** addressing the `Money` complex type in production code. The Wave 8 integration tests use focused helper `DbContext`s.
- **NOT** adding `Restored` action path (deletion is one-way today; restore is admin tooling in Wave 9).
- **NOT** unifying the per-aggregate decorator pattern. Each typed decorator is bespoke to its aggregate's mutation surface (Alert's `AddAsync` returns `bool`, TradeReview's attachment ops are forwarded without audit, PlannerSession's `IsTerminated` reflection, PreTradeChecklist's write-once, StripeCustomer's immutable). Unification is post-Wave 9 if patterns stabilize.
- **NOT** auditing `ISubscriptionAdminRepository` or `IStripeWebhookEventRepository` (the 2 documented SKIPs). See "Out of Scope (Wave 8 SKIPs — documented, not deferred)" above.
