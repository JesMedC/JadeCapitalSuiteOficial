namespace JadeCapital.Trading.Domain.Enums;

/// <summary>
/// Clase de activo subyacente. Se infiere a partir del Symbol pero el caller
/// puede override via parametro de factory cuando el simbolo es ambiguo
/// (e.g. brokers que aceptan "XAUUSD" tanto para Commodity como para Forex).
/// </summary>
public enum AssetClass : short
{
    Forex = 1,
    Crypto = 2,
    Binary = 3,
    Commodity = 4,
    Other = 5,
}
