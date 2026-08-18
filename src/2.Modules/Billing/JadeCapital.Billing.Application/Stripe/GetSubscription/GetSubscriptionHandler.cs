using JadeCapital.Billing.Application.Features.Subscriptions;
using JadeCapital.Billing.Contracts.Portal;
using JadeCapital.Billing.Domain.Stripe;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using MediatR;

namespace JadeCapital.Billing.Application.Stripe;

/// <summary>
/// Query: get the caller's own Stripe-backed subscription for the billing
/// portal (Wave 6, slice 6b.1).
///
/// <para>
/// <b>Cross-user isolation</b>: the handler MUST resolve the caller's
/// subscription from the JWT-derived <see cref="UserId"/>, never from any
/// caller-supplied <c>stripeSubscriptionId</c>. The endpoint passes the
/// userId extracted via <c>ClaimsPrincipal</c>.
/// </para>
/// </summary>
public sealed record GetSubscriptionQuery(Guid UserId)
    : IRequest<Result<BillingPortalSubscriptionDto>>;

/// <summary>
/// Handler for <see cref="GetSubscriptionQuery"/>.
///
/// <para>
/// <b>Flow</b>:
/// <list type="number">
///   <item>Resolve the caller's <see cref="StripeCustomer"/> mapping by userId.
///   If absent, return 404 <c>notfound.stripe.customer_not_found</c>.</item>
///   <item>Resolve the caller's local <c>Subscription</c> by userId. If
///   absent (e.g. checkout never completed), return 404
///   <c>notfound.subscription_not_found</c>.</item>
///   <item>Read the local <c>StripeSubscriptionId</c> from the subscription.
///   If the webhook hasn't synced yet (null), return 404
///   <c>notfound.subscription_not_found</c> — there is no Stripe data to read.</item>
///   <item>Call <see cref="IStripeGateway.GetSubscriptionAsync"/>. Map the
///   result onto <see cref="BillingPortalSubscriptionDto"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why no Stripe id from request</b>: the contract guarantees that a user
/// can never read another user's subscription, even by tampering with the
/// request body. The handler ignores any Stripe id the caller might try to
/// supply; the only input is the JWT-derived <see cref="GetSubscriptionQuery.UserId"/>.
/// </para>
/// </summary>
public sealed class GetSubscriptionHandler
    : IRequestHandler<GetSubscriptionQuery, Result<BillingPortalSubscriptionDto>>
{
    private readonly IStripeCustomerRepository _customerRepo;
    private readonly ISubscriptionAdminRepository _subRepo;
    private readonly IStripeGateway _gateway;

    public GetSubscriptionHandler(
        IStripeCustomerRepository customerRepo,
        ISubscriptionAdminRepository subRepo,
        IStripeGateway gateway)
    {
        _customerRepo = customerRepo;
        _subRepo = subRepo;
        _gateway = gateway;
    }

    public async Task<Result<BillingPortalSubscriptionDto>> Handle(
        GetSubscriptionQuery query, CancellationToken ct)
    {
        if (query.UserId == Guid.Empty)
            return Result.Failure<BillingPortalSubscriptionDto>(
                Error.Validation("validation.user_id_required",
                    "Authenticated user id is required."));

        var customer = await _customerRepo.GetByUserIdAsync(query.UserId, ct);
        if (customer is null)
            return Result.Failure<BillingPortalSubscriptionDto>(
                Error.NotFound("stripe.customer_not_found",
                    "No Stripe customer found for the current user. Complete a Checkout first."));

        var subscription = await _subRepo.FindByUserIdAsync(query.UserId, ct);
        if (subscription is null || string.IsNullOrEmpty(subscription.StripeSubscriptionId))
            return Result.Failure<BillingPortalSubscriptionDto>(
                Error.NotFound("subscription_not_found",
                    "No active subscription found for the current user."));

        var gatewayResult = await _gateway.GetSubscriptionAsync(
            subscription.StripeSubscriptionId, ct);

        if (gatewayResult.IsFailure)
            return Result.Failure<BillingPortalSubscriptionDto>(gatewayResult.Error);

        var stripeSub = gatewayResult.Value;
        return Result.Success(new BillingPortalSubscriptionDto(
            StripeSubscriptionId: stripeSub.StripeSubscriptionId,
            Status: stripeSub.Status,
            PlanCode: stripeSub.PlanCode,
            CurrentPeriodEnd: stripeSub.CurrentPeriodEnd,
            CancelAtPeriodEnd: stripeSub.CancelAtPeriodEnd));
    }
}
