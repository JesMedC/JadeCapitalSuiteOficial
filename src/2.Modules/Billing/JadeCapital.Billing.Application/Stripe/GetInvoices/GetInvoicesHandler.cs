using JadeCapital.Billing.Contracts.Portal;
using JadeCapital.Billing.Domain.Stripe;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using MediatR;

namespace JadeCapital.Billing.Application.Stripe;

/// <summary>
/// Query: list the caller's own Stripe-backed invoices for the billing portal
/// (Wave 6, slice 6b.1).
///
/// <para>
/// <b>Cross-user isolation</b>: the handler MUST resolve the caller's Stripe
/// customer from the JWT-derived <see cref="UserId"/>, never from any
/// caller-supplied <c>stripeCustomerId</c>.
/// </para>
/// </summary>
public sealed record GetInvoicesQuery(Guid UserId)
    : IRequest<Result<IReadOnlyList<BillingPortalInvoiceDto>>>;

/// <summary>
/// Handler for <see cref="GetInvoicesQuery"/>.
///
/// <para>
/// <b>Flow</b>:
/// <list type="number">
///   <item>Resolve the caller's <see cref="StripeCustomer"/> mapping by userId.
///   If absent, return 404 <c>notfound.stripe.customer_not_found</c>.</item>
///   <item>Call <see cref="IStripeGateway.GetInvoicesAsync"/> with the caller's
///   Stripe customer id. Empty lists are returned as empty arrays, NOT 404 —
///   Stripe distinguishes "no invoices" from "no customer".</item>
///   <item>Map the list onto <see cref="BillingPortalInvoiceDto"/>.</item>
/// </list>
/// </para>
/// </summary>
public sealed class GetInvoicesHandler
    : IRequestHandler<GetInvoicesQuery, Result<IReadOnlyList<BillingPortalInvoiceDto>>>
{
    private readonly IStripeCustomerRepository _customerRepo;
    private readonly IStripeGateway _gateway;

    public GetInvoicesHandler(
        IStripeCustomerRepository customerRepo,
        IStripeGateway gateway)
    {
        _customerRepo = customerRepo;
        _gateway = gateway;
    }

    public async Task<Result<IReadOnlyList<BillingPortalInvoiceDto>>> Handle(
        GetInvoicesQuery query, CancellationToken ct)
    {
        if (query.UserId == Guid.Empty)
            return Result.Failure<IReadOnlyList<BillingPortalInvoiceDto>>(
                Error.Validation("validation.user_id_required",
                    "Authenticated user id is required."));

        var customer = await _customerRepo.GetByUserIdAsync(query.UserId, ct);
        if (customer is null)
            return Result.Failure<IReadOnlyList<BillingPortalInvoiceDto>>(
                Error.NotFound("stripe.customer_not_found",
                    "No Stripe customer found for the current user. Complete a Checkout first."));

        var gatewayResult = await _gateway.GetInvoicesAsync(
            customer.StripeCustomerId, ct);

        if (gatewayResult.IsFailure)
            return Result.Failure<IReadOnlyList<BillingPortalInvoiceDto>>(gatewayResult.Error);

        var dtos = gatewayResult.Value
            .Select(inv => new BillingPortalInvoiceDto(
                Id: inv.Id,
                Number: inv.Number,
                AmountCents: inv.AmountCents,
                Currency: inv.Currency,
                IssuedAt: inv.IssuedAt,
                PaidAt: inv.PaidAt,
                Status: inv.Status,
                PdfUrl: inv.PdfUrl))
            .ToList();

        IReadOnlyList<BillingPortalInvoiceDto> readOnly = dtos;
        return Result.Success(readOnly);
    }
}
