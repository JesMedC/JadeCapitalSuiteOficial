namespace JadeCapital.Shared.Kernel.Imports;

/// <summary>
/// Lifecycle status of an imported trade row. Mirrors
/// <see cref="JadeCapital.Trading.Domain.Enums.TradeStatus"/> (Open=1, Closed=2);
/// Cancelled is excluded because cancelled trades are not exported by any
/// broker — an import is the source of historical truth.
/// </summary>
public enum ImportRowStatus : byte
{
    Open = 1,
    Closed = 2,
}