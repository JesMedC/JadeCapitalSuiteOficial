using System.Globalization;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Shared.Kernel.Money;

/// <summary>
/// Value Object Money = Amount (decimal) + Currency (Currency).
///
/// Inmutable. Toda operacion que pueda fallar por mismatch de moneda devuelve
/// Result&lt;Money&gt; en lugar de tirar excepcion (caller decide que hacer).
///
/// Invariante de rango: Math.Abs(Amount) &lt; 10^16. Esto garantiza que el valor
/// entra en NUMERIC(24,8) de Postgres (24 digitos totales, 8 decimales,
/// 16 digitos enteros). El techo del .NET decimal (~7.9e28) es mucho mas alto
/// que NUMERIC(24,8), asi que esta validacion es estrictamente de persistencia.
///
/// La suma y resta requieren misma moneda. La multiplicacion por scalar decimal
/// SI permite cambiar la unidad: el caller es responsable de que el resultado
/// siga teniendo sentido (ej: price * volume produce PnL en otra unidad).
///
/// ## Persistencia
///
/// Las propiedades "planas" `Amount` (decimal) + `CurrencyCode` (string)
/// son las que EF Core mapea via OwnsOne. La propiedad `Currency` es derivada
/// (`Currency.FromTrusted(CurrencyCode)`) — nunca se persiste, EF la ignora.
/// Esto permite que el constructor `(decimal, string)` mapee directamente
/// a las columnas del DB sin necesidad de constructores especiales.
/// </summary>
public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string CurrencyCode { get; }
    public Currency Currency => Currency.FromTrusted(CurrencyCode);

    /// <summary>
    /// Techo de magnitud para NUMERIC(24,8). 10^16 = 10.000.000.000.000.000.
    /// El maximo entero representable es 10^16 - 1.
    /// </summary>
    public const decimal MaxAmount = 10_000_000_000_000_000m;

    /// <summary>
    /// Constructor privado para EF Core. Mapea Amount + CurrencyCode
    /// directamente a las columnas del DB.
    /// </summary>
    private Money(decimal amount, string currencyCode)
    {
        Amount = amount;
        CurrencyCode = currencyCode;
    }

    /// <summary>
    /// Crea Money validando rango del Amount y construccion del Currency.
    /// Usado en codigo de dominio (handlers, factories de Aggregate).
    /// </summary>
    public static Result<Money> Create(decimal amount, Currency currency)
    {
        if (currency is null)
            return Result.Failure<Money>(MoneyErrors.Currency.CodeRequired);

        if (decimal.IsNegative(amount) ? -amount >= MaxAmount : amount >= MaxAmount)
            return Result.Failure<Money>(MoneyErrors.Money.AmountOutOfRange);

        return Result.Success(new Money(amount, currency.Code));
    }

    /// <summary>
    /// Acceso directo sin validacion de rango. Usar SOLO desde Infrastructure
    /// al hidratar desde DB, donde el valor ya paso por la validacion al
    /// escribirse.
    /// </summary>
    public static Money FromTrusted(decimal amount, Currency currency) => new(amount, currency.Code);

    /// <summary>
    /// Variante que acepta el codigo de moneda directo, util para hidratar
    /// desde columnas del DB sin necesidad de un Currency ya construido.
    /// </summary>
    public static Money FromTrusted(decimal amount, string currencyCode) => new(amount, currencyCode);

    // ============================================
    // Operadores
    // ============================================

    /// <summary>Suma. Falla con CurrencyMismatch si las monedas difieren.</summary>
    public static Result<Money> operator +(Money a, Money b)
    {
        if (a is null || b is null)
            return Result.Failure<Money>(MoneyErrors.Money.CurrencyMismatch);

        if (!a.Currency.Equals(b.Currency))
            return Result.Failure<Money>(MoneyErrors.Money.CurrencyMismatch);

        var newAmount = a.Amount + b.Amount;
        if (Math.Abs(newAmount) >= MaxAmount)
            return Result.Failure<Money>(MoneyErrors.Money.AmountOutOfRange);

        return Result.Success(new Money(newAmount, a.CurrencyCode));
    }

    /// <summary>Resta. Falla con CurrencyMismatch si las monedas difieren.</summary>
    public static Result<Money> operator -(Money a, Money b)
    {
        if (a is null || b is null)
            return Result.Failure<Money>(MoneyErrors.Money.CurrencyMismatch);

        if (!a.Currency.Equals(b.Currency))
            return Result.Failure<Money>(MoneyErrors.Money.CurrencyMismatch);

        var newAmount = a.Amount - b.Amount;
        if (Math.Abs(newAmount) >= MaxAmount)
            return Result.Failure<Money>(MoneyErrors.Money.AmountOutOfRange);

        return Result.Success(new Money(newAmount, a.CurrencyCode));
    }

    /// <summary>
    /// Multiplicacion por scalar decimal. Mantiene la moneda del operando izquierdo.
    /// No falla por moneda (no hay segunda moneda); si falla es por overflow.
    /// </summary>
    public static Result<Money> operator *(Money a, decimal multiplier)
    {
        if (a is null)
            return Result.Failure<Money>(MoneyErrors.Money.CurrencyMismatch);

        var newAmount = a.Amount * multiplier;
        if (Math.Abs(newAmount) >= MaxAmount)
            return Result.Failure<Money>(MoneyErrors.Money.AmountOutOfRange);

        return Result.Success(new Money(newAmount, a.CurrencyCode));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return CurrencyCode;
    }

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Amount} {CurrencyCode}");
}
