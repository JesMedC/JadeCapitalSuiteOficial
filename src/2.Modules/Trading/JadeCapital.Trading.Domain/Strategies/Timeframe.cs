namespace JadeCapital.Trading.Domain.Strategies;

// ============================================================================
//  Timeframe — slice 3a (Trader Strategies + Alerts + Planner).
//
//  Identifica el timeframe del chart que el trader usa para una strategy.
//  Stored as SMALLINT 1..10 en la DB (mapeado via value converter a byte).
//  El rango [1,10] deja headroom para una futura extension (e.g. Y1=10)
//  sin tocar la migration ni el enum del dominio.
//
//  Note: Unspecified = 0 permite semantica nullable en la columna SMALLINT
//  (la columna es nullable en la DB). Los valores 1..9 son los unicos que
//  acepta el aggregate Strategy.Create/Update; el CHECK constraint en DB
//  enforce 1..10 como red de seguridad.
// ============================================================================

public enum Timeframe : byte
{
    Unspecified = 0,

    M1  = 1,
    M5  = 2,
    M15 = 3,
    M30 = 4,
    H1  = 5,
    H4  = 6,
    D1  = 7,
    W1  = 8,
    MN  = 9,
}