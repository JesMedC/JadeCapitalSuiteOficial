# Design — Wave 6 (Stripe Real + Billing Portal + Multi-Tenant + Soft-Delete & Audit)

## Architecture Overview

Wave 6 introduces four orthogonal capabilities that close the commercial + governance loop:

1. **Stripe real** (6a) — `IStripeGateway` interface in `Shared.Kernel.Billing.Stripe`, `StripeGateway` impl in `Billing.Infrastructure` using `Stripe.net 47.0.0` (already declared in `Directory.Build.props` and `Billing.Infrastructure.csproj`). A `StubStripeGateway` returns synthetic responses when `Stripe__ApiKey` env is absent. The gateway handles: Customer creation (idempotent), Checkout session creation, Customer Portal session creation, webhook signature verification, subscription / payment-method / invoice reads. Webhook events are append-logged in `billing.stripe_webhook_events` (idempotent re-delivery).
2. **Billing portal** (6b) — Self-service read endpoints (`GET /api/billing/portal/subscription|payment-methods|invoices`) + Angular 19 standalone Signals OnPush SCSS page. The "Manage in Stripe" button delegates to Stripe Customer Portal via `POST /api/billing/stripe/portal`.
3. **Multi-tenant** (6c) — `Tenant` aggregate (`Identity.Domain/Tenants/Tenant.cs`). `tenant_id` column on every user-owned table (added in ONE migration 0025, nullable, NOT NULL after backfill in 6c.3). `ITenantContext` interface in `Shared.Kernel.MultiTenancy`. `TenantContextMiddleware` resolves tenant from JWT `tenant_id` claim. `Repository<T>` extension filters by `tenant_id`. `BackfillTenantsHostedService` runs once idle-after-startup, idempotent.
4. **Soft-delete + audit** (6d) — `ISoftDelete` interface in `Shared.Kernel.SoftDelete`. EF global query filter on all `ISoftDelete` entities. `AuditEvent` aggregate (append-only, no mutators). `IAuditLogger` interface. `AuditLogger` EF impl. `DecoratedRepository<T>` decorator wraps `IRepository<T>` to log Create/Update/Delete.

The four capabilities share cross-cutting concerns:

- **Tenant is the new namespace** — every existing query that goes through `IRepository<T>` automatically gets the tenant filter; legacy DbContext queries must be migrated to repositories (work tracked but NOT in Wave 6 scope).
- **Audit is fire-and-forget** — the `DecoratedRepository` writes the audit event AFTER the main mutation commits. If the audit write fails, the main mutation is NOT rolled back (the audit log is a defense layer, not a critical path). Tests assert: main mutation committed + audit log contains the row OR audit log is silently dropped with a warning.
- **Webhook + audit work together** — every webhook-driven subscription change writes BOTH a `billing.subscriptions` row update AND an `audit.events` row. The audit row records `actor = "stripe-webhook"` so the chain is traceable.
- **`size:exception` is the default** — Wave 5 precedent (5a.1=2108, 5a.2=1134, 5b.1=1207, 5b.2=2989, 5c.1=3075, 5c.2=971) means every Wave 6 slice will likely use `size:exception`. Per-slice justification is mandatory.

The new domain entities live in `Billing.Domain/Stripe/` (6a) and `Identity.Domain/Tenants/` + `Identity.Domain/Audit/` (6c/6d). The application layer adds handlers in `Billing.Application/Features/Stripe/` + `Identity.Application/Features/Tenants/`. Infrastructure extends `BillingDbContext` + `IdentityDbContext` with new tables, registers `IStripeGateway` + `ITenantContext` + `IAuditLogger`, and adds one `BackgroundService` (the backfill). Host wires four endpoint groups (`/api/billing/stripe`, `/api/billing/portal`, `/api/tenants`); the soft-delete / audit hooks are transparent.

## New Abstractions (Shared.Kernel)

### `IStripeGateway` (Shared.Kernel/Billing/Stripe/IStripeGateway.cs)

```csharp
public interface IStripeGateway
{
    /// <summary>Creates a Stripe Customer for the given user; idempotent — returns existing if user_id already has a StripeCustomer row.</summary>
    Task<Result<StripeCustomerDto>> CreateOrGetCustomerAsync(Guid userId, string email, string? displayName, CancellationToken ct = default);

    /// <summary>Creates a Stripe Checkout session for a subscription. Returns the URL the FE should redirect to.</summary>
    Task<Result<StripeCheckoutSessionDto>> CreateCheckoutSessionAsync(Guid userId, string priceId, string successUrl, string cancelUrl, CancellationToken ct = default);

    /// <summary>Creates a Stripe Customer Portal session for the user. Returns the URL the FE should redirect to.</summary>
    Task<Result<StripePortalSessionDto>> CreatePortalSessionAsync(Guid userId, string returnUrl, CancellationToken ct = default);

    /// <summary>Verifies a Stripe webhook signature and returns the parsed event. Returns 401 on bad signature.</summary>
    Task<Result<StripeWebhookEvent>> VerifyWebhookAsync(string payload, string signatureHeader, CancellationToken ct = default);

    /// <summary>Reads subscription state from Stripe (single source of truth for current state).</summary>
    Task<Result<StripeSubscriptionDto>> GetSubscriptionAsync(string stripeSubscriptionId, CancellationToken ct = default);

    /// <summary>Lists payment methods for a Stripe Customer.</summary>
    Task<Result<IReadOnlyList<StripePaymentMethodDto>>> GetPaymentMethodsAsync(string stripeCustomerId, CancellationToken ct = default);

    /// <summary>Lists invoices for a Stripe Customer (newest first, capped at 100).</summary>
    Task<Result<IReadOnlyList<StripeInvoiceDto>>> GetInvoicesAsync(string stripeCustomerId, CancellationToken ct = default);
}

public sealed record StripeCustomerDto(string StripeCustomerId, string Email, string? DisplayName, DateTimeOffset CreatedAt);
public sealed record StripeCheckoutSessionDto(string SessionId, string Url, DateTimeOffset ExpiresAt);
public sealed record StripePortalSessionDto(string SessionId, string Url, DateTimeOffset ExpiresAt);
public sealed record StripeSubscriptionDto(string StripeSubscriptionId, string Status, string PlanCode, DateTimeOffset CurrentPeriodEnd, bool CancelAtPeriodEnd);
public sealed record StripePaymentMethodDto(string Id, string Brand, string Last4, DateTimeOffset? ExpiresAt, bool IsDefault);
public sealed record StripeInvoiceDto(string Id, string Number, long AmountCents, string Currency, DateTimeOffset IssuedAt, DateTimeOffset? PaidAt, string Status, string PdfUrl);
public sealed record StripeWebhookEvent(string EventId, string Type, string PayloadJson, DateTimeOffset OccurredAt);
```

**Why Shared.Kernel**: mirrors `IQuoteProvider` (Wave 4b), `IAIProvider` (Wave 5b), `IImportRowParser` (Wave 5a) precedent. The wire shapes are cross-module stable. The `StripeGateway` impl in `Billing.Infrastructure` is the only Stripe SDK consumer; the rest of the system never references `Stripe` namespace directly.

### `ITenantContext` (Shared.Kernel/MultiTenancy/ITenantContext.cs)

```csharp
public interface ITenantContext
{
    /// <summary>Current tenant from JWT. Returns null for anonymous callers.</summary>
    TenantId? Current { get; }

    /// <summary>Current user from JWT. Returns null for anonymous callers.</summary>
    Guid? CurrentUserId { get; }

    /// <summary>True if the current user is a super-admin (cross-tenant access). Used sparingly for admin endpoints.</summary>
    bool IsSuperAdmin { get; }
}

public sealed record TenantId(Guid Value)
{
    public static TenantId Empty => new(Guid.Empty);
    public static TenantId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
```

**Why Shared.Kernel**: every module reads tenant context. Same pattern as `IClock` (Wave 0) which is also in `Shared.Kernel/Time/`.

### `ISoftDelete` (Shared.Kernel/SoftDelete/ISoftDelete.cs)

```csharp
public interface ISoftDelete
{
    bool IsDeleted { get; }
    DateTimeOffset? DeletedAtUtc { get; }
    Guid? DeletedByUserId { get; }
}
```

**EF global query filter convention**: every entity implementing `ISoftDelete` declares `b.HasQueryFilter(e => !e.IsDeleted)` in its `IEntityTypeConfiguration`. The filter is omittable via `IgnoreQueryFilters()` in test fixtures that need to see soft-deleted rows.

### `IAuditLogger` (Shared.Kernel/Audit/IAuditLogger.cs)

```csharp
public interface IAuditLogger
{
    /// <summary>Records an audit event. Fire-and-forget — failures are logged but do NOT throw.</summary>
    Task LogAsync(AuditEventEntry entry, CancellationToken ct = default);
}

public sealed record AuditEventEntry(
    string EntityType,
    Guid EntityId,
    AuditAction Action,
    Guid? TenantId,
    Guid? UserId,
    string? ChangesJson,
    DateTimeOffset OccurredAt);

public enum AuditAction : byte
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    Restored = 3,
}
```

**Why Shared.Kernel**: auditing is cross-cutting. The `AuditEvent` aggregate lives in `Identity.Domain/Audit/`, but the `IAuditLogger` interface is in `Shared.Kernel` so any module can log without importing Identity.

## New Aggregates

### `StripeCustomer` (Billing.Domain/Stripe/StripeCustomer.cs)

```csharp
public class StripeCustomer : AggregateRoot<Guid>
{
    public Guid UserId { get; }
    public string StripeCustomerId { get; }
    public string Email { get; }
    public string? DisplayName { get; private set; }
    public DateTimeOffset CreatedAt { get; }

    public static Result<StripeCustomer> Create(
        Guid userId, string stripeCustomerId, string email, string? displayName, IClock clock);
}
```

**Invariant**: `(user_id, stripe_customer_id)` is unique. One Stripe Customer per user (idempotent).

### `StripeWebhookEvent` (Billing.Domain/Stripe/StripeWebhookEvent.cs)

```csharp
public class StripeWebhookEvent : AggregateRoot<Guid>
{
    public string EventId { get; }       // Stripe `evt_...` id — unique
    public string EventType { get; }
    public string PayloadJson { get; }
    public string? SignatureHeader { get; }
    public DateTimeOffset ReceivedAt { get; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? ProcessingError { get; private set; }

    public static Result<StripeWebhookEvent> Record(
        string eventId, string eventType, string payloadJson, string? signatureHeader, IClock clock);

    public Result MarkProcessed(IClock clock);
    public Result MarkFailed(string error, IClock clock);
}
```

**Invariant**: `event_id` is unique. Re-delivery of the same `event_id` returns the existing row (no insert). Append-only after `Record`.

### `SubscriptionWebhookSync` (Billing.Domain/Stripe/SubscriptionWebhookSync.cs)

```csharp
/// <summary>
/// Read-model projection (not aggregate) that maps a Stripe webhook event
/// (customer.subscription.created|updated|deleted) to a Subscription mutator call.
/// 
/// Does NOT own persistence — it's a translator. Lives in Domain because
/// the mapping rule (status code → SubscriptionStatus) is a domain invariant.
/// </summary>
public static class SubscriptionWebhookSync
{
    public static Result ApplyToSubscription(
        Subscription subscription, StripeSubscriptionDto stripeSub, IClock clock);
}
```

### `Tenant` (Identity.Domain/Tenants/Tenant.cs)

```csharp
public class Tenant : AggregateRoot<Guid>
{
    public string Name { get; private set; }
    public string Slug { get; }                       // URL-safe, unique
    public Guid OwnerUserId { get; }
    public TenantPlan Plan { get; private set; }      // Personal=0, Pro=1, Enterprise=2
    public TenantStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; }

    public static Result<Tenant> Create(
        string name, string slug, Guid ownerUserId, IClock clock);

    public Result Rename(string newName, IClock clock);
    public Result ChangePlan(TenantPlan newPlan, IClock clock);
    public Result Suspend(string reason, IClock clock);
    public Result Archive(IClock clock);
}

public enum TenantStatus : byte { Active = 0, Suspended = 1, Archived = 2 }
public enum TenantPlan : byte { Personal = 0, Pro = 1, Enterprise = 2 }
```

**Invariants**:
- `Slug` is unique across `identity.tenants`.
- `Status` transitions: `Active → Suspended → Archived`. No back-transitions.
- `OwnerUserId` cannot be changed (transfer ownership is a future operation).

### `AuditEvent` (Identity.Domain/Audit/AuditEvent.cs)

```csharp
public class AuditEvent : AggregateRoot<Guid>
{
    public string EntityType { get; }
    public Guid EntityId { get; }
    public AuditAction Action { get; }
    public Guid? TenantId { get; }
    public Guid? UserId { get; }
    public string? ChangesJson { get; }    // JSONB: { "field": { "before": ..., "after": ... } }
    public DateTimeOffset OccurredAt { get; }

    /// <summary>No mutators — append-only. Once persisted, immutable.</summary>
    public static Result<AuditEvent> Create(AuditEventEntry entry, IClock clock);
}
```

**Invariant**: zero mutators. EF is configured to use `ValueGeneratedNever()` + no UPDATE/DELETE policy in the DbContext (configuration step).

## Modified Aggregates

### `User` (Identity.Domain/Users/User.cs) — additive `tenant_id`

```csharp
public class User : AggregateRoot<Guid>
{
    // ... existing Wave 0 fields unchanged ...

    /// <summary>Wave 6c: tenant membership. NULL pre-Wave-6; NOT NULL after backfill (6c.3).</summary>
    public TenantId? TenantId { get; private set; }

    public Result AssignToTenant(TenantId tenantId, IClock clock);
}
```

The `AssignToTenant` call is idempotent — re-assigning to the same tenant is a no-op. Cross-tenant re-assignment requires admin authorization.

### `Subscription` (Billing.Domain/Subscriptions/Subscription.cs) — webhook handler

```csharp
public class Subscription : AggregateRoot<Guid>
{
    // ... existing Wave 0 fields unchanged ...

    /// <summary>Wave 6a.2: Stripe subscription id — populated when webhook syncs.</summary>
    public string? StripeSubscriptionId { get; private set; }

    public Result SyncFromStripe(StripeSubscriptionDto stripeSub, IClock clock);
}
```

The `SyncFromStripe` preserves the existing optimistic-concurrency guard: if webhook sees `version = N` and tries to mutate, the call must pass `observedVersion = N-1` (from the prior sync). If the row was updated by an admin in between, the webhook retries up to 3 times with jitter.

### `ImportJob` (Trading.Domain/Imports/ImportJob.cs) — `ISoftDelete` + `tenant_id`

```csharp
public class ImportJob : AggregateRoot<Guid>, ISoftDelete, ITenantOwned
{
    // ... existing Wave 5a fields unchanged ...
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAtUtc { get; private set; }
    public Guid? DeletedByUserId { get; private set; }
    public TenantId? TenantId { get; private set; }

    public Result MarkDeleted(Guid userId, IClock clock);
}
```

`ITenantOwned` is a new marker interface for entities that need tenant filtering. The `Repository<T>` extension checks `if (entity is ITenantOwned) { query = query.Where(e => e.TenantId == tenantContext.Current); }`.

## EF Configuration

### `StripeCustomerConfiguration` (NEW, Billing.Infrastructure)

```csharp
internal sealed class StripeCustomerConfiguration : IEntityTypeConfiguration<StripeCustomer>
{
    public void Configure(EntityTypeBuilder<StripeCustomer> b)
    {
        b.ToTable("stripe_customers");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).HasColumnName("id");
        b.Property(c => c.UserId).HasColumnName("user_id").IsRequired();
        b.Property(c => c.StripeCustomerId).HasColumnName("stripe_customer_id").HasMaxLength(64).IsRequired();
        b.Property(c => c.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        b.Property(c => c.DisplayName).HasColumnName("display_name").HasMaxLength(120);
        b.Property(c => c.CreatedAt).HasColumnName("created_at");

        b.Ignore(c => c.DomainEvents);

        b.HasIndex(c => c.UserId).IsUnique().HasDatabaseName("ux_stripe_customers_user");
        b.HasIndex(c => c.StripeCustomerId).IsUnique().HasDatabaseName("ux_stripe_customers_stripe_id");
    }
}
```

### `StripeWebhookEventConfiguration` (NEW, Billing.Infrastructure)

```csharp
internal sealed class StripeWebhookEventConfiguration : IEntityTypeConfiguration<StripeWebhookEvent>
{
    public void Configure(EntityTypeBuilder<StripeWebhookEvent> b)
    {
        b.ToTable("stripe_webhook_events");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.EventId).HasColumnName("event_id").HasMaxLength(64).IsRequired();
        b.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(64).IsRequired();
        b.Property(e => e.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.SignatureHeader).HasColumnName("signature_header").HasMaxLength(256);
        b.Property(e => e.ReceivedAt).HasColumnName("received_at");
        b.Property(e => e.ProcessedAt).HasColumnName("processed_at");
        b.Property(e => e.ProcessingError).HasColumnName("processing_error").HasMaxLength(2000);

        b.Ignore(e => e.DomainEvents);

        b.HasIndex(e => e.EventId).IsUnique().HasDatabaseName("ux_stripe_webhook_events_event_id");
        b.HasIndex(e => e.EventType).HasDatabaseName("ix_stripe_webhook_events_type");
    }
}
```

### `TenantConfiguration` (NEW, Identity.Infrastructure)

```csharp
internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("tenants");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).HasColumnName("id");
        b.Property(t => t.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        b.Property(t => t.Slug).HasColumnName("slug").HasMaxLength(64).IsRequired();
        b.Property(t => t.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        b.Property(t => t.Plan).HasColumnName("plan").HasConversion<byte>();
        b.Property(t => t.Status).HasColumnName("status").HasConversion<byte>();
        b.Property(t => t.CreatedAt).HasColumnName("created_at");

        b.Ignore(t => t.DomainEvents);

        b.HasIndex(t => t.Slug).IsUnique().HasDatabaseName("ux_tenants_slug");
        b.HasIndex(t => t.OwnerUserId).HasDatabaseName("ix_tenants_owner");
    }
}
```

### `UserConfiguration` (extended, Identity.Infrastructure)

```csharp
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        // ... existing Wave 0 mappings ...

        // Wave 6c: tenant_id
        b.Property(u => u.TenantId).HasColumnName("tenant_id").HasConversion(g => g!.Value, g => TenantId.From(g));
        b.HasIndex(u => u.TenantId).HasDatabaseName("ix_users_tenant_id");
    }
}
```

### `AuditEventConfiguration` (NEW, Identity.Infrastructure)

```csharp
internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> b)
    {
        b.ToTable("events");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.EntityType).HasColumnName("entity_type").HasMaxLength(80).IsRequired();
        b.Property(e => e.EntityId).HasColumnName("entity_id").IsRequired();
        b.Property(e => e.Action).HasColumnName("action").HasConversion<byte>();
        b.Property(e => e.TenantId).HasColumnName("tenant_id");
        b.Property(e => e.UserId).HasColumnName("user_id");
        b.Property(e => e.ChangesJson).HasColumnName("changes").HasColumnType("jsonb");
        b.Property(e => e.OccurredAt).HasColumnName("occurred_at");

        b.Ignore(e => e.DomainEvents);

        b.HasIndex(e => new { e.EntityType, e.EntityId }).HasDatabaseName("ix_audit_events_entity");
        b.HasIndex(e => new { e.TenantId, e.OccurredAt }).HasDatabaseName("ix_audit_events_tenant_time");
        b.HasIndex(e => e.UserId).HasDatabaseName("ix_audit_events_user");
    }
}
```

### `ImportJobConfiguration` (extended, Trading.Infrastructure)

```csharp
internal sealed class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> b)
    {
        // ... existing Wave 5a mappings ...

        // Wave 6c.2: tenant_id filter
        b.Property(j => j.TenantId).HasColumnName("tenant_id").HasConversion(g => g!.Value, g => TenantId.From(g));

        // Wave 6d.1: soft-delete filter
        b.Property(j => j.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        b.Property(j => j.DeletedAtUtc).HasColumnName("deleted_at");
        b.Property(j => j.DeletedByUserId).HasColumnName("deleted_by_user_id");
        b.HasQueryFilter(j => !j.IsDeleted);
    }
}
```

## Stripe Implementation Detail

### `StripeGateway` (Billing.Infrastructure/Stripe/StripeGateway.cs)

```csharp
public sealed class StripeGateway : IStripeGateway
{
    private readonly StripeClient _client;
    private readonly StripeOptions _options;
    private readonly ILogger<StripeGateway> _logger;

    public StripeGateway(IOptions<StripeOptions> options, ILogger<StripeGateway> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new StripeClient(_options.ApiKey);
        StripeConfiguration.ApiVersion = _options.ApiVersion;   // pinned per user choice
    }

    public async Task<Result<StripeCustomerDto>> CreateOrGetCustomerAsync(Guid userId, string email, string? displayName, CancellationToken ct = default)
    {
        try
        {
            var service = new CustomerService(_client);
            var customers = await service.ListAsync(new CustomerListOptions { Email = email, Limit = 1 }, ct);
            if (customers.Data.Count > 0)
            {
                var existing = customers.Data[0];
                return Result.Success(new StripeCustomerDto(existing.Id, existing.Email, existing.Name, existing.Created));
            }

            var created = await service.CreateAsync(new CustomerCreateOptions
            {
                Email = email,
                Name = displayName,
                Metadata = new Dictionary<string, string> { ["user_id"] = userId.ToString() }
            }, ct);
            return Result.Success(new StripeCustomerDto(created.Id, created.Email, created.Name, created.Created));
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe customer create failed for user {UserId}", userId);
            return Result.Failure<StripeCustomerDto>(MapStripeError(ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe SDK unexpected error for user {UserId}", userId);
            return Result.Failure<StripeCustomerDto>(new StripeError("stripe.internal_error", ex.Message));
        }
    }

    // ... other methods follow same pattern: try/catch wraps each Stripe SDK call.
    // 5s linked-CTS timeout on every call (matches OllamaHttpClient 5c.1 precedent).
}
```

**Stub fallback** (dev / CI without Stripe key):

```csharp
public sealed class StubStripeGateway : IStripeGateway
{
    public Task<Result<StripeCustomerDto>> CreateOrGetCustomerAsync(Guid userId, string email, string? displayName, CancellationToken ct = default)
    {
        var stubId = $"cus_stub_{userId:N}";
        return Task.FromResult(Result.Success(new StripeCustomerDto(stubId, email, displayName, DateTimeOffset.UtcNow)));
    }

    // ... other methods return synthetic responses with predictable shapes.
}
```

**DI registration**:

```csharp
services.AddSingleton<IStripeGateway>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<StripeOptions>>().Value;
    return string.IsNullOrWhiteSpace(opts.ApiKey)
        ? new StubStripeGateway()
        : ActivatorUtilities.CreateInstance<StripeGateway>(sp);
});
```

### `HandleWebhookHandler` (Billing.Application/Features/Stripe/HandleWebhook/HandleWebhookHandler.cs)

```csharp
internal sealed class HandleWebhookHandler : IRequestHandler<HandleWebhookCommand, Result<WebhookOutcomeDto>>
{
    private readonly IStripeGateway _stripe;
    private readonly IStripeWebhookEventRepository _events;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;

    public async Task<Result<WebhookOutcomeDto>> Handle(HandleWebhookCommand cmd, CancellationToken ct)
    {
        // 1. Verify signature
        var verifyResult = await _stripe.VerifyWebhookAsync(cmd.PayloadJson, cmd.SignatureHeader, ct);
        if (verifyResult.IsFailure)
            return Result.Failure<WebhookOutcomeDto>(verifyResult.Error);   // 401

        var stripeEvent = verifyResult.Value;

        // 2. Idempotency: check if event_id already processed
        var existing = await _events.FindByEventIdAsync(stripeEvent.EventId, ct);
        if (existing is not null && existing.ProcessedAt is not null)
            return Result.Success(new WebhookOutcomeDto("duplicate", existing.Id));

        // 3. Append-log the event
        var loggedStripeEvent = existing ?? StripeWebhookEvent.Record(
            stripeEvent.EventId, stripeEvent.Type, stripeEvent.PayloadJson, cmd.SignatureHeader, _clock);
        if (existing is null)
            await _events.AddAsync(loggedStripeEvent, ct);

        // 4. Dispatch on event type
        try
        {
            switch (stripeEvent.Type)
            {
                case "customer.subscription.created":
                case "customer.subscription.updated":
                    await SyncSubscriptionAsync(stripeEvent, ct);
                    break;
                case "customer.subscription.deleted":
                    await CancelSubscriptionAsync(stripeEvent, ct);
                    break;
                default:
                    // Unknown event type — log only, return 200 (Stripe will not retry)
                    break;
            }

            loggedStripeEvent.MarkProcessed(_clock);
            await _events.UpdateAsync(loggedStripeEvent, ct);
            return Result.Success(new WebhookOutcomeDto("processed", loggedStripeEvent.Id));
        }
        catch (Exception ex)
        {
            loggedStripeEvent.MarkFailed(ex.Message, _clock);
            await _events.UpdateAsync(loggedStripeEvent, ct);
            return Result.Failure<WebhookOutcomeDto>(new StripeError("stripe.handler_failed", ex.Message));
        }
    }

    private async Task SyncSubscriptionAsync(StripeWebhookEvent stripeEvent, CancellationToken ct)
    {
        var stripeSub = JsonSerializer.Deserialize<StripeSubscriptionDto>(stripeEvent.PayloadJson);
        var subscription = await _subscriptions.FindByStripeSubscriptionIdAsync(stripeSub.StripeSubscriptionId, ct);
        if (subscription is null)
        {
            // Subscription not yet created locally — webhook arrived before /api/billing/stripe/customers
            // Mitigation: log + skip. Admin must re-process via out-of-band job (Wave 7).
            return;
        }

        var before = snapshot(subscription);
        var result = subscription.SyncFromStripe(stripeSub, _clock);
        if (result.IsFailure)
            throw new InvalidOperationException($"Subscription sync failed: {result.Error.Message}");
        await _subscriptions.UpdateAsync(subscription, ct);

        // Audit
        await _audit.LogAsync(new AuditEventEntry(
            EntityType: "billing.subscriptions",
            EntityId: subscription.Id,
            Action: AuditAction.Updated,
            TenantId: subscription.TenantId?.Value,
            UserId: null,  // webhook actor
            ChangesJson: diff(before, subscription),
            OccurredAt: _clock.UtcNow), ct);
    }
}
```

**Signature verify raw body contract**: ASP.NET Core reads the body for form-urlencoded but NOT for JSON. The endpoint must call `Request.EnableBuffering()` and `await Request.Body.CopyToAsync(memoryStream)` BEFORE model binding. The endpoint signature is `HandleWebhookCommand(string PayloadJson, string SignatureHeader)` — JSON binding would corrupt the payload. Mitigation: bind manually:

```csharp
app.MapPost("/api/billing/stripe/webhooks", async (HttpRequest req, ISender sender, CancellationToken ct) =>
{
    req.EnableBuffering();
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms, ct);
    var payload = Encoding.UTF8.GetString(ms.ToArray());
    var signature = req.Headers["Stripe-Signature"].ToString();
    var result = await sender.Send(new HandleWebhookCommand(payload, signature), ct);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Unauthorized();
});
```

## Multi-Tenant Implementation Detail

### `TenantContext` (Identity.Infrastructure/MultiTenancy/TenantContext.cs)

```csharp
public sealed class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContext;

    public TenantContext(IHttpContextAccessor httpContext)
    {
        _httpContext = httpContext;
    }

    public TenantId? Current => _httpContext.HttpContext?.User?.FindFirst("tenant_id")?.Value is { } v
        && Guid.TryParse(v, out var g) && g != Guid.Empty
        ? new TenantId(g)
        : null;

    public Guid? CurrentUserId => _httpContext.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value is { } v
        && Guid.TryParse(v, out var g)
        ? g
        : null;

    public bool IsSuperAdmin => _httpContext.HttpContext?.User?.IsInRole("SuperAdmin") ?? false;
}
```

### `TenantContextMiddleware` (Identity.Infrastructure/MultiTenancy/TenantContextMiddleware.cs)

```csharp
public sealed class TenantContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantContextMiddleware> _logger;

    public TenantContextMiddleware(RequestDelegate next, ILogger<TenantContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenant)
    {
        if (context.User?.Identity?.IsAuthenticated == true && tenant.Current is null)
        {
            // Authenticated user without tenant_id — could be a pre-Wave-6 token.
            // Log + force logout (return 401 with token-expired message).
            _logger.LogWarning("Authenticated user {UserId} has no tenant_id claim", tenant.CurrentUserId);
            await context.Response.WriteAsJsonAsync(new { code = "auth.tenant_missing" }, statusCode: 401);
            return;
        }
        await _next(context);
    }
}
```

### `TenantRepository<T>` extension (Identity.Infrastructure/Repositories/TenantRepository.cs)

```csharp
public interface ITenantRepository<T> : IRepository<T> where T : class, ITenantOwned
{
    // No new methods — the filter is applied at query time.
}

public sealed class TenantRepository<T> : ITenantRepository<T> where T : class, ITenantOwned
{
    private readonly DbContext _db;
    private readonly ITenantContext _tenant;

    public TenantRepository(DbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await _db.Set<T>()
            .Where(e => !((ISoftDelete)e).IsDeleted)        // soft-delete filter
            .Where(e => e.TenantId == _tenant.Current)      // tenant filter
            .FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    // ... other methods follow same pattern.
}
```

### `BackfillTenantsHostedService` (Identity.Infrastructure/MultiTenancy/BackfillTenantsHostedService.cs)

```csharp
public sealed class BackfillTenantsHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _factory;
    private readonly ILogger<BackfillTenantsHostedService> _logger;
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(15);   // idle after startup

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(Delay, stoppingToken);
        try
        {
            await using var scope = _factory.CreateAsyncScope();
            var runner = scope.ServiceProvider.GetRequiredService<IBackfillTenantsRunner>();
            var affected = await runner.RunAsync(stoppingToken);
            _logger.LogInformation("Backfill tenants complete: {Affected} users assigned to Personal tenant", affected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backfill tenants failed — will retry on next startup");
        }
    }
}

public sealed class BackfillTenantsRunner : IBackfillTenantsRunner
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        // 1. Find or create "Personal" tenant (idempotent by Slug = 'personal')
        var personal = await _tenants.FindBySlugAsync("personal", ct);
        if (personal is null)
        {
            var ownerResult = await _someUserProvider.GetAnyActiveUserIdAsync(ct);
            if (ownerResult.IsFailure) return 0;   // no users to backfill
            var createResult = Tenant.Create("Personal", "personal", ownerResult.Value, _clock);
            if (createResult.IsFailure) return 0;
            await _tenants.AddAsync(createResult.Value, ct);
            personal = createResult.Value;
        }

        // 2. UPDATE users SET tenant_id = @personal WHERE tenant_id IS NULL
        var affected = await _users.AssignNullTenantsToAsync(personal.Id, ct);
        return affected;
    }
}
```

## Audit Implementation Detail

### `AuditLogger` (Identity.Infrastructure/Audit/AuditLogger.cs)

```csharp
public sealed class AuditLogger : IAuditLogger
{
    private readonly AuditDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ILogger<AuditLogger> _logger;

    public async Task LogAsync(AuditEventEntry entry, CancellationToken ct = default)
    {
        try
        {
            var enriched = entry with
            {
                TenantId = entry.TenantId ?? _tenant.Current?.Value,
                UserId = entry.UserId ?? _tenant.CurrentUserId
            };
            var auditEvent = AuditEvent.Create(enriched, _clock);
            await _db.AuditEvents.AddAsync(auditEvent, ct);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit log write failed for {EntityType}/{EntityId}. Main mutation was committed.", entry.EntityType, entry.EntityId);
            // Fire-and-forget — never throw.
        }
    }
}
```

### `DecoratedRepository<T>` (Identity.Infrastructure/Persistence/DecoratedRepository.cs)

```csharp
public sealed class DecoratedRepository<T> : IRepository<T> where T : class
{
    private readonly IRepository<T> _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public async Task AddAsync(T entity, CancellationToken ct)
    {
        var result = await _inner.AddAsync(entity, ct);
        await _audit.LogAsync(new AuditEventEntry(
            EntityType: typeof(T).Name,
            EntityId: GetId(entity),
            Action: AuditAction.Created,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: null,    // no diff for create
            OccurredAt: _clock.UtcNow), ct);
        return result;
    }

    public async Task UpdateAsync(T entity, CancellationToken ct)
    {
        var before = await _inner.GetByIdAsync(GetId(entity), ct);  // snapshot
        var result = await _inner.UpdateAsync(entity, ct);
        await _audit.LogAsync(new AuditEventEntry(
            EntityType: typeof(T).Name,
            EntityId: GetId(entity),
            Action: AuditAction.Updated,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: Diff(before, entity),
            OccurredAt: _clock.UtcNow), ct);
        return result;
    }

    public async Task DeleteAsync(T entity, CancellationToken ct)
    {
        var result = await _inner.DeleteAsync(entity, ct);
        await _audit.LogAsync(new AuditEventEntry(
            EntityType: typeof(T).Name,
            EntityId: GetId(entity),
            Action: AuditAction.Deleted,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: null,
            OccurredAt: _clock.UtcNow), ct);
        return result;
    }
}
```

**Decoration chain**: `OuterRepository -> DecoratedRepository -> TenantRepository -> DbContext`. Each layer adds a concern: audit, tenant, raw EF. Each is independently testable.

**DI registration**:

```csharp
services.AddScoped<IRepository<Tenant>, TenantRepository>();
services.Decorate<IRepository<Tenant>, TenantDecorator>();   // adds tenant filter
services.Decorate<IRepository<Tenant>, DecoratedRepository<Tenant>>();   // adds audit
```

(Note: `Scrutor` `Decorate` extension is used. If not already in `Directory.Build.props`, add it in 6d.2.)

## Soft-Delete Implementation Detail

### `ISoftDelete` + EF global query filter

Each entity implementing `ISoftDelete` declares:

```csharp
public class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> b)
    {
        // ... mappings ...

        // Wave 6d.1: soft-delete filter
        b.HasQueryFilter(j => !j.IsDeleted);
    }
}
```

### `SoftDeleteHandler` (Identity.Infrastructure/SoftDelete/SoftDeleteHandler.cs)

```csharp
public sealed class SoftDeleteHandler : IRequestHandler<SoftDeleteCommand, Result>
{
    private readonly IRepository<T> _repo;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;

    public async Task<Result> Handle(SoftDeleteCommand cmd, CancellationToken ct)
    {
        var entity = await _repo.GetByIdAsync(cmd.Id, ct);
        if (entity is null)
            return Result.Failure(new NotFoundError($"{typeof(T).Name}.{cmd.Id}"));

        if (entity is ISoftDelete softDelete)
        {
            var markResult = ((dynamic)entity).MarkDeleted(cmd.UserId, _clock);
            if (markResult.IsFailure)
                return Result.Failure(markResult.Error);
            await _repo.UpdateAsync(entity, ct);
            // Audit is triggered by the DecoratedRepository decorator.
            return Result.Success();
        }
        return Result.Failure(new ValidationError("entity not soft-deletable"));
    }
}
```

## DI Composition

### `BillingModuleRegistration` (NEW additions)

```csharp
// Wave 6a — Stripe
services.AddSingleton<IStripeGateway>(sp =>
    string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<StripeOptions>>().Value.ApiKey)
        ? new StubStripeGateway()
        : ActivatorUtilities.CreateInstance<StripeGateway>(sp));
services.Configure<StripeOptions>(builder.Configuration.GetSection("Stripe"));
services.AddScoped<CreateOrGetCustomerHandler>();
services.AddScoped<CreateCheckoutSessionHandler>();
services.AddScoped<CreatePortalSessionHandler>();
services.AddScoped<HandleWebhookHandler>();
services.AddScoped<GetSubscriptionHandler>();
services.AddScoped<GetPaymentMethodsHandler>();
services.AddScoped<GetInvoicesHandler>();
services.AddScoped<IStripeCustomerRepository, StripeCustomerRepository>();
services.AddScoped<IStripeWebhookEventRepository, StripeWebhookEventRepository>();
```

### `IdentityModuleRegistration` (NEW additions)

```csharp
// Wave 6c — Tenant
services.AddScoped<ITenantContext, TenantContext>();
services.AddHttpContextAccessor();   // already present
services.AddScoped<CreateTenantHandler>();
services.AddScoped<UpdateTenantHandler>();
services.AddScoped<ListTenantUsersHandler>();
services.AddScoped<InviteTenantUserHandler>();
services.AddScoped<RemoveTenantUserHandler>();
services.AddScoped<GetTenantHandler>();
services.AddScoped<ITenantRepository, TenantRepository>();
services.AddHostedService<BackfillTenantsHostedService>();

// Wave 6d — Audit
services.AddScoped<IAuditLogger, AuditLogger>();
services.Decorate<IRepository<Tenant>, DecoratedRepository<Tenant>>();

// Wave 6d — Soft-delete
services.AddScoped(typeof(SoftDeleteHandler<>));
```

### `Program.cs` (NEW additions)

```csharp
// Stripe endpoints
app.MapBillingStripeEndpoints();        // 4 endpoints: customers, checkout, portal, webhooks
app.MapBillingPortalEndpoints();        // 3 endpoints: subscription, payment-methods, invoices

// Tenant endpoints
app.MapTenantEndpoints();               // 4 endpoints: list, invite, remove, update

// Middleware order: TenantContextMiddleware AFTER AuthenticationMiddleware
app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();   // 401 if missing tenant_id
app.UseAuthorization();
```

## Frontend

### New files

**Billing portal** (`frontend/src/app/features/trader/billing/`):
- `billing-portal-page.ts` — standalone Signals OnPush SCSS with: plan card (current plan + status + next billing date), payment methods list (brand + last 4 + expiry), invoices list (number + amount + paid status + pdf link), "Manage in Stripe" button.
- `api/billing-portal.service.ts` — 3 HTTP wrappers (`getSubscription`, `getPaymentMethods`, `getInvoices`).
- `api/billing-portal.types.ts` — DTOs.
- `state/billing-portal.state.ts` — Signals (`subscription`, `paymentMethods`, `invoices`, `loading`, `error`).
- `billing.routes.ts` — sub-route.
- `__tests__/billing-portal-page.spec.ts` — 6 jest specs.

### Modified

- `frontend/src/app/features/trader/trader.routes.ts` — add `billing` lazy route.
- `frontend/src/app/features/trader/trader-shell.ts` — add `Billing` nav entry (12 items total).
- `frontend/src/app/features/trader/__tests__/trader-shell.spec.ts` — add 12-item + Billing row assertion.

## Migration Sequencing

9 separate migration files (one per slice that ships independently):

```sql
-- 0022_stripe_customers.sql (Wave 6a.1)
BEGIN;
CREATE TABLE IF NOT EXISTS billing.stripe_customers (
    id                    UUID PRIMARY KEY,
    user_id               UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE,
    stripe_customer_id    VARCHAR(64) NOT NULL,
    email                 VARCHAR(320) NOT NULL,
    display_name          VARCHAR(120),
    created_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ck_stripe_customers_email CHECK (email LIKE '%_@_%.__%'),
    CONSTRAINT ux_stripe_customers_user UNIQUE (user_id),
    CONSTRAINT ux_stripe_customers_stripe_id UNIQUE (stripe_customer_id)
);
COMMENT ON TABLE billing.stripe_customers IS 'Stripe Customer mapping (Wave 6a.1). One row per user; idempotent create.';
COMMIT;

-- 0023_stripe_webhook_events.sql (Wave 6a.2)
BEGIN;
CREATE TABLE IF NOT EXISTS billing.stripe_webhook_events (
    id                  UUID PRIMARY KEY,
    event_id            VARCHAR(64) NOT NULL,
    event_type          VARCHAR(64) NOT NULL,
    payload_json        JSONB NOT NULL,
    signature_header    VARCHAR(256),
    received_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    processed_at        TIMESTAMPTZ,
    processing_error    VARCHAR(2000),
    CONSTRAINT ux_stripe_webhook_events_event_id UNIQUE (event_id)
);
CREATE INDEX IF NOT EXISTS ix_stripe_webhook_events_type ON billing.stripe_webhook_events (event_type);
COMMENT ON TABLE billing.stripe_webhook_events IS 'Stripe webhook event log (Wave 6a.2). Append-only; re-delivery is no-op.';
COMMIT;

-- 0024_tenants.sql (Wave 6c.1)
BEGIN;
CREATE TABLE IF NOT EXISTS identity.tenants (
    id              UUID PRIMARY KEY,
    name            VARCHAR(120) NOT NULL,
    slug            VARCHAR(64) NOT NULL,
    owner_user_id   UUID NOT NULL REFERENCES identity.users(id) ON DELETE RESTRICT,
    plan            SMALLINT NOT NULL DEFAULT 0,
    status          SMALLINT NOT NULL DEFAULT 0,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ck_tenants_plan CHECK (plan IN (0, 1, 2)),
    CONSTRAINT ck_tenants_status CHECK (status IN (0, 1, 2)),
    CONSTRAINT ux_tenants_slug UNIQUE (slug)
);
CREATE INDEX IF NOT EXISTS ix_tenants_owner ON identity.tenants (owner_user_id);
COMMENT ON TABLE identity.tenants IS 'Tenant aggregate (Wave 6c.1). One row per tenant; users belong to exactly one tenant.';
COMMIT;

-- 0025_users_tenant_id.sql (Wave 6c.1) — additive, nullable
BEGIN;
ALTER TABLE identity.users ADD COLUMN IF NOT EXISTS tenant_id UUID;
ALTER TABLE identity.users
    ADD CONSTRAINT fk_users_tenant_id FOREIGN KEY (tenant_id) REFERENCES identity.tenants(id) ON DELETE SET NULL;
CREATE INDEX IF NOT EXISTS ix_users_tenant_id ON identity.users (tenant_id) WHERE tenant_id IS NOT NULL;
COMMENT ON COLUMN identity.users.tenant_id IS 'Tenant membership (Wave 6c). NULL pre-Wave-6; NOT NULL after 6c.3 backfill.';
COMMIT;

-- 0026_backfill_personal_tenant.sql (Wave 6c.2) — idempotent, safe to re-run
BEGIN;
-- 1. Insert Personal tenant if absent (owner = first user, or a sentinel UUID if no users)
INSERT INTO identity.tenants (id, name, slug, owner_user_id, plan, status, created_at)
SELECT
    '00000000-0000-0000-0000-000000000001'::UUID,
    'Personal',
    'personal',
    COALESCE((SELECT id FROM identity.users ORDER BY created_at LIMIT 1), '00000000-0000-0000-0000-000000000000'::UUID),
    0, 0, now()
WHERE NOT EXISTS (SELECT 1 FROM identity.tenants WHERE slug = 'personal');

-- 2. Assign all users with NULL tenant_id to Personal
UPDATE identity.users
SET tenant_id = '00000000-0000-0000-0000-000000000001'::UUID
WHERE tenant_id IS NULL;
COMMIT;

-- 0027_audit_events.sql (Wave 6d.1)
BEGIN;
CREATE TABLE IF NOT EXISTS audit.events (
    id              UUID PRIMARY KEY,
    entity_type     VARCHAR(80) NOT NULL,
    entity_id       UUID NOT NULL,
    action          SMALLINT NOT NULL,
    tenant_id       UUID,
    user_id         UUID,
    changes         JSONB,
    occurred_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ck_audit_events_action CHECK (action IN (0, 1, 2, 3))
);
CREATE INDEX IF NOT EXISTS ix_audit_events_entity ON audit.events (entity_type, entity_id);
CREATE INDEX IF NOT EXISTS ix_audit_events_tenant_time ON audit.events (tenant_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_audit_events_user ON audit.events (user_id);
COMMENT ON TABLE audit.events IS 'Audit event log (Wave 6d). Append-only; EF is configured to prevent UPDATE/DELETE.';
COMMIT;
```

All 6 migrations are idempotent (`IF NOT EXISTS` / `ADD COLUMN IF NOT EXISTS` / `WHERE NOT EXISTS` + idempotent re-run safe). Wire en `migrate.Dockerfile` happy + retry path with `\\\"` escape.

## Sequence Diagrams

### Stripe Checkout flow

```
Trader              FE              POST /api/billing/stripe/checkout    CreateCheckoutSessionHandler       IStripeGateway (Stripe or Stub)
  |                |                       |                                       |                                              |
  |--click upgrade->|                      |                                       |                                              |
  |                |--POST {priceId}------>|                                       |                                              |
  |                |                       |--CreateCheckoutSessionAsync-------->|                                              |
  |                |                       |                                       |--CheckoutService.CreateAsync---------------->|
  |                |                       |                                       |<--Session URL--|
  |                |                       |                                       |--Persist checkout session link --------------->|
  |                |<--303 redirect-{url}--|                                       |                                              |
  |--navigate to Stripe----------------->   |                                       |                                              |
  |                |--complete payment--->|                                       |                                              |
  |                |                       |                                       |                                              |
  |                |  ... async ...                                                                                              |
  |                |                       |                                       |                                              |
  |                |                       |    Stripe webhook POST /api/billing/stripe/webhooks                                                |
  |                |                       |                                       |--VerifyWebhookAsync------------------->|
  |                |                       |                                       |<--Event {customer.subscription.created}--|
  |                |                       |                                       |--FindByEventIdAsync (idempotent check)-->|
  |                |                       |                                       |--Record(StripeWebhookEvent)---------->|
  |                |                       |                                       |--SyncSubscriptionAsync------------------>|
  |                |                       |                                       |--subscription.SyncFromStripe()------>|
  |                |                       |                                       |--IAuditLogger.LogAsync------------------>|
  |                |                       |<--200 OK--|                           |                                              |
```

### Webhook signature verify flow

```
Stripe                       POST /api/billing/stripe/webhooks   Billing Stripe Endpoints               HandleWebhookHandler            IStripeGateway
  |                                   |                                       |                                       |                                              |
  |--POST {raw JSON, Stripe-Signature>|                                       |                                       |                                              |
  |                                   |--Request.EnableBuffering()           |                                       |                                              |
  |                                   |--Body.CopyToAsync(memoryStream)     |                                       |                                              |
  |                                   |--Send(HandleWebhookCommand)-------->|                                       |                                              |
  |                                   |                                       |--VerifyWebhookAsync(payload, sig)---->|                                              |
  |                                   |                                       |                                       |--EventUtility.ConstructEvent------>|
  |                                   |                                       |                                       |<--Stripe.Event {id, type, data}--|
  |                                   |                                       |<--Event--|                           |                                              |
  |                                   |                                       |--Continue dispatch...                |                                              |
  |                                   |<--200 OK ----------------------------|                                       |                                              |
```

### Tenant backfill flow

```
Startup                  BackfillTenantsHostedService           BackfillTenantsRunner                    IdentityDbContext
  |                              |                                          |                                          |
  |---(after 15s idle)---------->|                                          |                                          |
  |                              |--RunAsync()----------------------------->|                                          |
  |                              |                                          |--FindBySlugAsync("personal")--------->|
  |                              |                                          |<--null (first run)------------------|
  |                              |                                          |--FindAnyActiveUserIdAsync()------->|
  |                              |                                          |<--someUserId---------------------|
  |                              |                                          |--Tenant.Create("Personal", "personal", someUserId, _clock)|
  |                              |                                          |--INSERT INTO identity.tenants----->|
  |                              |                                          |--UPDATE users SET tenant_id=@personal WHERE NULL--->|
  |                              |                                          |<--affected = 247-----------|
  |                              |<--affected---------------|                                          |
```

### Audit log write flow

```
Application Layer                IRepository<T>                       DecoratedRepository<T>         IAuditLogger (AuditLogger)         AuditDbContext
       |                                |                                       |                                   |                                |
       |--AddAsync(entity)------------->|                                       |                                   |                                |
       |                                |--inner.AddAsync(entity)------------->|                                   |                                |
       |                                |<--ok-------------------------------|                                   |                                |
       |                                |--audit.LogAsync(Created entry)---->|                                   |                                |
       |                                |                                       |--db.AuditEvents.AddAsync-------->|                                |
       |                                |                                       |--db.SaveChangesAsync------------->|                                |
       |                                |<--result--|                         |                                   |                                |
       |<--result--|                   |                                       |                                   |                                |
```

## Cross-Module Concerns

### JWT `tenant_id` claim (6c.2)

The Identity module's `_mintAccessToken` must be updated to include `tenant_id`:

```csharp
private string MintAccessToken(User user, TenantId tenantId)
{
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim("tenant_id", tenantId.Value.ToString()),
        new Claim(ClaimTypes.Role, user.Role.ToString()),
        // ... existing claims
    };
    // ... JWT signing logic unchanged
}
```

Existing users without a tenant_id claim will be rejected by `TenantContextMiddleware` after deploy. The refresh path inserts `tenant_id` (resolves user → tenant → claim) on every refresh.

### Wave 5 customers table migration (6c.1)

`billing.subscriptions` has `user_id` but no `tenant_id`. Wave 6c.1 adds it. The webhook handler (6a.2) resolves tenant_id from user_id via a single JOIN query per sync. The subscription row is updated to include `tenant_id`.

### Existing repositories (6c.2)

All Wave 0-5 repositories that own user-scoped entities MUST be migrated to `Repository<T>` extension or `TenantRepository<T>` over the next 2 waves. Wave 6c.2 wires the abstract `ITenantContext` + `TenantRepository<T>` extension + middleware, but does NOT migrate every existing repo (too large). 6c.3 migrates the most-loaded ones (Tenant, Subscription, ImportJob). The rest comes in Wave 7.

### Audit decorator on legacy repos (6d.2)

Same pattern: Wave 6d.2 wires the `DecoratedRepository<T>` decorator + registers it on `TenantRepository`, `SubscriptionRepository`, `ImportJobRepository`. The rest comes in Wave 7.

## Per-Slice Path Budget

| Slice | Files created | Files modified | Total paths | Bounded review ≤ 32 |
|---|---:|---:|---:|:---:|
| 6a.1 | 11 | 4 | 15 | OK |
| 6a.2 | 9 | 5 | 14 | OK |
| 6b.1 | 7 | 3 | 10 | OK |
| 6b.2 | 6 | 3 | 9 | OK |
| 6c.1 | 12 | 5 | 17 | OK |
| 6c.2 | 8 | 6 | 14 | OK |
| 6c.3 | 7 | 4 | 11 | OK |
| 6d.1 | 9 | 4 | 13 | OK |
| 6d.2 | 8 | 5 | 13 | OK |
| **Total** | **77** | **39** | **116** | All ≤ 32 paths OK |

Each slice is well under 32 paths. The largest is 6c.1 at 17 paths. Wave 5 precedent (5c.1 = 31 paths) shows 32 paths is comfortable even for cross-cutting slices.

## Total Wave 6

~5,900 net LOC, 116 file paths, 9 PRs chained. Each ≤ 32 paths.
