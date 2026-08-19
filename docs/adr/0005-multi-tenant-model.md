# 5. Multi-tenant model

## Status
Accepted (2026-08-19, Wave 10)

## Context
JadeCapital needs multi-tenant isolation so multiple SaaS customers share one deployment without data leaks.

## Decision
- Each user-owned row has a `tenant_id` column (NOT NULL post-Wave 6 migration 0026_NOT_NULL_tenant_id.sql)
- JWT contains `tenant_id` claim set during Register / Login
- `TenantContext` resolves tenant_id from HttpContext + propagates via Scoped DI
- EF Core global query filter automatically appends `WHERE tenant_id = @current`
- `ITenantRepository` mediates tenant CRUD
- `BackfillTenantsHostedService` (Wave 6 6c.2) migrated pre-Wave-6 NULL users to a "Personal" tenant

## Consequences
- Pros: hard isolation per tenant, query-level enforcement
- Cons: every new aggregate must include `tenant_id` (Wave 7/8/9 decorators all enforce this)
- Migrations required: 0026_tenants + 0027_users_tenant_id + 0028_NOT_NULL_tenant_id
