using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.TradeAttachments;
using JadeCapital.Trading.Domain.TradeReviews;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Persistence;

/// <summary>
/// Implementacion EF Core de <see cref="ITradeReviewRepository"/>.
/// Singleton del DbContext: comparte el mismo <see cref="TradingDbContext"/>
/// scoped de la UoW.
///
/// Cross-user scope: cada Find/List/Remove recibe userId como parametro
/// explicito y la query filtra WHERE user_id = @userId. Es la aplicacion
/// quien pasa el userId autenticado (no se infiere de la row — seria
/// inseguro si el caller construye una row con un userId ajeno).
/// </summary>
public sealed class TradeReviewRepository : ITradeReviewRepository
{
    private readonly TradingDbContext _db;

    public TradeReviewRepository(TradingDbContext db) { _db = db; }

    // ===== Reviews =====

    public async Task AddAsync(TradeReview review, CancellationToken ct)
        => await _db.TradeReviews.AddAsync(review, ct);

    public Task<TradeReview?> FindByTradeIdAsync(
        Guid tradeId, Guid userId, CancellationToken ct)
        => _db.TradeReviews
            .FirstOrDefaultAsync(r => r.TradeId == tradeId && r.UserId == userId, ct);

    public Task<TradeReview?> FindByIdAsync(
        Guid reviewId, Guid userId, CancellationToken ct)
        => _db.TradeReviews
            .FirstOrDefaultAsync(r => r.Id == reviewId && r.UserId == userId, ct);

    public async Task UpdateAsync(TradeReview review, CancellationToken ct)
    {
        var entry = _db.Entry(review);
        if (entry.State == EntityState.Detached)
        {
            _db.TradeReviews.Update(review);
        }
        await Task.CompletedTask;
    }

    // ===== Attachments =====

    public async Task AddAttachmentAsync(TradeAttachment attachment, CancellationToken ct)
        => await _db.TradeAttachments.AddAsync(attachment, ct);

    public async Task<IReadOnlyList<TradeAttachment>> ListAttachmentsByReviewIdAsync(
        Guid reviewId, Guid userId, CancellationToken ct)
        => await _db.TradeAttachments
            .Where(a => a.ReviewId == reviewId && a.UserId == userId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

    public async Task<int> CountAttachmentsByReviewIdAsync(
        Guid reviewId, CancellationToken ct)
        => await _db.TradeAttachments
            .CountAsync(a => a.ReviewId == reviewId, ct);

    public Task<TradeAttachment?> FindAttachmentByIdAsync(
        Guid attachmentId, Guid userId, CancellationToken ct)
        => _db.TradeAttachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.UserId == userId, ct);

    public async Task UpdateAttachmentAsync(TradeAttachment attachment, CancellationToken ct)
    {
        var entry = _db.Entry(attachment);
        if (entry.State == EntityState.Detached)
        {
            _db.TradeAttachments.Update(attachment);
        }
        await Task.CompletedTask;
    }

    public async Task<string?> RemoveAttachmentAsync(
        Guid attachmentId, Guid userId, CancellationToken ct)
    {
        var row = await _db.TradeAttachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.UserId == userId, ct);
        if (row is null) return null;

        var key = row.ObjectKey;
        _db.TradeAttachments.Remove(row);
        return key;
    }

    public async Task<Guid?> GetTradeIdByAttachmentIdAsync(
        Guid attachmentId, CancellationToken ct)
    {
        // JOIN via review: el attachment -&gt; review -&gt; trade.
        // Una sola query SQL (EF traduce a LEFT JOIN).
        var tradeId = await _db.TradeAttachments
            .Where(a => a.Id == attachmentId)
            .Join(_db.TradeReviews,
                  a => a.ReviewId,
                  r => r.Id,
                  (a, r) => r.TradeId)
            .FirstOrDefaultAsync(ct);

        return tradeId == Guid.Empty ? null : tradeId;
    }
}
