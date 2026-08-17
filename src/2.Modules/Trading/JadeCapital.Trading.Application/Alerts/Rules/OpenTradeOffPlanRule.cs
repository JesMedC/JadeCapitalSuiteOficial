using JadeCapital.Shared.Kernel.Coaching;
using JadeCapital.Shared.Kernel.Alerts;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Trades;

namespace JadeCapital.Trading.Application.Alerts.Rules;

// ============================================================================
//  OpenTradeOffPlanRule — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Trigger (spec requirement #4):
//   "user has a journal entry today with premarket_plan non-empty
//    AND the user has >= 1 open trade today whose Symbol is NOT mentioned
//    in the journal's tags or premarket_plan text"
//  → emit an OpenTradeOffPlan alert with severity Medium.
//
//  Symbol matching is case-insensitive. We extract candidate symbols
//  from the journal's Tags list (Wave 2 already stores tags as 32-char
//  strings — the user puts "EUR/USD" there) and from any word in the
//  premarket_plan that matches a known symbol pattern.
//
//  PII: body uses qualitative copy + the symbols that diverge from the
//  plan (symbols themselves are NOT PII — they're instrument tickers).
// ============================================================================

public sealed class OpenTradeOffPlanRule : IAlertRule
{
    public string RuleId => "OpenTradeOffPlan";
    public int Priority => 500;

    public IReadOnlyList<Alert> Evaluate(AlertContext ctx)
    {
        // Need at least one open trade today AND one journal entry today
        // with a non-empty plan.
        var openToday = ctx.OpenTrades
            .Where(t => t.Status == TradeStatus.Open)
            .Where(t => IsToday(t.OpenedAt, ctx.WindowEnd))
            .ToList();
        if (openToday.Count == 0)
            return Array.Empty<Alert>();

        var journalToday = ctx.RecentJournals
            .Where(j => IsToday(j.CreatedAt, ctx.WindowEnd))
            .FirstOrDefault();
        if (journalToday is null) return Array.Empty<Alert>();

        var plan = journalToday.PremarketPlan;
        if (string.IsNullOrWhiteSpace(plan)) return Array.Empty<Alert>();

        // Collect symbols the trader mentioned: tags (uppercased) + tokens
        // extracted from the plan text that look like tickers (uppercase
        // letters/digits, optional '/', length >= 3).
        var planSymbols = journalToday.Tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToUpperInvariant())
            .ToHashSet();
        foreach (var token in TokenizeSymbols(plan))
        {
            planSymbols.Add(token);
        }

        if (planSymbols.Count == 0) return Array.Empty<Alert>();

        // Find symbols traded today that don't match the plan.
        var tradedSymbols = openToday
            .Select(t => t.Symbol.Value.ToUpperInvariant())
            .Distinct()
            .ToList();

        var tradedNotInPlan = tradedSymbols
            .Where(s => !planSymbols.Contains(s))
            .ToList();
        if (tradedNotInPlan.Count == 0) return Array.Empty<Alert>();

        var mentioned = string.Join(" y ", planSymbols.OrderBy(s => s).Take(2));
        var traded = string.Join(", ", tradedNotInPlan.OrderBy(s => s).Take(2));
        var body = $"Operaste {traded} hoy, no estaba en tu plan ({mentioned}). ¿Estabas siguiendo tu plan?";

        return new[]
        {
            new Alert(
                RuleId: RuleId,
                Severity: Severity.Medium,
                Title: "Trade fuera del plan",
                Body: body,
                Cta: new Cta("/app/journal", "Ver plan de hoy"),
                ExpiresAt: null),
        };
    }

    private static bool IsToday(DateTimeOffset ts, DateTimeOffset reference)
        => ts.UtcDateTime.Date == reference.UtcDateTime.Date;

    /// <summary>
    /// Crude token extractor: pull any sequence of 3+ uppercase letters or
    /// letters+digits with optional <c>/</c> separator (e.g. EURUSD, BTCUSDT,
    /// EUR/USD). Good enough for Wave 3 — the journal entry already has the
    /// user's tags as canonical symbols.
    /// </summary>
    private static IEnumerable<string> TokenizeSymbols(string text)
    {
        var current = new System.Text.StringBuilder();
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c) || c == '/')
            {
                current.Append(char.ToUpperInvariant(c));
            }
            else
            {
                if (current.Length >= 3)
                    yield return current.ToString();
                current.Clear();
            }
        }
        if (current.Length >= 3)
            yield return current.ToString();
    }
}