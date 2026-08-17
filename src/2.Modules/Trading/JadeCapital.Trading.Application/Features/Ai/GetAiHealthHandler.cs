using JadeCapital.Shared.Kernel.Ai;
using JadeCapital.Shared.Kernel.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.Ai.GetAiHealth;

/// <summary>MediatR query — no parameters; the handler probes the configured provider.</summary>
public sealed record GetAiHealthQuery() : IRequest<Result<AiHealthDto>>;

/// <summary>Response payload for <c>GET /api/ai/health</c>.</summary>
/// <param name="Status">Either <c>"ok"</c> (provider answered 2xx) or <c>"down"</c> (any failure).</param>
/// <param name="Model">The configured model name. Null when the provider is down.</param>
public sealed record AiHealthDto(string Status, string? Model);

/// <summary>
/// Returns the current AI provider health (Wave 5, slice 5b.1).
///
/// <para>
/// Wraps <see cref="IAIProvider.IsHealthyAsync"/> so the HTTP endpoint can
/// stay provider-agnostic (Wave 6 swaps the impl, not the endpoint). The
/// handler never throws — <c>IsHealthyAsync</c> already swallows transient
/// errors and returns <c>false</c>.
/// </para>
///
/// <para>
/// The status payload is intentionally minimal: <c>status</c> +
/// <c>model</c>. Detailed metrics (latency, last error) belong on a future
/// <c>/api/ai/stats</c> endpoint — out of scope for slice 5b.1.
/// </para>
/// </summary>
public sealed class GetAiHealthHandler : IRequestHandler<GetAiHealthQuery, Result<AiHealthDto>>
{
    private readonly IAIProvider _provider;
    private readonly AIProviderOptions _options;
    private readonly ILogger<GetAiHealthHandler> _logger;

    public GetAiHealthHandler(
        IAIProvider provider,
        AIProviderOptions options,
        ILogger<GetAiHealthHandler> logger)
    {
        _provider = provider;
        _options = options;
        _logger = logger;
    }

    public async Task<Result<AiHealthDto>> Handle(GetAiHealthQuery request, CancellationToken ct)
    {
        var healthy = await _provider.IsHealthyAsync(ct);
        if (healthy)
        {
            return Result<AiHealthDto>.Success(new AiHealthDto(Status: "ok", Model: _options.Model));
        }

        // Down: log once per call at Information level so an offline Ollama
        // is visible in dev logs without spamming. Don't include the model —
        // we don't know if it's actually the configured one that's broken.
        _logger.LogInformation("AI provider health probe returned down (BaseUrl={BaseUrl}, Model={Model}).",
            _options.BaseUrl, _options.Model);
        return Result<AiHealthDto>.Success(new AiHealthDto(Status: "down", Model: null));
    }
}
