using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Contracts.Scanner;
using JadeCapital.Trading.Domain.Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Scanner.DeleteScannerFilter;

public sealed record DeleteScannerFilterCommand(Guid FilterId, Guid UserId) : IRequest<Result<Unit>>;

public sealed class DeleteScannerFilterHandler : IRequestHandler<DeleteScannerFilterCommand, Result<Unit>>
{
    private readonly IScannerFilterRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public DeleteScannerFilterHandler(IScannerFilterRepository repo, IUnitOfWork uow, IClock clock)
    { _repo = repo; _uow = uow; _clock = clock; }

    public async Task<Result<Unit>> Handle(DeleteScannerFilterCommand req, CancellationToken ct)
    {
        var filter = await _repo.GetByIdAsync(req.FilterId, ct);
        if (filter is null || filter.UserId != req.UserId)
            return Result.Failure<Unit>(TradingDomainErrors.Scanner.NotFound);
        filter.Deactivate(_clock);
        await _repo.UpdateAsync(filter, ct);
        var saved = await _uow.SaveChangesAsync(ct);
        return saved.IsFailure ? Result.Failure<Unit>(saved.Error) : Result.Success(Unit.Value);
    }
}
