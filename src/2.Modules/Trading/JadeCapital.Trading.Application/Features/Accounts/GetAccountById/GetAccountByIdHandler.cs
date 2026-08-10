using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Accounts.GetAccountById;

public sealed class GetAccountByIdHandler : IRequestHandler<GetAccountByIdQuery, Result<AccountDto>>
{
    private readonly IAccountRepository _accounts;

    public GetAccountByIdHandler(IAccountRepository accounts)
    {
        _accounts = accounts;
    }

    public async Task<Result<AccountDto>> Handle(GetAccountByIdQuery req, CancellationToken ct)
    {
        var account = await _accounts.FindByIdAsync(req.AccountId, ct);
        if (account is null || account.UserId != req.UserId)
            return Result.Failure<AccountDto>(TradingApplicationErrors.Accounts.NotFound);

        return Result.Success(account.ToDto());
    }
}
