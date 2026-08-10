namespace JadeCapital.Trading.Domain.Enums;

/// <summary>
/// Clase(s) de activo a las que un Instrument pertenece. Un Instrument
/// puede pertenecer a multiples clases (ej. EUR/USD puede ser usado
/// para Forex Y para opciones binarias).
///
/// Se serializa como SMALLINT (bitmask) en la BD. El bit individual
/// identifica la clase: Forex=bit0, Crypto=bit1, Binary=bit2,
/// Commodity=bit3, Other=bit4.
///
/// Trade.AssetClass representa UNA sola clase (la del trade puntual,
/// derivada de Account.MarketType); usa el mismo enum pero como
/// valor single-bit (1, 2, 4, 8 o 16).
/// </summary>
[Flags]
public enum AssetClass : short
{
    None      = 0,
    Forex     = 1,    // 1 << 0
    Crypto    = 2,    // 1 << 1
    Binary    = 4,    // 1 << 2
    Commodity = 8,    // 1 << 3
    Other     = 16,   // 1 << 4
}
