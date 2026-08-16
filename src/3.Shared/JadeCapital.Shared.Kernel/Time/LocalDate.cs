namespace JadeCapital.Shared.Kernel.Time;

/// <summary>
/// Calendar date in the user's local timezone (no time, no offset).
/// Inmutable. Compara por valor (record struct).
///
/// La conversion desde <see cref="DateTimeOffset"/> usa
/// <see cref="TimeZoneInfo.FindSystemTimeZoneById"/> con el nombre IANA
/// que el cliente envio en el header <c>X-User-Timezone</c>. Si el
/// timezone no se reconoce, cae a UTC (defensa en profundidad — la
/// aplicacion ya valida el formato IANA antes de llegar aqui).
///
/// ## Persistencia
///
/// EF Core persiste <c>Year</c>/<c>Month</c>/<c>Day</c> como columnas
/// separadas y deriva una <c>DateOnly</c> en el OnModelCreating para
/// mapear a la columna DATE del DB. O alternativamente, el handler
/// convierte a <see cref="DateOnly"/> en el mapper hacia el repositorio.
/// </summary>
public readonly record struct LocalDate(int Year, int Month, int Day)
{
    /// <summary>Construye un LocalDate a partir de un <see cref="DateOnly"/>.</summary>
    public static LocalDate From(DateOnly date) => new(date.Year, date.Month, date.Day);

    /// <summary>
    /// Construye un LocalDate a partir de un instante UTC + un nombre IANA
    /// (e.g. <c>America/Argentina/Buenos_Aires</c>). Si el sistema no
    /// reconoce el timezone, cae a UTC (la aplicacion valida el formato
    /// del header <c>X-User-Timezone</c> antes de invocar este metodo).
    /// </summary>
    public static LocalDate From(DateTimeOffset utc, string ianaTimezone)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(ianaTimezone);
            var local = TimeZoneInfo.ConvertTime(utc, tz);
            return new LocalDate(local.Year, local.Month, local.Day);
        }
        catch (TimeZoneNotFoundException)
        {
            return FromUtc(utc);
        }
        catch (InvalidTimeZoneException)
        {
            return FromUtc(utc);
        }
    }

    private static LocalDate FromUtc(DateTimeOffset utc)
    {
        var d = utc.UtcDateTime;
        return new LocalDate(d.Year, d.Month, d.Day);
    }

    /// <summary>Representacion ISO 8601 (<c>YYYY-MM-DD</c>).</summary>
    public string Iso8601 => $"{Year:0000}-{Month:00}-{Day:00}";

    /// <summary>Convierte a <see cref="DateOnly"/> para persistir en columnas DATE.</summary>
    public DateOnly ToDateOnly() => new(Year, Month, Day);

    /// <summary>
    /// Comparacion cronologica: primero por Year, luego Month, luego Day.
    /// Implementada manualmente porque C# no genera los operadores
    /// automaticamente para <c>readonly record struct</c>.
    /// </summary>
    public int CompareTo(LocalDate other)
    {
        var c = Year.CompareTo(other.Year);
        if (c != 0) return c;
        c = Month.CompareTo(other.Month);
        if (c != 0) return c;
        return Day.CompareTo(other.Day);
    }

    public static bool operator <(LocalDate a, LocalDate b) => a.CompareTo(b) < 0;
    public static bool operator >(LocalDate a, LocalDate b) => a.CompareTo(b) > 0;
    public static bool operator <=(LocalDate a, LocalDate b) => a.CompareTo(b) <= 0;
    public static bool operator >=(LocalDate a, LocalDate b) => a.CompareTo(b) >= 0;
}
