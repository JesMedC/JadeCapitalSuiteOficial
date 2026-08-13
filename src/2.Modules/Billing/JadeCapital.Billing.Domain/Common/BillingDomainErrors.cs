using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Billing.Domain.Common;

/// <summary>
/// Errores semánticos del bounded context Billing.
/// Convención: código = "billing.{entidad}.{detalle}" para que el DomainGuard
/// pueda enrutarlos correctamente.
/// </summary>
public static class BillingDomainErrors
{
    public static class Plan
    {
        public static readonly Error CodeRequired =
            Error.Validation("plan.code_required", "Plan code is required.");

        public static readonly Error CodeInvalidLength =
            Error.Validation("plan.code_invalid_length", "Plan code length must be 2-32 characters.");

        public static readonly Error NameRequired =
            Error.Validation("plan.name_required", "Plan name is required.");
    }

    public static class Subscription
    {
        public static readonly Error IdRequired =
            Error.Validation("subscription.id_required", "Subscription identifier is required.");

        public static readonly Error UserIdRequired =
            Error.Validation("subscription.user_id_required", "User identifier is required.");

        public static readonly Error PlanRequired =
            Error.Validation("subscription.plan_required", "Plan is required.");

        public static readonly Error VersionConflict =
            Error.Conflict("subscription.version_conflict",
                "Subscription was modified by another caller; reload and retry.");

        public static readonly Error NotCancellable =
            Error.Conflict("subscription.not_cancellable",
                "Subscription is not in a cancellable state.");

        public static readonly Error NotInTrial =
            Error.Conflict("subscription.not_in_trial",
                "Trial extension is only allowed on active trial subscriptions.");

        public static readonly Error TrialEndExpired =
            Error.Validation("subscription.trial_end_expired",
                "Trial end must be in the future.");

        public static readonly Error PlanNotEligible =
            Error.Validation("subscription.plan_not_eligible",
                "Plan is not eligible for self-service subscription.");

        public static readonly Error CancellationReasonRequired =
            Error.Validation("subscription.cancellation_reason_required",
                "Cancellation reason is required.");
    }
}
