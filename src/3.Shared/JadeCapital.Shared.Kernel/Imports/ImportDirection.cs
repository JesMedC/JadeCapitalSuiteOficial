#pragma warning disable CA1720

namespace JadeCapital.Shared.Kernel.Imports;

/// <summary>
/// Trade direction in an imported row. Mirrors the <see cref="JadeCapital.Trading.Domain.Enums.TradeDirection"/>
/// encoding (Long=1, Short=2) so the streaming pipeline can map without
/// translation; the parsers emit this directly.
/// </summary>
public enum ImportDirection : byte
{
    Long = 1,
    Short = 2,
}