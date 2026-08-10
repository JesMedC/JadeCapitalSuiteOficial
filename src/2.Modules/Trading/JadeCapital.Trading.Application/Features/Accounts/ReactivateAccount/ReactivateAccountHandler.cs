using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Shared.Kernel.Validation;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.Accounts.ReactivateAccount;

/// <summary>
/// Reactiva una cuenta. Mismo patron de ownership unificado que Deactivate.
/// </summary>
public sealed class ReactivateAccountHandler : IRequestHandler<ReactivateAccountCommand, Result<AccountDto>>
{
    private readonly IAccountRepository _accounts;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<ReactivateAccountHandler> _logger;

    public ReactivateAccountHandler(
        IAccountRepository accounts,
        IUnitOfWork uow,
        IClock clock,
        ILogger<ReactivateAccountHandler> logger)
    {
        _accounts = accounts;
        _uow = uow;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<AccountDto>> Handle(ReactivateAccountCommand req, CancellationToken ct)
    {
        var account = await _accounts.FindByIdAsync(req.AccountId, ct);
        if (account is null || account.UserId != req.UserId)
            return Result.Failure<AccountDto>(TradingApplicationErrors.Accounts.NotFound);

        var reactivateResult = account.Reactivate(_clock);
        if (reactivateResult.IsFailure)
            return Result.Failure<AccountDto>(reactivateResult.Error);

        var saved = await _uow.SaveChangesAsync(ct);
        DomainGuard.EnsureSuccess(saved);

        _logger.LogInformation("Account {AccountId} reactivated.", account.Id);

        return Result.Success(account.ToDto());
    }
}
