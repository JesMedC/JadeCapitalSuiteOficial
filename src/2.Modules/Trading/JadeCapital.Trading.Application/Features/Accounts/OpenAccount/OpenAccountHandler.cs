using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Shared.Kernel.Validation;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Accounts;
using JadeCapital.Trading.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.Accounts.OpenAccount;

/// <summary>
/// Abre una cuenta. La validacion de shape corrio via FluentValidation;
/// las invariantes de negocio (currency valida, leverage positivo segun
/// MarketType, market type valido) las enforce el factory
/// <see cref="Account.Open"/>, que es la unica via de creacion.
/// </summary>
public sealed class OpenAccountHandler : IRequestHandler<OpenAccountCommand, Result<AccountDto>>
{
    private readonly IAccountRepository _accounts;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<OpenAccountHandler> _logger;

    public OpenAccountHandler(
        IAccountRepository accounts,
        IUnitOfWork uow,
        IClock clock,
        ILogger<OpenAccountHandler> logger)
    {
        _accounts = accounts;
        _uow = uow;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<AccountDto>> Handle(OpenAccountCommand req, CancellationToken ct)
    {
        var accountId = Guid.NewGuid();

        // Binary no usa leverage, pero el factory espera un valor. Default 1.0
        // (sin apalancamiento) para mantener consistencia si el cliente omite.
        var leverage = req.Leverage;
        if (req.MarketType == MarketType.Binary && (!leverage.HasValue || leverage.Value <= 0m))
            leverage = 1m;

        var openResult = Account.Open(
            accountId,
            req.UserId,
            req.Name,
            req.Broker,
            req.MarketType,
            req.Currency,
            req.InitialBalance,
            leverage,
            _clock);

        if (openResult.IsFailure)
            return Result.Failure<AccountDto>(openResult.Error);

        var account = openResult.Value;

        await _accounts.AddAsync(account, ct);
        var saved = await _uow.SaveChangesAsync(ct);
        DomainGuard.EnsureSuccess(saved);

        _logger.LogInformation("Account {AccountId} opened for user {UserId} as {MarketType}.", account.Id, account.UserId, account.MarketType);

        return Result.Success(account.ToDto());
    }
}
