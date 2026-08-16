/** Shape returned by the planner endpoints. Mirrors `PlannerSessionDto` backend. */

export interface PlannerSessionDto {
  id: string;
  userId: string;
  sessionDate: string;
  plannedStartTime: string | null;
  plannedEndTime: string | null;
  symbol: string | null;
  status: PlannerStatus;
  notes: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface PlannerSessionComparisonDto {
  sessionId: string;
  status: PlannerStatus;
  actualTradeCount: number;
  actualClosedTradeCount: number;
  actualSymbols: string[];
  followsPlan: boolean;
}

export interface PlannerWeekComparisonDto {
  planned: number;
  completed: number;
  skipped: number;
  cancelled: number;
  actualTrades: number;
  totalPnl: number;
}

export interface PlannerSessionWithComparisonDto {
  session: PlannerSessionDto;
  comparison: PlannerSessionComparisonDto;
}

export interface PlannerWeekDto {
  weekStartDate: string;
  sessions: PlannerSessionWithComparisonDto[];
  comparison: PlannerWeekComparisonDto;
}

export interface UpsertPlannerSessionRequest {
  sessionDate: string;
  plannedStartTime: string | null;
  plannedEndTime: string | null;
  symbol: string | null;
  notes: string | null;
}

export interface UpdatePlannerStatusRequest {
  newStatus: PlannerStatus;
}

export type PlannerStatus = 1 | 2 | 3 | 4;

export const PLANNER_STATUS_LABELS: Readonly<Record<PlannerStatus, string>> = {
  1: 'Planeada',
  2: 'Completada',
  3: 'Saltada',
  4: 'Cancelada',
};

export const PLANNER_STATUS_COLORS: Readonly<Record<PlannerStatus, string>> = {
  1: 'var(--blue)',
  2: 'var(--green)',
  3: 'var(--text-muted)',
  4: 'var(--red)',
};
