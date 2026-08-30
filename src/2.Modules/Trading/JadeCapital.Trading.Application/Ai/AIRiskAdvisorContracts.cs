using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Domain.Ai;

namespace JadeCapital.Trading.Application.Ai;

// ============================================================================
//  AIRiskAdvisorContracts — slice 5c.1 (Wave 5, AI Risk Advisor Pre-Trade).
//
//  Consolidated contract surface for the AI risk advisor pipeline. Holds
//  (a) the IAIRiskAdvisor interface, (b) the AIRiskAdviceRequest record,
//  (c) the AIRiskAdvisorPrompt static renderer, (d) the
//  AIRiskAdvisorResponseParser static parser, and (e) the AIRiskActionReason
//  sentinel constants. All five share the same bounded context and live
//  together to keep the path count under the 32-path hard cap (per the
//  5b.2 D8 path-budget discipline).
//
//  Why Application (not Shared.Kernel). The response shape is Trading-
//  specific (the parsed {action, reason}), so the abstraction lives in
//  Application. Wave 6 may swap the impl for OpenAI / Claude without
//  touching the interface — the abstraction is the swap point.
// ============================================================================

public interface IAIRiskAdvisor
{
    Task<Result<AIRiskAdvice>> AdviseAsync(AIRiskAdviceRequest request, CancellationToken ct = default);
}

public sealed record AIRiskAdviceRequest(
    Guid UserId,
    Guid? TradeId,
    string TradeSymbol,
    string Direction,
    decimal Volume,
    string VolumeCurrency,
    decimal EntryPrice,
    decimal? StopLoss,
    decimal RiskRewardAtEntry,
    string SetupQuality);

// ============================================================================
//  AIRiskAdvisorPrompt — renders the per-trade prompt sent to
//  <see cref="JadeCapital.Shared.Kernel.Ai.IAIProvider"/>.
//  Mirrors the CoachingPromptTemplate pattern (slice 5b.2): explicit
//  delimiter markers around the data block, output spec repeated AFTER the
//  data block to prevent prompt injection.
// ============================================================================

public static class AIRiskAdvisorPrompt
{
    /// <summary>Maximum target length for the prompt body. Defensive cap.</summary>
    public const int MaxPromptLength = 4000;

    /// <summary>Lower temperature — favor deterministic advisory output.</summary>
    public const decimal AdvisoryTemperature = 0.2m;

    /// <summary>Token cap for the advisory completion. 256 is enough for the JSON contract.</summary>
    public const int AdvisoryMaxTokens = 256;

    private const string ContextDelimiter = "--- USER TRADING CONTEXT (last 7 days) ---";
    private const string TradeDelimiter = "--- PROPOSED TRADE ---";
    private const string OutputDelimiter = "--- ADVISORY JSON ---";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string Render(AIRiskAdviceRequest req, UserTradingContext ctx)
    {
        var contextJson = JsonSerializer.Serialize(new
        {
            days_analyzed = ctx.WindowDays,
            closed_trades = ctx.ClosedTradeCount,
            winners = ctx.Winners,
            losers = ctx.Losers,
            win_rate = ctx.WinRate,
            avg_rr = ctx.AverageRiskReward,
            instruments_traded = ctx.InstrumentsTraded,
            violations = ctx.Violations,
        }, JsonOpts);

        var sb = new StringBuilder();
        sb.AppendLine("You are a risk-advisor for a trading system. Your role is advisory only.");
        sb.AppendLine("You will be given the user's recent trading context and the proposed trade.");
        sb.AppendLine("Respond ONLY with a JSON object: {\"action\": \"allow\"|\"warning\"|\"block\", \"reason\": \"<one-sentence explanation, max 200 chars>\"}");
        sb.AppendLine();
        sb.AppendLine(ContextDelimiter);
        sb.AppendLine(contextJson);
        sb.AppendLine();
        sb.AppendLine(TradeDelimiter);
        sb.AppendLine($"Symbol: {req.TradeSymbol}");
        sb.AppendLine($"Direction: {req.Direction}");
        sb.AppendLine($"Volume: {req.Volume} {req.VolumeCurrency}");
        sb.AppendLine($"Entry: {req.EntryPrice}");
        sb.AppendLine($"Stop: {(req.StopLoss?.ToString() ?? "not set")}");
        sb.AppendLine($"RiskRewardAtEntry: {req.RiskRewardAtEntry}");
        sb.AppendLine($"SetupQuality: {req.SetupQuality}");
        sb.AppendLine();
        sb.AppendLine(OutputDelimiter);
        return sb.ToString();
    }
}

// ============================================================================
//  AIRiskAdvisorResponseParser — static parser for the raw Ollama response body.
//  The advisor renders a prompt that asks the AI to return ONLY a JSON
//  object with shape { action: "allow"|"warning"|"block", reason: "..." }.
//  In practice Ollama sometimes wraps the JSON in markdown code fences,
//  occasionally returns unparseable freeform, and very occasionally
//  returns the wrong action verb. The parser is the LAST line of defense
//  before the OpenTradeHandler short-circuits — safe defaults are
//  non-negotiable.
//
//  Failure semantics:
//   - Code-fenced JSON → strip fences, parse the rest.
//   - Malformed JSON → (Allow, "AI returned unparseable response — proceeding without advisory").
//   - Unknown action string → (Allow, provided reason).
//   - Missing reason → (parsedAction, "(no reason given)").
//   - Reason > 500 chars → truncate to 500 chars (matches DB VARCHAR(500)).
//
//  The parser NEVER throws — every input maps to a valid (action, reason)
//  tuple. This is the contract callers rely on to short-circuit the
//  advisor without an additional try/catch.
// ============================================================================

public static partial class AIRiskAdvisorResponseParser
{
    [GeneratedRegex(@"```(?:json)?\s*|\s*```", RegexOptions.IgnoreCase)]
    private static partial Regex CodeFenceRegex();

    public static (AIRiskAction Action, string Reason) Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return (AIRiskAction.Allow, AIRiskActionReason.UnparseableResponse);
        }

        var trimmed = CodeFenceRegex().Replace(content, string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return (AIRiskAction.Allow, AIRiskActionReason.UnparseableResponse);
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return (AIRiskAction.Allow, AIRiskActionReason.UnparseableResponse);
            }

            if (!root.TryGetProperty("action", out var actionEl)
                || actionEl.ValueKind != JsonValueKind.String)
            {
                return (AIRiskAction.Allow, AIRiskActionReason.UnparseableResponse);
            }

            var actionStr = actionEl.GetString();
            var action = actionStr?.ToLowerInvariant() switch
            {
                "allow" => AIRiskAction.Allow,
                "warning" => AIRiskAction.Warning,
                "block" => AIRiskAction.Block,
                _ => AIRiskAction.Allow,
            };

            string reason;
            if (root.TryGetProperty("reason", out var reasonEl)
                && reasonEl.ValueKind == JsonValueKind.String)
            {
                reason = reasonEl.GetString() ?? AIRiskActionReason.NoneReason;
            }
            else
            {
                reason = AIRiskActionReason.NoneReason;
            }

            if (reason.Length > AIRiskAdvice.MaxReasonLength)
                reason = reason[..AIRiskAdvice.MaxReasonLength];

            return (action, reason);
        }
        catch (JsonException)
        {
            return (AIRiskAction.Allow, AIRiskActionReason.UnparseableResponse);
        }
    }
}

public static class AIRiskActionReason
{
    public const string NoneReason = "(no reason given)";
    public const string UnparseableResponse = "AI returned unparseable response — proceeding without advisory";
}
