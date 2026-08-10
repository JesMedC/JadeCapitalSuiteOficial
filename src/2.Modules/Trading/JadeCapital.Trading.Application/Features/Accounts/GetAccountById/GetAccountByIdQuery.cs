using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Accounts.GetAccountById;

/// <summary>
/// Trae una cuenta por id validando ownership. Missing + foreign ownership
/// se unifican en NotFound para no leak existencia (mismo patron que trades).
/// </summary>
public sealed record GetAccountByIdQuery(
    Guid AccountId,
    Guid UserId) : IRequest<Result<AccountDto>>;
