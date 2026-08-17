using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Contracts.Scanner;
using JadeCapital.Trading.Domain.Scanner;

namespace JadeCapital.Trading.Application._Common;

// ============================================================================
//  Scanner mapping helpers (slice 4a).
// ============================================================================

public static class ScannerMappingExtensions
{
    public static ScannerFilterDto ToDto(this ScannerFilter f)
        => new(
            f.Id, f.Name, f.MinSpread, f.MaxSpread, f.MinVolume, f.MinRiskReward,
            (byte)f.VolatilityWindow, f.ActiveHours, f.IsActive, f.CreatedAt, f.UpdatedAt ?? f.CreatedAt);
}
