# Multi-Tenant Specification

## Purpose

Introduce multi-tenant isolation across the entire platform. Every user belongs to exactly one tenant, and every user-owned entity (trades, subscriptions, import jobs, journal entries, etc.) is scoped to a tenant. The tenant is resolved from the JWT `tenant_id` claim via `ITenantContext`. Repository queries that touch tenant-owned entities MUST be filtered by the current tenant. The change also introduces a `Tenant` aggregate (named, slugged, owner-scoped) and tenant admin endpoints (list / invite / remove users).

This spec covers the `Tenant` aggregate, the `tenant_id` column on `identity.users`, the `ITenantContext` + `TenantContextMiddleware`, the repository-level query filter, the auto-backfill script, and the tenant admin endpoints. It does NOT cover per-tenant RBAC (Wave 7), tenant impersonation (Wave 8), or schema-per-tenant isolation (Wave 8+).

## Requirements

### Requirement: Tenant aggregate

The system MUST define `Tenant` as an `Identity.Domain.Tenants` aggregate with `Id, Name, Slug, OwnerUserId, Plan, Status, CreatedAt`. The aggregate MUST enforce invariants: `Name` length 1..120, `Slug` matches `^[a-z0-9-]+$` and length 1..64, `OwnerUserId` is a valid FK to `identity.users`, `Plan` is one of `Personal=0, Pro=1, Enterprise=2`, `Status` is one of `Active=0, Suspended=1, Archived=2`.

#### Scenario: Create valid tenant

- GIVEN a valid name, slug, owner user
- WHEN `Tenant.Create(name, slug, ownerUserId, clock)` is called
- THEN the result MUST be `Result.Success`
- AND the aggregate's `CreatedAt` MUST be set to current UTC time
- AND `Status` MUST default to `Active`

#### Scenario: Invalid slug

- GIVEN a slug with uppercase letters or special characters
- WHEN the factory runs
- THEN the result MUST be `Result.Failure(TenantErrors.InvalidSlug)`

#### Scenario: Slug uniqueness

- GIVEN a tenant with `slug = "personal"` already exists
- WHEN another tenant is created with the same slug
- THEN the database MUST reject the insert with a unique constraint violation
- AND the handler MUST return 409 with `error.code = "tenant.slug_conflict"`

#### Scenario: Status transitions

- GIVEN a tenant in `Status = Active`
- WHEN `Suspend(reason)` is called
- THEN the result MUST be `Result.Success`
- AND the status MUST be `Suspended`

- GIVEN a tenant in `Status = Suspended`
- WHEN `Reactivate()` is called (deferred to Wave 7)
- THEN the result MUST be `Result.Failure(TenantErrors.InvalidStatusTransition)`

### Requirement: Tenant from JWT

The system MUST resolve the current tenant from the JWT `tenant_id` claim via `ITenantContext.Current`. The `TenantContextMiddleware` MUST validate that the claim is present and well-formed for authenticated requests. Anonymous requests MUST proceed (no tenant required for `/api/auth/login`, `/api/billing/plans`, etc).

#### Scenario: Authenticated user with tenant_id

- GIVEN an authenticated user with a JWT containing `tenant_id = "T1"`
- WHEN the user calls any authenticated endpoint
- THEN `ITenantContext.Current` MUST return `TenantId("T1")`
- AND the request MUST proceed

#### Scenario: Authenticated user without tenant_id

- GIVEN an authenticated user with a JWT that does NOT contain `tenant_id` (e.g., pre-Wave-6 token)
- WHEN the user calls any authenticated endpoint
- THEN the middleware MUST return 401 with `error.code = "auth.tenant_missing"`
- AND the user MUST be prompted to re-login

#### Scenario: Malformed tenant_id claim

- GIVEN an authenticated user with a JWT containing `tenant_id = "not-a-uuid"`
- WHEN the user calls any authenticated endpoint
- THEN the middleware MUST return 401 with `error.code = "auth.tenant_invalid"`

### Requirement: tenant_id on user-owned tables

Every user-owned table MUST have a `tenant_id UUID` column (nullable initially, NOT NULL after 6c.3 backfill). The `Tenant_Id` is a FK to `identity.tenants.id`. The migration adds the column in 6c.1 (nullable), the backfill assigns existing users in 6c.2, and 6c.3 sets the NOT NULL constraint.

User-owned tables in scope for Wave 6c:
- `identity.users` (the owner row)
- `billing.subscriptions` (already has `user_id`)
- `billing.stripe_customers` (already has `user_id`)
- `billing.stripe_webhook_events` (no user_id — derives from event payload)
- `trading.trades`
- `trading.accounts`
- `trading.import_jobs`
- `trading.coaching_prompts_ai`
- `trading.ai_risk_advice`
- `trading.pre_trade_checklists`
- `trading.trade_reviews`
- `trading.attachments`
- `trading.journal_entries`
- `trading.strategies`
- `trading.alerts`
- `trading.planner_tasks`
- `trading.scanner_filters`
- `trading.quotations_cache`
- `trading.risk_profiles`

All `tenant_id` columns are added in ONE migration (0025) to keep the change atomic. The migration is idempotent (`ADD COLUMN IF NOT EXISTS`).

#### Scenario: Adding tenant_id to trading.trades

- GIVEN `trading.trades` exists without `tenant_id`
- WHEN migration 0025 runs
- THEN `ALTER TABLE trading.trades ADD COLUMN IF NOT EXISTS tenant_id UUID` MUST be issued
- AND a FK constraint `fk_trades_tenant_id` MUST be added
- AND a partial index `ix_trades_tenant_id` (WHERE tenant_id IS NOT NULL) MUST be created

#### Scenario: Backfill assigns existing users

- GIVEN migration 0026 has run
- AND the `Personal` tenant exists with id `"00000000-0000-0000-0000-000000000001"`
- WHEN `BackfillTenantsRunner.RunAsync` is called
- THEN `UPDATE identity.users SET tenant_id = "00000000-..." WHERE tenant_id IS NULL` MUST be executed
- AND the affected row count MUST be returned

#### Scenario: NOT NULL constraint

- GIVEN all users have `tenant_id IS NOT NULL` (after backfill)
- WHEN migration 0026_NOT_NULL runs
- THEN `ALTER TABLE identity.users ALTER COLUMN tenant_id SET NOT NULL` MUST execute successfully
- AND subsequent inserts MUST include a non-null `tenant_id`

### Requirement: Repository-level query filter

Every `IRepository<T>` whose `T` implements `ITenantOwned` MUST filter queries by `tenant_id = ITenantContext.Current`. The filter is applied at the EF level via `HasQueryFilter` or a wrapper repository. Tests use `IgnoreQueryFilters()` to bypass the filter when needed.

#### Scenario: GetById filters by tenant

- GIVEN user A in tenant T1 and user B in tenant T2
- AND a `Trade` row with `id = "TR1"` owned by user A (tenant T1)
- WHEN user B calls `TradeRepository.GetByIdAsync("TR1")`
- THEN the result MUST be `null` (cross-tenant isolation)
- AND the SQL query MUST include `WHERE tenant_id = @T2`

#### Scenario: List filters by tenant

- GIVEN tenant T1 has 50 trades and tenant T2 has 30 trades
- WHEN user A calls `TradeRepository.ListAsync()`
- THEN the result MUST be 50 trades (only T1's)
- AND user B's call MUST return 30 trades (only T2's)

#### Scenario: Ignore query filters in tests

- GIVEN a test that needs to verify cross-tenant data exists
- WHEN the test calls `.IgnoreQueryFilters().ToList()`
- THEN ALL rows MUST be returned (regardless of tenant)

### Requirement: Tenant owner cannot be changed

The `Tenant.OwnerUserId` is immutable. Transfer ownership is a future operation (Wave 8).

#### Scenario: Attempt to change owner

- GIVEN a tenant with `owner_user_id = U1`
- WHEN `Tenant.ChangeOwner(U2)` is called (NOT implemented in Wave 6)
- THEN the call MUST fail to compile (the method does not exist)

### Requirement: Tenant admin endpoints

The system MUST provide 4 endpoints at `/api/tenants/{id}/...`:
- `GET /api/tenants/{id}/users` — list users in a tenant.
- `POST /api/tenants/{id}/users` — invite a user to a tenant (creates a `TenantUserInvitation` record; email sending is mock in Wave 6).
- `DELETE /api/tenants/{id}/users/{user_id}` — remove a user from a tenant.
- `PATCH /api/tenants/{id}` — update tenant name or plan.

All endpoints require `RequireAuthorization()` and the caller MUST be the tenant owner OR a super-admin.

#### Scenario: List tenant users

- GIVEN a tenant with 5 users
- WHEN the owner calls `GET /api/tenants/{id}/users`
- THEN the endpoint MUST return 200 with an array of 5 user DTOs
- AND the DTOs must include `id`, `email`, `displayName`, `joinedAt`

#### Scenario: Non-owner list attempt

- GIVEN a user who is NOT the tenant owner
- WHEN the user calls `GET /api/tenants/{id}/users` for tenant T1
- THEN the endpoint MUST return 404 with `error.code = "tenant.not_found_or_no_access"`
- AND the response MUST NOT reveal T1's existence

#### Scenario: Invite user

- GIVEN the tenant owner calls `POST /api/tenants/{id}/users` with `{ email: "newuser@example.com" }`
- WHEN the handler runs
- THEN a `TenantUserInvitation` row MUST be created with `tenant_id = T1, email = "newuser@example.com", expires_at = now + 7d`
- AND the response MUST be 202 with `{ invitationId: "..." }`
- AND a mock email MUST be sent (logged, not actually sent)

#### Scenario: Invite existing user

- GIVEN the tenant owner invites an email that already exists in `identity.users`
- WHEN the handler runs
- THEN the existing user MUST be assigned to T1 (`tenant_id = T1`)
- AND the response MUST be 200 with `{ userId: U1 }`

#### Scenario: Remove user

- GIVEN the tenant owner calls `DELETE /api/tenants/{id}/users/{user_id}`
- WHEN the handler runs
- THEN the user's `tenant_id` MUST be set to NULL (or moved to a "Personal" placeholder)
- AND the response MUST be 200

#### Scenario: Owner cannot remove themselves

- GIVEN the tenant owner calls `DELETE /api/tenants/{id}/users/{owner_id}` (where `owner_id == caller's id`)
- WHEN the handler runs
- THEN the endpoint MUST return 422 with `error.code = "tenant.owner_cannot_self_remove"`

#### Scenario: Update tenant

- GIVEN the tenant owner calls `PATCH /api/tenants/{id}` with `{ name: "Acme Corp", plan: "Pro" }`
- WHEN the handler runs
- THEN the tenant's `name` and `plan` MUST be updated
- AND the response MUST be 200 with the updated tenant DTO

#### Scenario: Tenant at capacity

- GIVEN a tenant on `Pro` plan with 10 users (the cap)
- WHEN the owner tries to invite an 11th user
- THEN the endpoint MUST return 422 with `error.code = "tenant.at_capacity"`

### Requirement: Backfill is idempotent

The `BackfillTenantsRunner` MUST be safe to re-run. Creating `Personal` twice MUST fail silently (the `WHERE NOT EXISTS` clause). Re-assigning users to `Personal` MUST be a no-op if all users already have `tenant_id IS NOT NULL`.

#### Scenario: Backfill first run

- GIVEN no `Personal` tenant exists
- AND 247 users have `tenant_id IS NULL`
- WHEN the runner runs
- THEN the `Personal` tenant MUST be created (owner = first user)
- AND 247 users MUST be assigned to `Personal`
- AND the runner returns 247

#### Scenario: Backfill re-run

- GIVEN `Personal` tenant exists
- AND 0 users have `tenant_id IS NULL`
- WHEN the runner runs
- THEN no users MUST be assigned
- AND no new `Personal` tenant MUST be created
- AND the runner returns 0

### Requirement: JWT mint includes tenant_id

The Identity module's access token mint MUST include `tenant_id` as a claim. The refresh token path MUST also include `tenant_id` so re-login preserves tenant. Existing tokens (without `tenant_id`) MUST be rejected by `TenantContextMiddleware` until the user re-logins.

#### Scenario: New user gets tenant_id in JWT

- GIVEN a user signs up and is assigned to tenant T1
- WHEN the access token is minted
- THEN the JWT MUST contain `tenant_id = "T1"`
- AND the token MUST be valid for the user's tenant

#### Scenario: Refresh token carries tenant_id

- GIVEN a user with an active refresh token
- AND the refresh token's underlying user is in tenant T1
- WHEN the user refreshes the access token
- THEN the new access token MUST contain `tenant_id = "T1"`

## Data Model

```
identity.tenants
  id              UUID PK
  name            VARCHAR(120) NOT NULL
  slug            VARCHAR(64) NOT NULL
  owner_user_id   UUID NOT NULL REFERENCES identity.users(id) ON DELETE RESTRICT
  plan            SMALLINT NOT NULL DEFAULT 0  -- 0=Personal, 1=Pro, 2=Enterprise
  status          SMALLINT NOT NULL DEFAULT 0  -- 0=Active, 1=Suspended, 2=Archived
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now()

Constraints:
  ck_tenants_plan    CHECK (plan IN (0, 1, 2))
  ck_tenants_status  CHECK (status IN (0, 1, 2))
  ux_tenants_slug    UNIQUE (slug)

Indexes:
  ix_tenants_owner  (owner_user_id)

identity.users (extended)
  ... existing columns ...
  tenant_id  UUID  -- FK to identity.tenants(id) ON DELETE SET NULL

Constraints (added in 6c.3):
  fk_users_tenant_id  FOREIGN KEY (tenant_id) REFERENCES identity.tenants(id) ON DELETE SET NULL
  nn_users_tenant_id  CHECK (tenant_id IS NOT NULL)  -- added in 6c.3

Indexes:
  ix_users_tenant_id  (tenant_id) WHERE tenant_id IS NOT NULL
```

## Endpoints

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/api/tenants/{id}/users` | Owner or SuperAdmin | Returns tenant users. |
| POST | `/api/tenants/{id}/users` | Owner or SuperAdmin | Invites a user (creates invitation record). |
| DELETE | `/api/tenants/{id}/users/{user_id}` | Owner or SuperAdmin | Removes a user from tenant. |
| PATCH | `/api/tenants/{id}` | Owner or SuperAdmin | Updates tenant name or plan. |

Rate limit: `api-admin` (100 calls/hour/user).

## Output Shapes

```json
// GET /api/tenants/{id}/users
{
  "users": [
    {
      "id": "U1",
      "email": "user1@example.com",
      "displayName": "User One",
      "joinedAt": "2026-08-19T00:00:00Z"
    }
  ]
}

// POST /api/tenants/{id}/users
{
  "invitationId": "INV1",
  "expiresAt": "2026-08-26T00:00:00Z"
}

// PATCH /api/tenants/{id}
{
  "id": "T1",
  "name": "Acme Corp",
  "slug": "acme-corp",
  "plan": "Pro",
  "status": "Active",
  "createdAt": "2026-08-19T00:00:00Z"
}
```

## Architecture

- **Shared.Kernel/MultiTenancy** — `ITenantContext` interface + `TenantId` value object.
- **Identity.Domain/Tenants** — `Tenant` aggregate + `TenantErrors` + `TenantPlan` + `TenantStatus` + `TenantCreatedDomainEvent`.
- **Identity.Domain/Users/User.cs** — extended with `TenantId?` + `AssignToTenant`.
- **Identity.Application/Features/Tenants** — `CreateTenantHandler`, `GetTenantHandler`, `UpdateTenantHandler`, `ListTenantUsersHandler`, `InviteTenantUserHandler`, `RemoveTenantUserHandler`.
- **Identity.Application/Abstractions** — `ITenantRepository` interface.
- **Identity.Infrastructure/MultiTenancy** — `TenantContext`, `TenantContextMiddleware`, `BackfillTenantsHostedService`, `BackfillTenantsRunner`.
- **Identity.Infrastructure/Repositories** — `TenantRepository` (uses EF query filter).
- **Identity.Infrastructure/Persistence/Configurations** — `TenantConfiguration`, `UserConfiguration` (additive `tenant_id`).
- **Identity.Api/Endpoints** — `TenantEndpoints` (4 endpoints).

## Out of Scope

- Per-tenant RBAC (roles per tenant) — Wave 7.
- Schema-per-tenant isolation — Wave 8+.
- Tenant impersonation (login-as) — Wave 8 (requires heavy audit).
- Cross-tenant data sharing (read-only access for Pro tier) — Wave 8.
- Tenant-level billing (each tenant has its own Stripe account) — Wave 7.
- Tenant transfer ownership — Wave 8.
- Tenant deletion (only archiving) — Wave 8+.
- Tenant activity audit (logs of who-did-what) — covered by `soft-delete-audit` spec.
- Tenant theme / branding — Wave 8+.
- Tenant custom domains — Wave 8+.
- Tenant invitations by actual email (mock in Wave 6) — Wave 7.
- Tenant capacity enforcement (Pro = 10, Enterprise = 100) — Wave 7.
- Tenant suspension effects (e.g., users can't log in) — Wave 7.
- Tenant reactivation after suspension — Wave 7.
