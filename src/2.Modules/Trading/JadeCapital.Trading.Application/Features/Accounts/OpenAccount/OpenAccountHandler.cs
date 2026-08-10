using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Shared.Kernel.Validation;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Accounts;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.Accounts.OpenAccount;

/// <summary>
/// Abre una cuenta. La validacion de shape corrio via FluentValidation;
/// las invariantes de negocio (currency valida, leverage positivo, payout
/// en [0,1], name/broker no vacios) las enforce el factory
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

        var openResult = Account.Open(
            accountId,
            req.UserId,
            req.Name,
            req.Broker,
            req.Currency,
            req.InitialBalance,
            req.Leverage,
            req.PayoutPercent,
            _clock);

        if (openResult.IsFailure)
            return Result.Failure<AccountDto>(openResult.Error);

        var account = openResult.Value;

        await _accounts.AddAsync(account, ct);
        var saved = await _uow.SaveChangesAsync(ct);
        DomainGuard.EnsureSuccess(saved);

        _logger.LogInformation("Account {AccountId} opened for user {UserId}.", account.Id, account.UserId);

        return Result.Success(account.ToDto());
    }
}
