using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Attachments;
using MediatR;

namespace JadeCapital.Trading.Application.Attachments;

/// <summary>
/// Read-side query that returns the authenticated user's storage usage.
/// Powers the FE banner (slice 4d.2) and the admin visibility tooling.
///
/// The handler lives in <c>Trading.Application</c> because both the quota
/// contract (Identity.Contracts projection) and the aggregate usage
/// repository (Trading.Application.Abstractions) are resolved here.
/// </summary>
public sealed record GetAttachmentUsageQuery(Guid UserId) : IRequest<Result<AttachmentUsageDto>>;