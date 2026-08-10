using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Shared.Kernel.Money;

/// <summary>
/// Value Object que representa una moneda siguiendo ISO 4217 (3 letras mayusculas).
///
/// Inmutable. Igualdad estricta por codigo. Solo se aceptan monedas de la
/// lista permitida por Create(): mantenerla chica y estable evita que el
/// sistema acumule basura alfabetica que el provider externo no soporta.
///
/// Las instancias "estaticas" (Usd, Eur, etc.) son shortcuts validados en
/// arranque de proceso: como el code es valido, no tiene sentido devolver
/// Result al estilo "Currency.Usd" en dominio.
/// </summary>
public sealed class Currency : ValueObject
{
    public string Code { get; }

    private Currency(string code)
    {
        Code = code;
    }

    public static readonly Currency Usd = new("USD");
    public static readonly Currency Eur = new("EUR");
    public static readonly Currency Gbp = new("GBP");
    public static readonly Currency Jpy = new("JPY");
    public static readonly Currency Chf = new("CHF");
    public static readonly Currency Aud = new("AUD");
    public static readonly Currency Cad = new("CAD");
    public static readonly Currency Nzd = new("NZD");
    public static readonly Currency Xau = new("XAU");
    public static readonly Currency Xag = new("XAG");
    public static readonly Currency Btc = new("BTC");
    public static readonly Currency Eth = new("ETH");

    private static readonly HashSet<string> SupportedCodes = new(StringComparer.Ordinal)
    {
        "USD", "EUR", "GBP", "JPY", "CHF", "AUD", "CAD", "NZD",
        "XAU", "XAG", "BTC", "ETH",
    };

    /// <summary>
    /// Crea una moneda validando codigo ISO 4217-like (3 letras ASCII mayusculas)
    /// y pertenencia a la lista permitida. La normalizacion a mayusculas se hace
    /// aca para que el resto del dominio opere siempre con codigo canonico.
    /// </summary>
    public static Result<Currency> Create(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure<Currency>(MoneyErrors.Currency.CodeRequired);

        var normalized = code.Trim().ToUpperInvariant();

        if (normalized.Length != 3)
            return Result.Failure<Currency>(MoneyErrors.Currency.CodeInvalidLength);

        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];
            if (c < 'A' || c > 'Z')
                return Result.Failure<Currency>(MoneyErrors.Currency.CodeInvalidFormat);
        }

        if (!SupportedCodes.Contains(normalized))
            return Result.Failure<Currency>(MoneyErrors.Currency.CodeUnsupported);

        return Result.Success(new Currency(normalized));
    }

    /// <summary>
    /// Acceso directo por code canonico. NO valida: usar SOLO para codes hard-coded
    /// (serializacion, migraciones, fixtures de tests). Para input externo, usar Create.
    /// </summary>
    public static Currency FromTrustedCode(string code) => new(code.Trim().ToUpperInvariant());

    /// <summary>
    /// Alias corto de FromTrustedCode para uso en composicion (e.g. Money.FromTrusted).
    /// </summary>
    public static Currency FromTrusted(string code) => FromTrustedCode(code);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Code;
    }

    public override string ToString() => Code;
}
