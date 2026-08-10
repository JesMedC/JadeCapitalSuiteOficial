#pragma warning disable CA1720

namespace JadeCapital.Trading.Domain.Enums;

/// <summary>
/// Direccion del trade. Long apuesta a suba, Short a baja.
///
/// CA1720 suprimido: Long/Short son terminos de dominio establecidos
/// en trading y seria confuso renombrarlos (e.g. LongPosition rompe la
/// semantica del enum y choca con la nomenclatura del broker).
/// </summary>
public enum TradeDirection : short
{
    Long = 1,
    Short = 2,
}
