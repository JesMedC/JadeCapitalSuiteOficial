namespace JadeCapital.Trading.Domain.Planner;

// ============================================================================
//  PlannerStatus — slice 3c (Trader Strategies + Alerts + Planner).
//
//  Estado del ciclo de vida de una sesion planeada:
//   - Planned (1): default al crear. El trader todavia no ejecuto ni skip.
//   - Completed (2): el trader opero segun el plan (con o sin symbol match).
//   - Skipped (3): el trader decidio no operar ese dia (audit trail preserved).
//   - Cancelled (4): el trader cancelo explicitamente la sesion.
//
//  Stored as SMALLINT 1..4 en trading.planner_sessions.status (la migration
//  enforce el rango via CHECK constraint). Default 1 (Planned).
//
//  Razon del rango 1..4 (no 0..3): alineamos con el dominio (no dejar el 0
//  como sentinel de "Unspecified" aca — Planned es estado valido, no ausencia).
// ============================================================================

public enum PlannerStatus : byte
{
    Planned   = 1,
    Completed = 2,
    Skipped   = 3,
    Cancelled = 4,
}