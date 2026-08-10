namespace JadeCapital.Trading.Domain.Enums;

/// <summary>
/// Tipo de mercado de una cuenta de trading. Determina qué campos son
/// requeridos al crear trades y configura la UI del formulario.
///
/// - Forex: apalancamiento configurable, direccion Long/Short, soporta
///   Stop Loss / Take Profit / R multiples.
/// - Binary: opciones binarias, direccion Call/Put, Payout% viene del
///   Instrument. NO tiene apalancamiento relevante.
/// </summary>
public enum MarketType : short
{
    Forex = 1,
    Binary = 2,
}
