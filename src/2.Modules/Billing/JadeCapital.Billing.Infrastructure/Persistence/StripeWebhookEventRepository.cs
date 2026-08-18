using JadeCapital.Billing.Application.Stripe;
using JadeCapital.Billing.Domain.Stripe;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Billing.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IStripeWebhookEventRepository"/>
/// (Wave 6, slice 6a.2). Persists to <c>billing.stripe_webhook_events</c>.
/// The aggregate is append-only after <c>Record</c>; the only mutator
/// flows are <c>MarkProcessed</c> and <c>MarkFailed</c>, which translate
/// into <c>UPDATE</c> statements on the <c>processed_at</c> +
/// <c>processing_error</c> columns.
/// </summary>
public sealed class StripeWebhookEventRepository : IStripeWebhookEventRepository
{
    private readonly BillingDbContext _db;

    public StripeWebhookEventRepository(BillingDbContext db)
    {
        _db = db;
    }

    public Task<StripeWebhookEvent?> FindByEventIdAsync(
        string eventId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            return Task.FromResult<StripeWebhookEvent?>(null);

        return _db.StripeWebhookEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);
    }

    public async Task AddAsync(StripeWebhookEvent webhookEvent, CancellationToken ct = default)
    {
        await _db.StripeWebhookEvents.AddAsync(webhookEvent, ct);
    }

    public Task UpdateAsync(StripeWebhookEvent webhookEvent, CancellationToken ct = default)
    {
        // The aggregate is already tracked by the DbContext (loaded earlier
        // in the same request). EF will detect the changed ProcessedAt /
        // ProcessingError properties on the next SaveChanges.
        _db.StripeWebhookEvents.Update(webhookEvent);
        return Task.CompletedTask;
    }
}