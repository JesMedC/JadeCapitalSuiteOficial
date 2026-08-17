using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Shared.Kernel.Ai;

/// <summary>
/// AI provider abstraction (Wave 5, slice 5b.1).
///
/// <para>
/// <b>Why Shared.Kernel</b>: mirrors the <c>IQuoteProvider</c> precedent
/// (Wave 4b). The wire shape (<see cref="PromptRequest"/> /
/// <see cref="PromptResponse"/>) is cross-module stable — Trading owns the
/// default impl (<c>OllamaHttpClient</c> in Trading.Infrastructure) but
/// Billing or Identity could later consume the same abstraction.
/// </para>
///
/// <para>
/// <b>Failure semantics</b>: callers MUST NOT need to wrap either method in
/// try/catch for transient provider failures. <see cref="GenerateAsync"/>
/// returns <c>Result.Failure</c> with codes <c>ai.unavailable</c>,
/// <c>ai.timeout</c>, <c>ai.parse_error</c>, <c>ai.empty_response</c>, or
/// <c>ai.internal_error</c> depending on the failure mode. <see cref="IsHealthyAsync"/>
/// swallows transient connection errors and returns <c>false</c>.
/// </para>
///
/// <para>
/// <b>Cancellation</b>: if the caller's <see cref="CancellationToken"/>
/// fires, both methods MUST throw <see cref="OperationCanceledException"/>
/// per the standard .NET cancellation contract — cancellation is never
/// reported as <c>ai.timeout</c> or <c>false</c>.
/// </para>
/// </summary>
public interface IAIProvider
{
    /// <summary>
    /// Runs a single completion against the configured provider and returns
    /// the raw response (text + model + token usage + wall-clock latency).
    /// MUST NOT throw on transient provider failures — failures are returned
    /// as <c>Result&lt;PromptResponse&gt;.Failure(...)</c>. Throws
    /// <see cref="OperationCanceledException"/> if <paramref name="ct"/> fires.
    /// </summary>
    Task<Result<PromptResponse>> GenerateAsync(PromptRequest request, CancellationToken ct = default);

    /// <summary>
    /// Health probe — MUST NOT throw on transient provider failures
    /// (connection refused, timeout, DNS error). Returns <c>true</c> only
    /// when the provider answers with a 2xx within the configured timeout.
    /// Throws <see cref="OperationCanceledException"/> if
    /// <paramref name="ct"/> fires.
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken ct = default);
}
