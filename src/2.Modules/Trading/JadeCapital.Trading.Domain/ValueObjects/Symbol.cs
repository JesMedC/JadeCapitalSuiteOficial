using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Trading.Domain.Enums;

namespace JadeCapital.Trading.Domain.ValueObjects;

/// <summary>
/// Value Object que representa el simbolo de un instrumento financiero.
///
/// Formatos aceptados:
/// - "EUR/USD"   (Forex, slash separator)
/// - "BTC/USD"   (Crypto, slash separator)
/// - "XAU/USD"   (Commodity, slash separator)
/// - "BTCUSD"    (Crypto, sin separator)
/// - "R_100"     (Binary, synthetic index)
///
/// Inmutable. Validacion en Create(): 3-20 chars, mayusculas, letras/digitos/slash.
/// </summary>
public sealed class Symbol : ValueObject
{
    public string Value { get; }

    public const int MinLength = 3;
    public const int MaxLength = 20;

    private Symbol(string value)
    {
        Value = value;
    }

    public static Result<Symbol> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<Symbol>(TradingDomainErrors.Symbol.ValueRequired);

        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length < MinLength)
            return Result.Failure<Symbol>(TradingDomainErrors.Symbol.ValueTooShort);

        if (normalized.Length > MaxLength)
            return Result.Failure<Symbol>(TradingDomainErrors.Symbol.ValueTooLong);

        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];
            var isLetter = c >= 'A' && c <= 'Z';
            var isDigit = c >= '0' && c <= '9';
            var isSeparator = c == '/';
            if (!isLetter && !isDigit && !isSeparator)
                return Result.Failure<Symbol>(TradingDomainErrors.Symbol.ValueInvalidFormat);
        }

        return Result.Success(new Symbol(normalized));
    }

    /// <summary>
    /// Acceso directo sin validacion. SOLO para Infrastructure al hidratar
    /// desde DB donde el valor ya fue validado al persistirse.
    /// </summary>
    public static Symbol FromTrusted(string value) => new(value.Trim().ToUpperInvariant());

    /// <summary>
    /// Heuristica best-effort para inferir la clase de activo a partir del
    /// simbolo. El caller puede override via parametro cuando el simbolo es
    /// ambiguo (mismo simbolo usado en multiples asset classes por distintos brokers).
    /// </summary>
    public AssetClass DetectAssetClass()
    {
        // Metales preciosos: XAU (oro), XAG (plata), XPT (platino), XPD (paladio).
        if (Value.StartsWith("XAU", StringComparison.Ordinal)
            || Value.StartsWith("XAG", StringComparison.Ordinal)
            || Value.StartsWith("XPT", StringComparison.Ordinal)
            || Value.StartsWith("XPD", StringComparison.Ordinal))
        {
            return AssetClass.Commodity;
        }

        // Criptos: prefijos comunes. Si no matchea, el slash + length sugiere crypto/forex.
        if (Value.StartsWith("BTC", StringComparison.Ordinal)
            || Value.StartsWith("ETH", StringComparison.Ordinal)
            || Value.StartsWith("USDT", StringComparison.Ordinal)
            || Value.StartsWith("USDC", StringComparison.Ordinal))
        {
            return AssetClass.Crypto;
        }

        // Synthetic indices de brokers binarios (Deriv/IG etc): "BOOM500", "CRASH1000",
        // "JD10". NO incluimos "R_100" porque la validacion de Symbol no permite underscore.
        if (Value.StartsWith("BOOM", StringComparison.Ordinal)
            || Value.StartsWith("CRASH", StringComparison.Ordinal)
            || Value.StartsWith("JD", StringComparison.Ordinal))
        {
            return AssetClass.Binary;
        }

        // Cualquier par con "/" se asume Forex por default (EUR/USD, GBP/JPY, AUD/CAD).
        if (Value.Contains('/'))
            return AssetClass.Forex;

        return AssetClass.Other;
    }

    /// <summary>
    /// Infiere el codigo de moneda quotable (lado derecho del par) que se usa
    /// para validar EntryPrice / ExitPrice. Convencion:
    /// - "EUR/USD"   -> "USD"
    /// - "BTC/USDT"  -> "USDT"
    /// - "BTCUSD"    -> "USD" (ultimos 3-4 chars, intenta 4 primero por USDT)
    /// - "XAUUSD"    -> "USD"
    /// - "BOOM500"   -> vacio (no aplica, no hay quotable)
    /// </summary>
    public string InferQuoteCurrencyCode()
    {
        var slashIndex = Value.IndexOf('/');
        if (slashIndex >= 0 && slashIndex + 1 < Value.Length)
        {
            return Value[(slashIndex + 1)..];
        }

        // Sin slash: probar 4 chars (USDT, USDC) y luego 3 (USD, EUR, BTC).
        if (Value.Length >= 4)
        {
            var last4 = Value[^4..];
            if (last4 is "USDT" or "USDC" or "TUSD" or "BUSD")
                return last4;
        }
        if (Value.Length >= 3)
        {
            return Value[^3..];
        }
        return string.Empty;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
