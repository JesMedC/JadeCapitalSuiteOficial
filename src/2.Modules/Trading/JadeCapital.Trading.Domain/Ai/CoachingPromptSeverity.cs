namespace JadeCapital.Trading.Domain.Ai;

// ============================================================================
//  CoachingPromptSeverity + CoachingPromptKind — slice 5b.2 (Wave 5).
//
//  Both enums live in a single file because (a) they're tiny (each <30 LOC),
//  (b) they share the same bounded context, and (c) keeping them together
//  cuts the path count for the slice (5b.2 ships at 32 paths to stay within
//  the per-slice budget of `git diff --name-only <= 32`).
//
//  CoachingPromptSeverity — three-level scale for AI-generated coaching
//  prompts. Distinct from the Kernel.Severity enum (Low=1, Medium=2,
//  High=3) because the DB column is SMALLINT and the persistent form uses
//  0..2 (zero-indexed so we can distinguish "no severity" from "low" if
//  a future slice adds a Default bucket). CoachingMapping bridges to the
//  wire shape: 0 → "low", 1 → "medium", 2 → "high".
//
//  CoachingPromptKind — discriminator for the unified /api/coaching/prompts
//  response (Wave 3b rule-based + Wave 5b.2 AI prompts). Stored as SMALLINT
//  in the DB to leave room for future kinds (e.g. Wave 7 PWA "user feedback"
//  kind). Today the AI table only stores Kind = Ai; the Rule kind remains
//  on the Wave 3b table.
// ============================================================================

public enum CoachingPromptSeverity : byte
{
    Low = 0,
    Medium = 1,
    High = 2,
}

public enum CoachingPromptKind : byte
{
    Rule = 0,
    Ai = 1,
}