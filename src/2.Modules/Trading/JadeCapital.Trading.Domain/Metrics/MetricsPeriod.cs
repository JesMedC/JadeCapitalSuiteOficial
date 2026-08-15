namespace JadeCapital.Trading.Domain.Metrics;

/// <summary>
/// Ventana temporal para /api/trades/metrics.
/// 'all' desactiva el filtro de fecha (cubre TODA la historia del usuario).
/// </summary>
public enum MetricsPeriod
{
    Last7Days = 1,
    Last30Days = 2,
    Last90Days = 3,
    All = 4,
}

public static class MetricsPeriodExtensions
{
    public const string AllKey = "all";
    public const string Last7DaysKey = "7d";
    public const string Last30DaysKey = "30d";
    public const string Last90DaysKey = "90d";

    /// <summary>
    /// Parsea el query-string 'period' a enum. Falla a Last30Days si no se reconoce
    /// (la validacion FluentValidation rechaza el body antes de llegar aca).
    /// </summary>
    public static MetricsPeriod FromKey(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        Last7DaysKey => MetricsPeriod.Last7Days,
        Last30DaysKey => MetricsPeriod.Last30Days,
        Last90DaysKey => MetricsPeriod.Last90Days,
        AllKey => MetricsPeriod.All,
        _ => MetricsPeriod.Last30Days,
    };

    public static string ToKey(this MetricsPeriod period) => period switch
    {
        MetricsPeriod.Last7Days => Last7DaysKey,
        MetricsPeriod.Last30Days => Last30DaysKey,
        MetricsPeriod.Last90Days => Last90DaysKey,
        MetricsPeriod.All => AllKey,
        _ => Last30DaysKey,
    };

    /// <summary>
    /// Devuelve la fecha de inicio (inclusive) o null para All. 'now' se inyecta
    /// para que el handler pueda fijar el reloj.
    /// </summary>
    public static DateTimeOffset? FromDate(this MetricsPeriod period, DateTimeOffset now) => period switch
    {
        MetricsPeriod.Last7Days => now.AddDays(-7),
        MetricsPeriod.Last30Days => now.AddDays(-30),
        MetricsPeriod.Last90Days => now.AddDays(-90),
        MetricsPeriod.All => null,
        _ => now.AddDays(-30),
    };
}
