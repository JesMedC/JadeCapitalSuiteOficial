using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Validation;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.Accounts.UpdateAccount;

/// <summary>
/// Actualiza metadata de la cuenta. Missing + foreign ownership se unifican
/// en NotFound para no leak existencia (mismo patron que los trades).
/// </summary>
public sealed class UpdateAccountHandler : IRequestHandler<UpdateAccountCommand, Result<AccountDto>>
{
    private readonly IAccountRepository _accounts;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<UpdateAccountHandler> _logger;

    public UpdateAccountHandler(
        IAccountRepository accounts,
        IUnitOfWork uow,
        ILogger<UpdateAccountHandler> logger)
    {
        _accounts = accounts;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result<AccountDto>> Handle(UpdateAccountCommand req, CancellationToken ct)
    {
        var account = await _accounts.FindByIdAsync(req.AccountId, ct);
        if (account is null || account.UserId != req.UserId)
            return Result.Failure<AccountDto>(TradingApplicationErrors.Accounts.NotFound);

        var updateResult = account.UpdateMetadata(
            req.Name,
            req.Broker,
            req.Currency,
            req.Leverage,
            req.PayoutPercent);

        if (updateResult.IsFailure)
            return Result.Failure<AccountDto>(updateResult.Error);

        var saved = await _uow.SaveChangesAsync(ct);
        DomainGuard.EnsureSuccess(saved);

        _logger.LogInformation("Account {AccountId} metadata updated.", account.Id);

        return Result.Success(account.ToDto());
    }
}
