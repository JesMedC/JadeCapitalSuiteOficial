using JadeCapital.Billing.Domain.Stripe;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using JadeCapital.Shared.Kernel.Time;
using MediatR;

namespace JadeCapital.Billing.Application.Stripe;

/// <summary>
/// Command: create a Stripe Customer Portal session (Wave 6, slice 6a.2).
/// </summary>
public sealed record CreatePortalSessionCommand(
    Guid UserId,
    string ReturnUrl) : IRequest<Result<StripePortalSessionDto>>;

/// <summary>
/// Handler for <see cref="CreatePortalSessionCommand"/>.
///
/// <para>
/// <b>Flow</b>:
/// <list type="number">
///   <item>Validate <c>returnUrl</c>.</item>
///   <item>Look up the local Stripe Customer mapping. If absent, return
///   <c>notfound.stripe.customer_not_found</c> — the user must complete a
///   Checkout flow first to materialise the mapping.</item>
///   <item>Call <see cref="IStripeGateway.CreatePortalSessionAsync"/> and
///   return the DTO.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why not auto-create the customer here</b>: the Customer Portal is
/// meaningless without a Stripe customer record. Auto-creating one would
/// imply a paid subscription. The right UX is "go pick a plan first" — the
/// "Manage in Stripe" button on the billing portal only appears after the
/// user has a subscription.
/// </para>
/// </summary>
public sealed class CreatePortalSessionHandler
    : IRequestHandler<CreatePortalSessionCommand, Result<StripePortalSessionDto>>
{
    private readonly IStripeCustomerRepository _customerRepo;
    private readonly IStripeGateway _gateway;
    private readonly IClock _clock;

    public CreatePortalSessionHandler(
        IStripeCustomerRepository customerRepo,
        IStripeGateway gateway,
        IClock clock)
    {
        _customerRepo = customerRepo;
        _gateway = gateway;
        _clock = clock;
    }

    public async Task<Result<StripePortalSessionDto>> Handle(
        CreatePortalSessionCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.ReturnUrl))
            return Result.Failure<StripePortalSessionDto>(
                Error.Validation("validation.return_url_required",
                    "returnUrl is required."));

        var existing = await _customerRepo.GetByUserIdAsync(cmd.UserId, ct);
        if (existing is null)
            return Result.Failure<StripePortalSessionDto>(
                Error.NotFound("stripe.customer_not_found",
                    "No Stripe customer found for the current user. Complete a Checkout first."));

        var result = await _gateway.CreatePortalSessionAsync(
            cmd.UserId, cmd.ReturnUrl, ct);

        if (result.IsFailure)
            return Result.Failure<StripePortalSessionDto>(result.Error);

        return Result.Success(result.Value);
    }
}
