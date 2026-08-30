using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Validation;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.Accounts.DeleteAccount;

/// <summary>
/// Borra una cuenta. Pre-check de trades (FK RESTRICT): si la cuenta tiene
/// trades asociados, devuelve Conflict sin tocar la DB. Ownership unificado
/// en NotFound para no leak existencia.
/// </summary>
public sealed class DeleteAccountHandler : IRequestHandler<DeleteAccountCommand, Result<Unit>>
{
    private readonly IAccountRepository _accounts;
    private readonly ITradeRepository _trades;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<DeleteAccountHandler> _logger;

    public DeleteAccountHandler(
        IAccountRepository accounts,
        ITradeRepository trades,
        IUnitOfWork uow,
        ILogger<DeleteAccountHandler> logger)
    {
        _accounts = accounts;
        _trades = trades;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(DeleteAccountCommand req, CancellationToken ct)
    {
        var account = await _accounts.FindByIdAsync(req.AccountId, ct);
        if (account is null || account.UserId != req.UserId)
            return Result.Failure<Unit>(TradingApplicationErrors.Accounts.NotFound);

        // Pre-check: si hay trades que apuntan a esta cuenta, NO permitimos borrar.
        var tradeCount = await _trades.CountByUserIdAsync(
            req.UserId, ct, accountIdFilter: req.AccountId);

        if (tradeCount > 0)
            return Result.Failure<Unit>(TradingApplicationErrors.Accounts.HasTrades);

        await _accounts.DeleteAsync(account, ct);
        var saved = await _uow.SaveChangesAsync(ct);
        DomainGuard.EnsureSuccess(saved);

        _logger.LogInformation("Account {AccountId} deleted by user {UserId}.", account.Id, account.UserId);

        return Result.Success(Unit.Value);
    }
}
