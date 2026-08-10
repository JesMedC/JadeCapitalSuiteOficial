using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Shared.Kernel.Validation;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.Accounts.DeactivateAccount;

/// <summary>
/// Desactiva una cuenta. Missing + foreign ownership -&gt; NotFound unificado.
/// Already inactive -&gt; Conflict (delegado al aggregate).
/// </summary>
public sealed class DeactivateAccountHandler : IRequestHandler<DeactivateAccountCommand, Result<AccountDto>>
{
    private readonly IAccountRepository _accounts;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<DeactivateAccountHandler> _logger;

    public DeactivateAccountHandler(
        IAccountRepository accounts,
        IUnitOfWork uow,
        IClock clock,
        ILogger<DeactivateAccountHandler> logger)
    {
        _accounts = accounts;
        _uow = uow;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<AccountDto>> Handle(DeactivateAccountCommand req, CancellationToken ct)
    {
        var account = await _accounts.FindByIdAsync(req.AccountId, ct);
        if (account is null || account.UserId != req.UserId)
            return Result.Failure<AccountDto>(TradingApplicationErrors.Accounts.NotFound);

        var deactivateResult = account.Deactivate(_clock);
        if (deactivateResult.IsFailure)
            return Result.Failure<AccountDto>(deactivateResult.Error);

        var saved = await _uow.SaveChangesAsync(ct);
        DomainGuard.EnsureSuccess(saved);

        _logger.LogInformation("Account {AccountId} deactivated.", account.Id);

        return Result.Success(account.ToDto());
    }
}
