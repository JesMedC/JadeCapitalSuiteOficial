using JadeCapital.Trading.Domain.Accounts;
using JadeCapital.Trading.Domain.Enums;

namespace JadeCapital.Trading.Application._Common;

/// <summary>
/// Extension centralizada para mapear aggregate Account -&gt; AccountDto.
/// Vive en Application porque es contrato de capa: handlers, queries y
/// (futuros) projections de EF lo consumen.
/// </summary>
internal static class AccountMapping
{
    public static AccountDto ToDto(this Account account)
        => new(
            account.Id,
            account.UserId,
            account.Name,
            account.Broker,
            account.MarketType,
            account.Currency,
            account.InitialBalance,
            account.Leverage,
            account.IsActive,
            account.CreatedAt,
            account.UpdatedAt ?? account.CreatedAt);
}
