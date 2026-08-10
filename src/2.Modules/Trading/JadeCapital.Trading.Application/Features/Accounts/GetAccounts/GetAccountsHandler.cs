using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Accounts.GetAccounts;

/// <summary>
/// Lista las cuentas del usuario autenticado y las proyecta a DTO.
/// Lista vacia -&gt; success con IReadOnlyList vacio (NO error).
/// </summary>
public sealed class GetAccountsHandler : IRequestHandler<GetAccountsQuery, Result<IReadOnlyList<AccountDto>>>
{
    private readonly IAccountRepository _accounts;

    public GetAccountsHandler(IAccountRepository accounts)
    {
        _accounts = accounts;
    }

    public async Task<Result<IReadOnlyList<AccountDto>>> Handle(GetAccountsQuery req, CancellationToken ct)
    {
        var items = await _accounts.ListByUserIdAsync(req.UserId, ct);
        return Result.Success<IReadOnlyList<AccountDto>>(items.Select(a => a.ToDto()).ToList());
    }
}
