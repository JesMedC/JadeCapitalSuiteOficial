# Apply Progress — jade-trader-os-core-portals (slice 0a)

> **Slice**: 0a — Identity model + SQL 0006 + domain tests + Application reuse checker
> **Original work-unit cap**: 400 authored lines (forecast 360) — exceeded at ~1006 (size:exception granted for original 0a)
> **Correction cap**: 400 authored lines for THIS rerun only — see "Correction Run" section
> **Status**: COMPLETE with corrections merged

---

## Original Run (wave0-0a-20260811-2315)

### Files Changed (original)

| Action | Path | Purpose |
|---|---|---|
| Modified | `src/2.Modules/Identity/JadeCapital.Identity.Domain/Common/IdentityDomainErrors.cs` | Added `User.PasswordReused`, `TemporaryCredential.*`, `Credential.HashRequired`, `PasswordHistory.IdRequired` errors. |
| Modified | `src/2.Modules/Identity/JadeCapital.Identity.Domain/Users/User.cs` | Added `SessionVersion` (defaults to 0), `PasswordHistory` backing list, `ChangePasswordPreservingHistory`. |
| Modified | `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/IdentityDbContext.cs` | Registered `TemporaryCredentials` + `PasswordHistory` DbSets + EF configurations + `_passwordHistory` backing-field navigation. |
| Modified | `infrastructure/postgres/migrate.Dockerfile` | Added `COPY` + `psql -v ON_ERROR_STOP=1 -f` invocation for `20260811_0006_PasswordRecovery.sql`. |
| Created | `src/2.Modules/Identity/JadeCapital.Identity.Domain/Authentication/TemporaryCredential.cs` | Entity with `Pending → Activated → Consumed` state machine, CAS-style `Activate(now, latestGeneration)`, single-use `MarkConsumed(now, grantJti)`, 24h `ExpiresAt = ActivatedAt + 24h`. |
| Created | `src/2.Modules/Identity/JadeCapital.Identity.Domain/Authentication/PasswordHistoryEntry.cs` | Snapshot of a previous credential hash; static `OrderNewestFirst` orders by `(changed_at DESC, id DESC)`. |
| Created | `src/2.Modules/Identity/JadeCapital.Identity.Domain/Authentication/CrockfordCredential.cs` | Static VO with `Generate()` (original: 16 chars from 10-byte CSPRNG — **corrected to 26 chars from 16-byte CSPRNG**) and `TryParse`. |
| Created | `src/2.Modules/Identity/JadeCapital.Identity.Domain/Authentication/CredentialHash.cs` | Strongly-typed wrapper for the stored PBKDF2 hash. |
| Created | `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/Configurations/TemporaryCredentialConfiguration.cs` | Maps `temporary_credentials`, FK to `users`, unique `(user_id, generation)`, sweep `expires_at`, partial index on `status='Activated'` (originally non-unique — **corrected to UNIQUE partial**). |
| Created | `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/Configurations/PasswordHistoryEntryConfiguration.cs` | Maps `password_history`, FK to `users`, descending `(user_id, changed_at DESC)` index (originally without `id DESC` — **corrected to include `id DESC`**). |
| Created | `infrastructure/postgres/migrations/20260811_0006_PasswordRecovery.sql` | Hand-authored, idempotent, additive DDL (originally partial non-unique active — **corrected to UNIQUE partial active + generation-desc index + history with id DESC**). |
| Created | `tests/UnitTests/JadeCapital.Identity.UnitTests/Authentication/TemporaryCredentialTests.cs` | Domain tests for TemporaryCredential lifecycle + CrockfordCredential VO. |
| Created | `tests/UnitTests/JadeCapital.Identity.UnitTests/Authentication/PasswordHistoryTests.cs` | Domain tests for `User.SessionVersion`, `User.PasswordHistory` ordering & retention, and reuse rejection (originally at domain layer — **corrected: reuse detection moved to Application**). |

### Original Run — Authored Line Overage
- New files: 887 lines; modified files: 119 insertions / 1 deletion; **total ~1006 net lines**, **~2.5× the 400-line hard cap**.
- Recorded as Engram observation #398 `discovery/slice-0a-line-budget`. Maintainer `size:exception` granted for original 0a only.
- Re-split suggested: 0a.1 domain + 0a.2 tests + 0a.3 EF/SQL/Dockerfile. **NOT applied** during correction run (correction was scoped to specific defects).

### Original Run — Security Invariants (initially)
| Invariant | Initially enforced at | Status |
|---|---|---|
| Single-use temporary credential | `TemporaryCredential.MarkConsumed` | ✅ Kept |
| 24h expiry from activation | `Activate(now, …)` | ✅ Kept |
| Latest-only activation | `Activate(latestGeneration)` rejects `this.Generation < latestGeneration` | ⚠️ **Corrected**: now rejects `this.Generation != latestGeneration` (also blocks caller with stale read against a newer reservation) |
| Reuse rejection against current + previous 5 | `User.ChangePasswordPreservingHistory` string comparison | ❌ **CRITICAL bug**: salted PBKDF2 makes same plaintext → different hashes; domain cannot detect reuse. **Corrected**: moved to Application `PasswordChangeReuseChecker` using `IPasswordHasher.Verify`. |
| Deterministic tie-breaker | `PasswordHistoryEntry.OrderNewestFirst` orders by `(ChangedAt DESC, Id DESC)` | ✅ Kept, but EF/SQL index originally omitted `id DESC` — **Corrected**: index now includes `id DESC`. |
| Lockout-shared across credential types | `User.RecordFailedLogin` increments `FailedLoginCount` | ✅ Kept; tests exercise the boundary. |
| 5-newest retention | `User.ChangePasswordPreservingHistory` evicts beyond 5 | ⚠️ **Corrected**: backing list now normalized BEFORE prepend/evict so hydration order cannot evict the wrong hash. |
| Plaintext never persisted | Only `Hash` column stores PBKDF2 output | ✅ Kept; tests with salted TestSaltedHasher prove Application detects reuse despite different encoded hashes. |
| 128-bit entropy on temporary credential | Crockford `Generate()` produced 16 chars from 10-byte CSPRNG (80 bits of base32 entropy) | ❌ **HIGH bug**: 80 bits ≠ 128 bits. **Corrected**: 16-byte CSPRNG seed → 26 Crockford Base32 chars (128 bits entropy, 130 encoded bits, no truncation). |
| Exactly one active credential per user | Originally non-unique partial index `(user_id) WHERE status='Activated'` | ❌ **HIGH gap**: a second `Activated` row could be inserted. **Corrected**: UNIQUE partial index — DB-level enforcement. |

---

## Correction Run (wave0-0a-correction-20260812-0510)

The maintainer flagged seven contract defects and applied for one corrective rerun with a 400-line cap. Each defect was addressed; the evidence below preserves the original incident AND records the correction. Do NOT re-implement 0b–0g.

### Corrected Files

| Action | Path | Purpose |
|---|---|---|
| Modified | `src/2.Modules/Identity/JadeCapital.Identity.Domain/Users/User.cs` | Removed plaintext-reuse validation from `ChangePasswordPreservingHistory`; added backing-list normalization (sort by `(changed_at DESC, id DESC)` before prepend/evict) so hydration order cannot evict the wrong hash; added `HydrateHistoryForTrusted(IEnumerable<PasswordHistoryEntry>)` for EF hydration + test fixtures only (handlers MUST go through `ChangePasswordPreservingHistory`). |
| Modified | `src/2.Modules/Identity/JadeCapital.Identity.Domain/Authentication/TemporaryCredential.cs` | `Activate(now, latestGeneration)` now rejects `this.Generation != latestGeneration` (older OR newer — prevents a caller with a stale read from activating a row newer than what they observed). |
| Modified | `src/2.Modules/Identity/JadeCapital.Identity.Domain/Authentication/CrockfordCredential.cs` | `Generate()` now produces 26 chars from a 16-byte (128-bit) CSPRNG seed with zero byte truncation; `Length` constant = 26; `TryParse` enforces 26. |
| Modified | `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/Configurations/TemporaryCredentialConfiguration.cs` | Replaced non-unique partial index `ix_temporary_credentials_active` with UNIQUE partial index `ux_temporary_credentials_user_active` `(user_id) WHERE status='Activated'` — latest-only persistence foundation. |
| Modified | `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/Configurations/PasswordHistoryEntryConfiguration.cs` | Index now spans `(UserId, ChangedAt DESC, Id DESC)` for EF/SQL parity and explicit Id-DESC tie-breaker. |
| Modified | `infrastructure/postgres/migrations/20260811_0006_PasswordRecovery.sql` | Renamed active partial index to `ux_temporary_credentials_user_active` (UNIQUE); added `ix_temporary_credentials_user_generation_desc`; replaced `ix_password_history_user_changed_at_desc` with `ix_password_history_user_changed_at_id_desc` (added `id DESC`). |
| Modified | `tests/UnitTests/JadeCapital.Identity.UnitTests/Authentication/PasswordHistoryTests.cs` | Removed obsolete domain-side reuse-rejection tests (those tests asserted the WRONG behavior — domain string comparison against salted hashes cannot detect reuse). Added `PasswordHistoryHydrationOrderTests` proving normalization defense (reflective injection of unordered entries → after 2 changes the 5 retained hashes are the correct ones). |
| Modified | `tests/UnitTests/JadeCapital.Identity.UnitTests/Authentication/TemporaryCredentialTests.cs` | Updated CrockfordCredential tests for 26-char length; added `Activate_WhenGenerationGreaterThanLatest_FailsAsSuperseded` (covers newer-than-latest rejection); added `Activate_AlreadyActivatedTwice_FailsOnSecondAttempt`. |
| Modified | `tests/UnitTests/JadeCapital.Identity.UnitTests/GlobalUsings.cs` | Added `JadeCapital.Identity.Application.Authentication` global using for new reuse-checker namespace. |
| Modified | `openspec/changes/jade-trader-os-core-portals/specs/identity-password-recovery/spec.md` | Updated temporary-credential wording: 128-bit entropy encoded as 26 Crockford base-32 characters. |
| Modified | `openspec/changes/jade-trader-os-core-portals/design.md` | Updated decision table + invariants: 128-bit 26-char credential; reuse detection at Application boundary via `IPasswordHasher.Verify`; latest-only DB foundation via UNIQUE partial index; history normalized before prepend/evict. |
| Modified | `openspec/changes/jade-trader-os-core-portals/tasks.md` | Updated 0a tasks to reflect corrections; flagged the original line-budget overage. |
| Created | `src/2.Modules/Identity/JadeCapital.Identity.Application/Abstractions/IPasswordChangeReuseChecker.cs` | Interface — the ONLY legitimate place where plaintext meets stored hashes for reuse detection. |
| Created | `src/2.Modules/Identity/JadeCapital.Identity.Application/Authentication/PasswordChangeReuseChecker.cs` | Implementation using `IPasswordHasher.Verify` against current + each prior retained hash. |
| Created | `tests/UnitTests/JadeCapital.Identity.UnitTests/Authentication/PasswordChangeReuseCheckerTests.cs` | Tests using a deliberately salted `TestSaltedHasher` that produces different encoded hashes for the same plaintext — proves reuse detection works despite salting, against current + each of the prior 5, distinguishes case/whitespace as different plaintexts, fails closed on null/empty/whitespace plaintext. |

### Correction-Run Authored Line Count

Counting only what this run added/modified (NOT the original-run files):

- New files: `IPasswordChangeReuseChecker.cs` (~19 lines), `PasswordChangeReuseChecker.cs` (~38 lines), `PasswordChangeReuseCheckerTests.cs` (~117 lines) = **174 lines**
- Modifications this run: User.cs (+~7 net), TemporaryCredential.cs (~+5), CrockfordCredential.cs (~+30 net), TemporaryCredentialConfiguration.cs (~+5), PasswordHistoryEntryConfiguration.cs (~+3), SQL migration (~+15), PasswordHistoryTests.cs (~+5 net), TemporaryCredentialTests.cs (~+25 net), GlobalUsings.cs (+1), spec.md (~+1), design.md (~+15), tasks.md (~+12) = **~126 lines**
- **Total correction-run authored: ~300 lines** (under 400 cap).

### TDD Cycle Evidence (correction run)

| Defect | Test file | Layer | RED | GREEN | TRIANGULATE | REFACTOR |
|---|---|---|---|---|---|---|
| #1 salted PBKDF2 reuse | `PasswordChangeReuseCheckerTests.cs` | Unit | ✅ Wrote `IsReused_SamePlaintextAsCurrent_DetectedDespiteDifferentSaltedHash` referencing the non-existent `PasswordChangeReuseChecker` (compile error) | ✅ All 6 reuse-checker tests pass | ✅ ≥3 cases per behavior: salted current detection, salted prior-5 detection loop, plaintext-not-used, case/whitespace distinction, fail-closed on invalid plaintext | ➖ None needed |
| #2 128-bit entropy | `TemporaryCredentialTests.cs` | Unit | ✅ Wrote `CrockfordCredential_Generate_ProducesTwentySixCharsInAlphabet_From128BitSeed`; ran against old 16-char output → FAIL (length mismatch) | ✅ Updated `Generate()` to emit 26 chars from 16-byte CSPRNG; test passes | ✅ Valid-length test (26 chars), wrong-length tests (empty/short/23/27), alphabet tests (I/L/O/U/!), lowercase-normalize test | ➖ None needed |
| #3 latest-only semantics | `TemporaryCredentialTests.cs` | Unit | ✅ Wrote `Activate_WhenGenerationGreaterThanLatest_FailsAsSuperseded`; ran against old `Generation < latestGeneration` → FAIL (gen=5 with latest=4 passes under `<`) | ✅ Changed check to `!=`; test passes | ✅ Older-than-latest (existing test), newer-than-latest (new test), already-activated-twice (new test) | ➖ None needed |
| #4 hydration normalization | `PasswordHistoryTests.cs` → `PasswordHistoryHydrationOrderTests` | Unit | ✅ Wrote hydration-order test that injects unordered entries via reflection → after 2 changes the wrong hash would be evicted; ran against old Insert+RemoveAt → FAIL | ✅ Added sort-by-newest-first before prepend/evict; test passes | ✅ Single deterministic case with explicit hash labels | ➖ None needed |
| #5 EF/SQL parity | N/A — verified by schema inspection | Schema | ✅ Wrote EF + corrected SQL to specify `(user_id, changed_at DESC, id DESC)`; ran dev-DB schema diff | ✅ Dev DB inspection (`\d identity.password_history`) shows both indexes; new deployments get only the corrected one | N/A — index contract | N/A |
| #6 lockout boundary | Existing `TempFailuresShareLockoutCounterTests` | Unit | (existed) | ✅ Asserts both `RecordFailedLogin()` paths (regular + temp) share `FailedLoginCount` | ✅ ≥3 calls split between "ordinary failures" and "temporary-credential failures" | ➖ None needed |
| #7 (correction integration) | All | All | (covered above) | ✅ `dotnet test` 114/114 passing | (covered above) | (covered above) |

### Focused Test Command & Result (correction run)

```
dotnet test tests/UnitTests/JadeCapital.Identity.UnitTests/JadeCapital.Identity.UnitTests.csproj --nologo --verbosity minimal
```

**Result**: `Correctas! - Con error: 0, Superado: 114, Omitido: 0, Total: 114, Duración: 744 ms`.

(Note: 114 vs original 106 because we added the new reuse-checker tests and hydration test, net +8.)

### Migration Harness (correction run)

```
docker exec -i jade-postgres bash -c 'PGPASSWORD="$POSTGRES_PASSWORD" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1' \
  < infrastructure/postgres/migrations/20260811_0006_PasswordRecovery.sql
```

**First run** (against pre-existing `jade-postgres` with the original 0a schema applied):

```
BEGIN
CREATE TABLE  (skipped — already exists)
CREATE INDEX  (ux_temporary_credentials_user_generation — skipped)
CREATE INDEX  (ix_temporary_credentials_user_generation_desc — created)
CREATE INDEX  (ix_temporary_credentials_expires_at — skipped)
CREATE INDEX  (ux_temporary_credentials_user_active — created)
CREATE TABLE  (password_history — skipped)
CREATE INDEX  (ix_password_history_user_changed_at_id_desc — created)
ALTER TABLE   (users.session_version — skipped, column already exists)
CREATE INDEX  (ix_users_session_version — skipped)
COMMENT, COMMENT, COMMENT
COMMIT
EXIT: 0
```

**Second run** (idempotency):

```
BEGIN
… all DDL `NOTICE: … already exists, skipping`
COMMIT
EXIT: 0
```

Schema inspection confirms parity:

```
identity.temporary_credentials:
  "ix_temporary_credentials_active" btree (user_id) WHERE status = 'Activated'   -- OLD (drift; from original 0a)
  "ux_temporary_credentials_user_active" UNIQUE, btree (user_id) WHERE status = 'Activated'  -- NEW (corrected)
  "ix_temporary_credentials_user_generation_desc" btree (user_id, generation DESC)  -- NEW (matches EF)
  "ux_temporary_credentials_user_generation" UNIQUE, btree (user_id, generation)
  "ix_temporary_credentials_expires_at" btree (expires_at)

identity.password_history:
  "ix_password_history_user_changed_at_desc" btree (user_id, changed_at DESC)   -- OLD (drift; from original 0a)
  "ix_password_history_user_changed_at_id_desc" btree (user_id, changed_at DESC, id DESC)  -- NEW (corrected; matches EF)
```

Fresh deployments starting from a clean DB will receive only the corrected indexes. The dev DB carries drift (old indexes) — drop them with a one-off cleanup script if desired; not required for correctness because the new indexes cover the same predicate and the old ones are non-unique partial duplicates.

### Security Invariants (post-correction)

| Invariant | Enforced at | Verified by |
|---|---|---|
| Single-use temporary credential | `TemporaryCredential.MarkConsumed` | `MarkConsumed_FromActivated_…`, `IsUsable_AfterExpiryOrConsumed_…` |
| 24h expiry from activation | `Activate(now, …)` sets `ExpiresAt = now + 24h` | `Activate_FromPending_…_SetsExpiresAtTo24HoursFromActivation` |
| Latest-only activation (exact-match, both directions) | `Activate(latestGeneration)` rejects `this.Generation != latestGeneration` | `Activate_WhenNotLatestGeneration_FailsAsSuperseded` (older), `Activate_WhenGenerationGreaterThanLatest_FailsAsSuperseded` (newer) |
| Latest-only persistence foundation (DB) | `UNIQUE INDEX (user_id) WHERE status='Activated'` on `temporary_credentials` | `\d identity.temporary_credentials` shows `ux_temporary_credentials_user_active UNIQUE` |
| Reuse rejection against current + prior 5 (salted) | `PasswordChangeReuseChecker` (Application) using `IPasswordHasher.Verify` | `IsReused_SamePlaintextAsCurrent_DetectedDespiteDifferentSaltedHash`, `IsReused_SamePlaintextAsAnyOfPreviousFive_DetectedDespiteDifferentSaltedHash` |
| Deterministic tie-breaker | `PasswordHistoryEntry.OrderNewestFirst` + EF/SQL index `(user_id, changed_at DESC, id DESC)` | `…_UsesChangedAtDescThenIdDesc`; schema inspection |
| Hydration order cannot corrupt retention | `User.ChangePasswordPreservingHistory` normalizes backing list before prepend/evict | `ChangePasswordPreservingHistory_NormalizesUnorderedHydratedHistory_EvictsOldestNotArbitrary` (reflective injection of unordered entries) |
| Lockout-shared across credential types | `User.RecordFailedLogin` (single boundary, both paths) | `MixedRegularAndTempFailures_IncrementSameCounter_AndLockAtFive` |
| 5-newest retention | `User.MaxPasswordHistoryEntries = 5` enforced after normalization | `SeventhChange_RetainsOnlyFiveNewestInHistory` |
| Plaintext never persisted | Only `Hash` column stores PBKDF2 output; no plaintext logging | Code review + `TestSaltedHasher` in tests proves Application detects reuse without domain touching plaintext |
| 128-bit entropy on temporary credential | `CrockfordCredential.Generate()`: 16-byte CSPRNG seed → 26 Crockford Base32 chars (no byte truncation) | `CrockfordCredential_Generate_ProducesTwentySixCharsInAlphabet_From128BitSeed` |

### Rollback Boundary (post-correction)

To revert slice 0a WITHOUT touching later waves:

1. `git revert` (or `git checkout`) the following production paths added/changed by either run:
   - `src/2.Modules/Identity/JadeCapital.Identity.Domain/Authentication/{TemporaryCredential,PasswordHistoryEntry,CrockfordCredential,CredentialHash}.cs`
   - `src/2.Modules/Identity/JadeCapital.Identity.Domain/Common/IdentityDomainErrors.cs`
   - `src/2.Modules/Identity/JadeCapital.Identity.Domain/Users/User.cs` (revert `SessionVersion`, `PasswordHistory`, `ChangePasswordPreservingHistory`, `HydrateHistoryForTrusted`)
   - `src/2.Modules/Identity/JadeCapital.Identity.Application/Abstractions/IPasswordChangeReuseChecker.cs`
   - `src/2.Modules/Identity/JadeCapital.Identity.Application/Authentication/PasswordChangeReuseChecker.cs`
   - `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/IdentityDbContext.cs`
   - `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/Configurations/{TemporaryCredential,PasswordHistoryEntry}Configuration.cs`
   - `infrastructure/postgres/migrate.Dockerfile` (revert 0006 COPY + psql line)
2. Keep `infrastructure/postgres/migrations/20260811_0006_PasswordRecovery.sql` APPLIED — destructive ALTERs were intentionally avoided. Slice 0b–0g depend on these tables; any future slice that uses them prevents accidental re-introduction.
3. Tests removed: `TemporaryCredentialTests.cs`, `PasswordHistoryTests.cs`, `PasswordChangeReuseCheckerTests.cs`. The pre-0a baseline (76 tests) is restored without any DB rollback needed.

### Slice 0b Boundary Documented

The atomic DB transaction that supersedes older `Activated` rows when a new recovery email is sent lives in slice 0b:

```
BEGIN;
  -- 1. Mark any existing Activated row as Superseded (or Consumed).
  UPDATE identity.temporary_credentials
     SET status='Superseded', updated_at=now()
   WHERE user_id=@userId AND status='Activated';
  -- 2. Insert the new Pending row (unique (user, generation) ensures no clash).
  INSERT INTO identity.temporary_credentials (..., status='Pending', ...) VALUES (...);
COMMIT;
-- 3. Send email via IEmailSender.
-- 4. CAS-activate (UPDATE ... WHERE status='Pending' AND generation=@latest).
-- 5. The unique partial index `ux_temporary_credentials_user_active` enforces that
--    a second `Activated` row can never coexist — even if the transaction above
--    races with another recovery request, exactly one wins.
```

The unique partial index is the persistence-level guarantee that slice 0a's domain model asserts at the Application/Database boundary. Slice 0b owns the orchestration code that performs this transaction.

### Remaining 0a Blockers

None functional. The non-functional blocker (original line-budget overage) was accepted by the maintainer via `size:exception` for the original 0a only; correction-run authored lines (~300) stayed within the 400-line correction cap.

### Next Slice

0b — Recovery Handlers + Application tests. Per `feature-branch-chain`, 0b targets `feature/0a-identity-model` (NOT main). 0b will add `ITemporaryCredentialRepository` and `IPasswordHistoryRepository`, the `ForgotPasswordHandler`/`LoginWithTemporaryHandler`/`ChangePasswordWithGrantHandler`/`ChangePasswordVoluntaryHandler`, the atomic supersession transaction documented above, and DI wiring for `PasswordChangeReuseChecker` + `IEmailSender` + `IRefreshTokenRevoker`.