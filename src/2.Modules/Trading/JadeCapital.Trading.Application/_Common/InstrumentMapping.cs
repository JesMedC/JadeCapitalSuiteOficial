using JadeCapital.Trading.Domain.Instruments;

namespace JadeCapital.Trading.Application._Common;

/// <summary>
/// Extension centralizada para mapear entity Instrument -&gt; InstrumentDto
/// (projection completa para los endpoints CRUD).
/// </summary>
internal static class InstrumentMapping
{
    public static InstrumentDto ToDto(this Instrument instrument)
        => new(
            instrument.Id,
            instrument.Symbol.Value,
            instrument.AssetClasses,
            instrument.ContractSize,
            instrument.DecimalPlaces,
            instrument.PipValue,
            instrument.PayoutPercent,
            instrument.IsActive,
            instrument.CreatedAt,
            instrument.UpdatedAt ?? instrument.CreatedAt);
}
