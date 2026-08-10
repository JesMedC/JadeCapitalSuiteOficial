namespace JadeCapital.Trading.Domain.Enums;

/// <summary>
/// Estado del ciclo de vida de un trade. Open es el unico estado del que se
/// puede transicionar (a Closed o Cancelled). Closed y Cancelled son terminales.
/// </summary>
public enum TradeStatus : short
{
    Open = 1,
    Closed = 2,
    Cancelled = 3,
}
