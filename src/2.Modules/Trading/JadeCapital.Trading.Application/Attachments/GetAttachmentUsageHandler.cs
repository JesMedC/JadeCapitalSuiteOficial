using JadeCapital.Identity.Contracts.Projections;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Storage;
using JadeCapital.Trading.Application.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Attachments;

/// <summary>
/// Returns the authenticated user's attachment usage snapshot:
/// <c>(totalBytes, attachmentCount, quotaBytes, quotaCount, percentFull)</c>.
/// </summary>
public sealed class GetAttachmentUsageHandler
    : IRequestHandler<GetAttachmentUsageQuery, Result<AttachmentUsageDto>>
{
    private readonly IAttachmentQuotaReader _quotaReader;
    private readonly ITradeAttachmentUsageRepository _usage;
    private readonly ILogger<GetAttachmentUsageHandler> _logger;

    public GetAttachmentUsageHandler(
        IAttachmentQuotaReader quotaReader,
        ITradeAttachmentUsageRepository usage,
        ILogger<GetAttachmentUsageHandler> logger)
    {
        _quotaReader = quotaReader;
        _usage = usage;
        _logger = logger;
    }

    public async Task<Result<AttachmentUsageDto>> Handle(
        GetAttachmentUsageQuery req,
        CancellationToken ct)
    {
        var quotaRow = await _quotaReader.GetQuotaAsync(req.UserId, ct);
        var (usedBytes, count) = await _usage.GetUsageAsync(req.UserId, ct);

        var quotaBytes = quotaRow?.QuotaBytes ?? AttachmentQuota.Default.MaxTotalBytes;
        var quotaCount = AttachmentQuota.Default.MaxAttachmentCount;

        _logger.LogDebug(
            "AttachmentUsage for user {UserId}: {Used}/{Quota} bytes, {Count} attachments.",
            req.UserId, usedBytes, quotaBytes, count);

        return Result.Success(AttachmentUsageDto.From(usedBytes, count, quotaBytes, quotaCount));
    }
}