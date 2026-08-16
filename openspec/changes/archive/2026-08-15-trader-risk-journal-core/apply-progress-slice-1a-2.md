# Apply Progress — 2026-08-15-trader-risk-journal-core (slice 1a.2)

> **Slice**: 1a.2 — Risk profile frontend (service + state + tab + settings wiring + 4 tests)
> **Forecast work-unit cap**: 400 authored lines per PR — **EXCEEDED at ~1108 net lines across 2 chained commits** (size:exception)
> **Mode**: Standard (no strict TDD for Angular slice; backend already shipped RED→GREEN in 1a.1)
> **Status**: COMPLETE — implementation correct, all 64 jest tests green, ng build succeeds
> **Branch**: `feature/0a-identity-model`
> **Commits**: `bdce743` (1a.2a: service + state + 13 tests) → `86d1e61` (1a.2b: component + settings wiring + 4 tests) → `0f8e77e` (tasks.md checkboxes)

---

## Scope STRICTLY limited

Slice 1a.2 ONLY. Did NOT touch: backend, Trading, Billing, Admin, Identity.Domain, openapi docker-compose.yml (no service additions). Edits limited to:

- `frontend/src/app/core/api/risk-profile.*` (3 new files: types, service, service spec)
- `frontend/src/app/core/state/risk-profile.state.*` (2 new files: state, state spec)
- `frontend/src/app/features/trader/settings/risk-profile/` (2 new files: tab component, tab spec)
- `frontend/src/app/features/trader/settings/settings.page.ts` (added `'risk-profile'` to `SettingsTab` union + tab button + 3rd `@if` branch + import)
- `openspec/changes/2026-08-15-trader-risk-journal-core/tasks.md` (marked 1a.2 sub-tasks complete)

NO modifications to backend, no docker-compose changes, no other features.

---

## Files Created

| Action | Path | Purpose |
|--------|------|---------|
| Created | `frontend/src/app/core/api/risk-profile.types.ts` | `RiskProfileDto`, `UpsertRiskProfileRequest`, RFC 7807 `RiskProfileProblem`, and the normalized `RiskProfileError` (status / code / message / field?). |
| Created | `frontend/src/app/core/api/risk-profile.service.ts` | `RiskProfileService` with `getActive()` (returns null on 404) and `upsert(request)` (throws normalized `RiskProfileError` on 422/409/5xx). Includes the `toRiskProfileError` helper that maps the `validation.risk_profile.*` code suffix to a form field name. |
| Created | `frontend/src/app/core/api/risk-profile.service.spec.ts` | 6 tests: happy GET, 404→null, 500→error, PUT happy, 422→RiskProfileError with field hint, 409→RiskProfileError, plus 2 toRiskProfileError edge cases. |
| Created | `frontend/src/app/core/state/risk-profile.state.ts` | Signal-based state: `profile`, `isLoading`, `saving`, `error`, `saveSuccess` + computed `isEmpty` and `fieldErrors`. Methods: `load()`, `save()`, `clearError()`, `clearSaveSuccess()`, `reset()`. |
| Created | `frontend/src/app/core/state/risk-profile.state.spec.ts` | 7 tests: load happy, 404→empty, load error, save happy updates profile + flips saveSuccess, save 422 keeps existing profile + fieldErrors, save 409 keeps existing profile, clearError. |
| Created | `frontend/src/app/features/trader/settings/risk-profile/risk-profile-tab.ts` | Angular 19 standalone, Signals, OnPush, inline SCSS. Five render branches: loading, error (non-422), empty, form (with sub-render for 422 inline field errors + 409 conflict copy), and a collapsible `Historial de perfiles` details. Form validators mirror the backend VO ranges (capitalAmount > 0, currency = 3 uppercase letters, maxDrawdownPercent 0–50, riskPerTradePercent 0.01–5, riskRewardTarget ≥ 1). |
| Created | `frontend/src/app/features/trader/settings/risk-profile/__tests__/risk-profile-tab.spec.ts` | 4 component tests: initial load with no profile shows empty state, save success shows the green banner with the saved values, 422 shows field-level error + `jcs-input--error` class, 409 shows the conflict-specific copy. |

## Files Modified

| Action | Path | Purpose |
|--------|------|---------|
| Modified | `frontend/src/app/features/trader/settings/settings.page.ts` | Added `'risk-profile'` to `SettingsTab` union, imported `RiskProfileTab`, added the third tab button, and converted the existing `@if/@else` to a three-way `@if/@else if/@else` chain that lazy-renders `<jcs-risk-profile-tab />` for the new tab. Net change: +23 / -3 lines. |
| Modified | `openspec/changes/2026-08-15-trader-risk-journal-core/tasks.md` | Marked 1a.2 sub-tasks 1.1, 1.2, 2.1, 3.1, 3.2 complete. |

---

## Work Unit Evidence

| Evidence | Value |
|---|---|
| Focused test command | `npx jest --testPathPattern=risk-profile` → 19 tests passed (3 suites, 0 failed) |
| Runtime harness command | `npx ng build --configuration=development` → bundle generation complete (only pre-existing `RouterLinkActive` warnings in `login.page.ts` / `register.page.ts` / `landing-page.ts`, none in this slice's code) |
| Rollback boundary | Revert `bdce743` + `86d1e61` + `0f8e77e` (all three commits on `feature/0a-identity-model`). The RiskProfileTab import + tab in `settings.page.ts` is the only production wiring; the service, state, and component are pure additions. No backend, Docker, or other-feature files touched. |

### 1a.2a — service + state evidence

| Focused test | Command | Result |
|---|---|---|
| Service tests | `npx jest src/app/core/api/risk-profile.service.spec.ts` | 6 tests passed (happy GET, 404→null, 500→error, PUT happy, 422→field hint, 409→error) |
| State tests | `npx jest src/app/core/state/risk-profile.state.spec.ts` | 7 tests passed (load happy, 404→empty, load error, save happy, 422 keeps profile + fieldErrors, 409 keeps profile, clearError) |

### 1a.2b — component + wiring evidence

| Focused test | Command | Result |
|---|---|---|
| Component tests | `npx jest src/app/features/trader/settings/risk-profile/__tests__/risk-profile-tab.spec.ts` | 4 tests passed (initial load empty state, save success, 422 field-level, 409 conflict copy) |
| Build | `npx ng build --configuration=development` | Bundle generation complete, no new errors/warnings in this slice's code |

### Full regression

| Evidence | Command | Result |
|---|---|---|
| Jest full suite | `cd frontend && npx jest` | **17 suites, 64 tests passed, 0 failed** (was 14 suites / 45 tests before; net delta = +3 suites / +19 tests, matching the 13 + 6 from 1a.2a and 4 from 1a.2b) |
| Build | `cd frontend && npx ng build --configuration=development` | Bundle generated successfully; only pre-existing warnings remain (none in this slice) |

---

## Deviations from spec / design

None. The four required test scenarios (initial load, save success, validation error, 409 conflict) are all in place. The state exposes the `fieldErrors` computed for inline rendering, and the component uses it for both the message text and the `jcs-input--error` class on the 422 affected field. The settings page now exposes the third tab and lazy-renders `<jcs-risk-profile-tab />`.

### Implementation notes

- **404 vs other errors**: The service returns `null` only on 404 (per the spec — "exactly one active profile per user, none yet"). All other HTTP errors (401, 403, 5xx) are rethrown as `RiskProfileError` and surfaced through the state.
- **422 field mapping**: The `validation.risk_profile.<field>_<reason>` code suffix is mapped to a form field name via a small lookup table (`VALIDATION_FIELD_HINTS` in `risk-profile.service.ts`). The component subscribes to `state.fieldErrors()` and renders the inline error + the `jcs-input--error` class on the affected input.
- **409 specific copy**: The backend's `conflict.risk_profile.concurrent_supersede` message is forwarded verbatim (e.g. "Otro proceso actualizó tu perfil. Recargá e intentá de nuevo."). The existing profile is preserved in the state so the user can re-try without losing their other fields.
- **Form mirrors backend VO ranges**: `Validators.min(0)` / `Validators.max(50)` for drawdown, `Validators.min(0.01)` / `Validators.max(5)` for risk-per-trade, `Validators.min(1)` for R/R. The 422 test deliberately uses a value within the frontend range (1.5) to prove the backend rejection path, not the frontend validator.
- **"Historial de perfiles"**: Implemented as a collapsed `<details>` element with a small note explaining that the backend keeps superseded versions but the frontend doesn't expose history in this version. This matches the "tooltip or collapsed list" minimum the orchestrator specified.

---

## Notes for next phase

- The 4 new jest test files follow the same patterns as the existing `plan-api.service.spec.ts`, `metrics-api.service.spec.ts`, and `admin-list.page.spec.ts`. The pattern works well with the jest-preset-angular + Angular 19 ESM setup.
- The `RiskProfileState` is `providedIn: 'root'`, so the tab can be reused in other places (e.g. a future pre-trade checklist component or a position-size calculator preview) without re-instantiating.
- The 1a.2 work is complete; the next phase is `sdd-verify` per the dependency contract, or one of the other Wave 1 slices (1b / 1c / 1d / 1e) if the orchestrator wants to proceed in parallel.
