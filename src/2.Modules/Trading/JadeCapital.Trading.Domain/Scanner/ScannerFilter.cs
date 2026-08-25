using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Domain.Common;

namespace JadeCapital.Trading.Domain.Scanner;

// ============================================================================
//  ScannerFilter aggregate (slice 4a, Wave 4).
//  Named set of filters the trader saves to run scans against the instrument universe.
//  Spread/volume filters are declared but NOT YET WIRED to live market data
//  (Wave 4b will add IQuoteProvider.GetQuoteAsync + a quotes_cache table).
//  HistoricalRiskReward is computed from the user's closed trades.
// ============================================================================

public enum VolatilityWindow : byte
{
    Daily = 1,
    Weekly = 7,
    Monthly = 30,
}

public sealed class ScannerFilter : AggregateRoot<Guid>
{
    public const int MaxNameLength = 64;

    public Guid UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public decimal? MinSpread { get; private set; }
    public decimal? MaxSpread { get; private set; }
    public decimal? MinVolume { get; private set; }
    public decimal? MinRiskReward { get; private set; }
    public VolatilityWindow VolatilityWindow { get; private set; }
    public string? ActiveHours { get; private set; }
    public bool IsActive { get; private set; }

    private ScannerFilter() { }

    public static Result<ScannerFilter> Create(
        Guid userId, string name, decimal? minSpread, decimal? maxSpread,
        decimal? minVolume, decimal? minRiskReward, VolatilityWindow window,
        string? activeHoursJson, IClock clock)
    {
        if (userId == Guid.Empty)
            return Result.Failure<ScannerFilter>(TradingDomainErrors.Scanner.UserIdRequired);
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<ScannerFilter>(TradingDomainErrors.Scanner.NameRequired);
        if (name.Length > MaxNameLength)
            return Result.Failure<ScannerFilter>(TradingDomainErrors.Scanner.NameTooLong);
        if (minSpread is < 0)
            return Result.Failure<ScannerFilter>(TradingDomainErrors.Scanner.SpreadNegative);
        if (maxSpread is < 0)
            return Result.Failure<ScannerFilter>(TradingDomainErrors.Scanner.SpreadNegative);
        if (minVolume is < 0)
            return Result.Failure<ScannerFilter>(TradingDomainErrors.Scanner.VolumeNegative);
        if (minRiskReward is < 1m)
            return Result.Failure<ScannerFilter>(TradingDomainErrors.Scanner.InvalidRiskReward);
        if ((byte)window is < 1 or > 30)
            return Result.Failure<ScannerFilter>(TradingDomainErrors.Scanner.InvalidVolatilityWindow);
        if (minSpread.HasValue && maxSpread.HasValue && minSpread > maxSpread)
            return Result.Failure<ScannerFilter>(TradingDomainErrors.Scanner.MinGreaterThanMax);

        var now = clock.UtcNow;
        var filter = new ScannerFilter
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name.Trim(),
            MinSpread = minSpread,
            MaxSpread = maxSpread,
            MinVolume = minVolume,
            MinRiskReward = minRiskReward,
            VolatilityWindow = window,
            ActiveHours = activeHoursJson,
            IsActive = true,
            UpdatedAt = now,
        };
        filter.SetCreatedAt(now);
        filter.RaiseDomainEvent(new ScannerFilterCreatedDomainEvent(filter.Id, userId, now));
        return Result.Success(filter);
    }

    public Result Update(
        string name, decimal? minSpread, decimal? maxSpread, decimal? minVolume,
        decimal? minRiskReward, VolatilityWindow window, string? activeHoursJson,
        IClock clock)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(TradingDomainErrors.Scanner.NameRequired);
        if (name.Length > MaxNameLength)
            return Result.Failure(TradingDomainErrors.Scanner.NameTooLong);
        if (minSpread is < 0 || maxSpread is < 0)
            return Result.Failure(TradingDomainErrors.Scanner.SpreadNegative);
        if (minVolume is < 0)
            return Result.Failure(TradingDomainErrors.Scanner.VolumeNegative);
        if (minRiskReward is < 1m)
            return Result.Failure(TradingDomainErrors.Scanner.InvalidRiskReward);
        if ((byte)window is < 1 or > 30)
            return Result.Failure(TradingDomainErrors.Scanner.InvalidVolatilityWindow);
        if (minSpread.HasValue && maxSpread.HasValue && minSpread > maxSpread)
            return Result.Failure(TradingDomainErrors.Scanner.MinGreaterThanMax);

        Name = name.Trim();
        MinSpread = minSpread;
        MaxSpread = maxSpread;
        MinVolume = minVolume;
        MinRiskReward = minRiskReward;
        VolatilityWindow = window;
        ActiveHours = activeHoursJson;
        UpdatedAt = clock.UtcNow;
        RaiseDomainEvent(new ScannerFilterUpdatedDomainEvent(Id, UserId, clock.UtcNow));
        return Result.Success();
    }

    public void Activate(IClock clock) { if (!IsActive) { IsActive = true; UpdatedAt = clock.UtcNow; } }
    public void Deactivate(IClock clock) { if (IsActive) { IsActive = false; UpdatedAt = clock.UtcNow; } }
}
