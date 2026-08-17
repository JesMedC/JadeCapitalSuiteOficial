// ============================================================================
//  Journal Daily API types — slice 2a.2 frontend.
//
//  Mirror of:
//  - src/2.Modules/Trading/JadeCapital.Trading.Contracts/Journal/JournalDtos.cs
//  - src/2.Modules/Trading/JadeCapital.Trading.Api/Endpoints/JournalEndpoints.cs
//
//  The backend normalizes the journal to one entry per (user, local_date).
//  Moods use a 1..5 scale (1=Fearful, 5=Euphoric), aligned with the
//  pre-trade checklist `emotionality` field. Validation caps are mirrored
//  client-side so the trader gets instant feedback before hitting the API.
// ============================================================================

/** Mood pre-market / during / post-market: 1=Fearful, 2=Anxious, 3=Neutral, 4=Confident, 5=Euphoric. */
export type Mood = 1 | 2 | 3 | 4 | 5;

/** Localized label for the 5-point mood scale. */
export const MOOD_LABELS: Record<number, string> = {
  1: 'Asustado',
  2: 'Ansioso',
  3: 'Neutral',
  4: 'Confiado',
  5: 'Eufórico',
};

export interface JournalEntryDto {
  id: string;
  /** ISO date in YYYY-MM-DD format. Resolved from the user's timezone (or UTC fallback). */
  localDate: string;
  timezone: string;
  moodPre: Mood | null;
  moodDuring: Mood | null;
  moodPost: Mood | null;
  premarketPlan: string | null;
  postmarketReflection: string | null;
  tags: string[];
  createdAt: string;
  updatedAt: string;
}

/** Body for POST /api/journal/today. All fields are optional — the upsert merges with the existing entry. */
export interface UpsertJournalEntryRequest {
  moodPre?: Mood | null;
  moodDuring?: Mood | null;
  moodPost?: Mood | null;
  premarketPlan?: string | null;
  postmarketReflection?: string | null;
  tags?: string[];
}

/** RFC 7807 problem shape returned by the backend on 422 / 404 / 5xx. */
export interface JournalProblem {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  errors?: Record<string, string[]>;
}

/** Hard caps — mirror the backend validator so the trader gets instant feedback. */
export const PLAN_MAX = 2000;
export const REFLECTION_MAX = 5000;
export const TAGS_MAX = 10;
export const TAG_LEN_MAX = 32;
