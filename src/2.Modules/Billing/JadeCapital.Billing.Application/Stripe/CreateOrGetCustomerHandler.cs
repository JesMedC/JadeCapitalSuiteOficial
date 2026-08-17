using JadeCapital.Billing.Domain.Stripe;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using JadeCapital.Shared.Kernel.Time;
using MediatR;

namespace JadeCapital.Billing.Application.Stripe;

/// <summary>
/// Command: create or fetch the Stripe Customer mapping for the given user
/// (Wave 6, slice 6a.1). Idempotent — if the user already has a mapping,
/// the existing row is returned without a second Stripe call.
/// </summary>
public sealed record CreateOrGetCustomerCommand(
    Guid UserId,
    string Email,
    string? DisplayName) : IRequest<Result<StripeCustomerDto>>;

/// <summary>
/// Handler for <see cref="CreateOrGetCustomerCommand"/>.
///
/// <para>
/// Flow:
/// <list type="number">
///   <item>Look up the existing mapping by user id (idempotency fast-path)</item>
///   <item>If found, return the existing Stripe Customer id</item>
///   <item>If not, call <see cref="IStripeGateway.CreateOrGetCustomerAsync"/></item>
///   <item>On success, persist the new mapping via <see cref="IStripeCustomerRepository"/></item>
/// </list>
/// </para>
/// </summary>
public sealed class CreateOrGetCustomerHandler
    : IRequestHandler<CreateOrGetCustomerCommand, Result<StripeCustomerDto>>
{
    private readonly IStripeCustomerRepository _repo;
    private readonly IStripeGateway _gateway;
    private readonly IClock _clock;

    public CreateOrGetCustomerHandler(
        IStripeCustomerRepository repo,
        IStripeGateway gateway,
        IClock clock)
    {
        _repo = repo;
        _gateway = gateway;
        _clock = clock;
    }

    public async Task<Result<StripeCustomerDto>> Handle(
        CreateOrGetCustomerCommand cmd, CancellationToken ct)
    {
        if (cmd.UserId == Guid.Empty)
            return Result.Failure<StripeCustomerDto>(
                Error.Validation("validation.user_id_required", "UserId is required."));
        if (string.IsNullOrWhiteSpace(cmd.Email))
            return Result.Failure<StripeCustomerDto>(
                Error.Validation("validation.email_required", "Email is required."));

        // 1. Idempotency fast-path.
        var existing = await _repo.GetByUserIdAsync(cmd.UserId, ct);
        if (existing is not null)
        {
            return Result.Success(new StripeCustomerDto(
                StripeCustomerId: existing.StripeCustomerId,
                Email: existing.Email,
                DisplayName: existing.DisplayName,
                CreatedAt: existing.CreatedAt));
        }

        // 2. New mapping — call Stripe.
        var gatewayResult = await _gateway.CreateOrGetCustomerAsync(
            cmd.UserId, cmd.Email, cmd.DisplayName, ct);

        if (gatewayResult.IsFailure)
            return Result.Failure<StripeCustomerDto>(gatewayResult.Error);

        var dto = gatewayResult.Value;

        // 3. Persist (best-effort; failure surfaces to caller).
        var createResult = StripeCustomer.Create(
            Guid.NewGuid(), cmd.UserId, dto.StripeCustomerId, dto.Email, dto.DisplayName, _clock);
        if (createResult.IsFailure)
            return Result.Failure<StripeCustomerDto>(createResult.Error);

        await _repo.AddAsync(createResult.Value, ct);

        return Result.Success(dto);
    }
}
