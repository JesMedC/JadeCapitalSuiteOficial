// ============================================================================
//  Coaching API types — slice 2d.2 frontend.
//
//  Mirror of:
//   src/2.Modules/Trading/JadeCapital.Trading.Contracts/Coaching/CoachingDtos.cs
//   GET /api/coaching/prompts?period=7d|30d|90d|all
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
