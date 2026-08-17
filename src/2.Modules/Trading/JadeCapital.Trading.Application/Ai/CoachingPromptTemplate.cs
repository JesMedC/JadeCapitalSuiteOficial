using System.Text;
using System.Text.Json;

namespace JadeCapital.Trading.Application.Ai;

// ============================================================================
//  CoachingPromptTemplate — slice 5b.2 (Wave 5, AI Coaching).
//
//  Renders the per-user prompt sent to <see cref="JadeCapital.Shared.Kernel.Ai.IAIProvider"/>.
//  The output is a single string with explicit delimiter sections:
//
//  1. Role definition (who the AI is).
//  2. Tone / output spec ("return ONLY the message text").
//  3. Data block — "--- USER TRADING CONTEXT ---" + JSON-serialized
//     aggregates. The output spec is REPEATED at the bottom so any
//     instruction-like content inside the data block is bracketed.
//  4. Output spec repeated.
//
//  <para>
//  PII exclusion: the context JSON contains ONLY the aggregate fields
//  declared in <see cref="UserTradingContext"/> — no email / displayName /
//  absolute P&L. The provider response is treated as "untrusted text"
//  (the FE renders it verbatim but the trader knows it's AI-generated).
//  </para>
//
//  <para>
//  Prompt-injection defense: the explicit delimiter markers around the
//  data block prevent any instrument name / trade notes from being
//  interpreted as a directive. The output spec appears AFTER the data
//  block, so a malicious string inside the data can't override the spec.
//  </para>
// ============================================================================

public static class CoachingPromptTemplate
{
    /// <summary>Maximum target length for the prompt body. Defensive cap.</summary>
    public const int MaxPromptBodyLength = 4000;

    private const string Delimiter = "--- USER TRADING CONTEXT ---";
    private const string OutputSpec = "Return ONLY the message text — no JSON, no headers, no markdown.";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string Render(UserTradingContext ctx)
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
        sb.AppendLine("You are a trading coach. The user has had this activity in the last "
                      + ctx.WindowDays + " days:");
        sb.AppendLine();
        sb.AppendLine(Delimiter);
        sb.AppendLine(contextJson);
        sb.AppendLine();
        sb.AppendLine("Generate a single coaching message:");
        sb.AppendLine("- Tone: respectful, specific, non-judgmental.");
        sb.AppendLine("- Length: 50-150 words.");
        sb.AppendLine("- Include 1 actionable suggestion tied to the data.");
        sb.AppendLine("- DO NOT include absolute P&L numbers (no '$450' or similar).");
        sb.AppendLine("- " + OutputSpec);
        sb.AppendLine();
        sb.AppendLine(OutputSpec);
        return sb.ToString();
    }
}