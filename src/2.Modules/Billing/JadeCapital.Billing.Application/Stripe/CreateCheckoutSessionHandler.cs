using JadeCapital.Billing.Domain.Stripe;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using JadeCapital.Shared.Kernel.Time;
using MediatR;

namespace JadeCapital.Billing.Application.Stripe;

/// <summary>
/// Command: create a Stripe Checkout session for a subscription (Wave 6,
/// slice 6a.2).
/// </summary>
public sealed record CreateCheckoutSessionCommand(
    Guid UserId,
    string PriceId,
    string SuccessUrl,
    string CancelUrl,
    string? Email = null,
    string? DisplayName = null) : IRequest<Result<StripeCheckoutSessionDto>>;

/// <summary>
/// Handler for <see cref="CreateCheckoutSessionCommand"/>.
///
/// <para>
/// <b>Flow</b>:
/// <list type="number">
///   <item>Validate inputs (priceId + URLs required).</item>
///   <item>Ensure the user has a Stripe Customer mapping. If not, call
///   <see cref="IStripeGateway.CreateOrGetCustomerAsync"/> to create one
///   (the customer mapping is required for Stripe to bind the checkout
///   to the right user).</item>
///   <item>Call <see cref="IStripeGateway.CreateCheckoutSessionAsync"/> and
///   return the DTO.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why ensure-customer first</b>: the gateway looks up the Stripe
/// Customer by <c>metadata.user_id</c> server-side. If no mapping exists,
/// the call returns <c>stripe.invalid_request</c>. Ensuring the mapping
/// here is simpler and more testable than asking the gateway to do it.
/// </para>
/// </summary>
public sealed class CreateCheckoutSessionHandler
    : IRequestHandler<CreateCheckoutSessionCommand, Result<StripeCheckoutSessionDto>>
{
    private readonly IStripeCustomerRepository _customerRepo;
    private readonly IStripeGateway _gateway;
    private readonly IClock _clock;

    public CreateCheckoutSessionHandler(
        IStripeCustomerRepository customerRepo,
        IStripeGateway gateway,
        IClock clock)
    {
        _customerRepo = customerRepo;
        _gateway = gateway;
        _clock = clock;
    }

    public async Task<Result<StripeCheckoutSessionDto>> Handle(
        CreateCheckoutSessionCommand cmd, CancellationToken ct)
    {
        // 1. Validate inputs.
        if (string.IsNullOrWhiteSpace(cmd.PriceId))
            return Result.Failure<StripeCheckoutSessionDto>(
                Error.Validation("validation.price_id_required",
                    "Stripe Price id is required."));
        if (string.IsNullOrWhiteSpace(cmd.SuccessUrl))
            return Result.Failure<StripeCheckoutSessionDto>(
                Error.Validation("validation.success_url_required",
                    "successUrl is required."));
        if (string.IsNullOrWhiteSpace(cmd.CancelUrl))
            return Result.Failure<StripeCheckoutSessionDto>(
                Error.Validation("validation.cancel_url_required",
                    "cancelUrl is required."));

        // 2. Ensure the user has a Stripe Customer mapping.
        var existing = await _customerRepo.GetByUserIdAsync(cmd.UserId, ct);
        if (existing is null)
        {
            // The handler MUST receive email + displayName to create a Stripe
            // Customer. If absent, the caller didn't extract them from the
            // JWT — return a validation error.
            if (string.IsNullOrWhiteSpace(cmd.Email))
                return Result.Failure<StripeCheckoutSessionDto>(
                    Error.Validation("validation.email_required_for_checkout",
                        "Email is required to create a Stripe customer."));

            var createResult = await _gateway.CreateOrGetCustomerAsync(
                cmd.UserId, cmd.Email, cmd.DisplayName, ct);
            if (createResult.IsFailure)
                return Result.Failure<StripeCheckoutSessionDto>(createResult.Error);

            var dto = createResult.Value;
            var aggregate = StripeCustomer.Create(
                Guid.NewGuid(), cmd.UserId, dto.StripeCustomerId,
                dto.Email, dto.DisplayName, _clock);
            if (aggregate.IsFailure)
                return Result.Failure<StripeCheckoutSessionDto>(aggregate.Error);

            await _customerRepo.AddAsync(aggregate.Value, ct);
        }

        // 3. Create the checkout session.
        var result = await _gateway.CreateCheckoutSessionAsync(
            cmd.UserId, cmd.PriceId, cmd.SuccessUrl, cmd.CancelUrl, ct);

        if (result.IsFailure)
            return Result.Failure<StripeCheckoutSessionDto>(result.Error);

        return Result.Success(result.Value);
    }
}
