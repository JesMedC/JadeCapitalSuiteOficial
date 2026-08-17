// ============================================================================
//  Coaching API types — slice 2d.2 frontend + slice 5b.2 (AI prompts).
//
//  Mirror of:
//   src/2.Modules/Trading/JadeCapital.Trading.Contracts/Coaching/CoachingDtos.cs
//   GET /api/coaching/prompts?period=7d|30d|90d|all        (Wave 3b — rules)
//   GET /api/coaching/ai-prompts?period=7d|30d|90d|all     (slice 5b.2 — AI)
//
//  Severity renders as 'low' (blue), 'medium' (yellow), 'high' (red) per
//  the existing dashboard patterns — kept identical to the patterns page
//  so the same icon/color tokens can be reused.
// ============================================================================

export type Period = '7d' | '30d' | '90d' | 'all';

export type Severity = 'low' | 'medium' | 'high';

export interface CoachingCtaDto {
  route: string;
  label: string;
}

export interface CoachingPromptDto {
  ruleId: string;
  severity: Severity;
  title: string;
  body: string;
  cta: CoachingCtaDto;
  occurredAt: string;
}

export interface CoachingPromptsDto {
  period: Period;
  prompts: CoachingPromptDto[];
}

// ============================================================================
//  AI prompt wire types — slice 5b.2 (Wave 5).
//
//  Mirrors AiCoachingPromptDto from the backend. `kind` is always 'ai' for
//  this endpoint; the rule-vs-ai discriminator lives on
//  `/api/coaching/prompts` (always 'rule' there).
// ============================================================================

export type PromptKind = 'ai';

export interface AiCoachingPromptDto {
  id: string;
  kind: PromptKind;
  severity: Severity;
  model: string;
  latencyMs: number;
  text: string;
  providerResponse: string;
  promptText: string;
  contextJson: string;
  createdAt: string;
  cta: CoachingCtaDto;
}

export interface AiCoachingPromptsDto {
  period: Period;
  prompts: AiCoachingPromptDto[];
}

/** Severity ordering used by the component (high → medium → low). */
export const SEVERITY_ORDER: Record<Severity, number> = {
  high: 0,
  medium: 1,
  low: 2,
};

/** RFC 7807 problem shape returned by the backend on 4xx. */
export interface CoachingProblem {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
}
