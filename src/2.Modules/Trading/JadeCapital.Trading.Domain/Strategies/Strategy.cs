using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Domain.Common;

namespace JadeCapital.Trading.Domain.Strategies;

// ============================================================================
//  Strategy aggregate — slice 3a (Trader Strategies + Alerts + Planner).
//
//  Representa el setup que el trader describe: nombre, instrumento (opcional
//  = multi-symbol), timeframe, reglas. Soft-delete via is_active=false.
//
//  Reglas de negocio:
//   - Name: 1..MaxNameLength chars, trimmed, case-insensitive (uniqueness
//     enforced en DB via partial UNIQUE INDEX sobre lower(name) WHERE is_active).
//   - Description <= MaxDescriptionLength chars (nullable).
//   - Rules <= MaxRulesLength chars (nullable).
//   - Timeframe ∈ {M1..MN} (range byte 1..9). El aggregate rechaza 0 (Unspecified)
//     y cualquier valor fuera del rango. La DB enforce 1..10 como safety net.
//   - Symbol null = multi-symbol strategy. Si viene un valor, el aggregate lo
//     persiste sin validar (la validacion contra trading.instruments vive en
//     el handler de Application — aca solo persistimos).
//   - Soft-delete: Deactivate() flipea is_active=false. Activate() lo
//     re-flipea a true. Ambas son idempotentes.
//   - Update preserva CreatedAt; actualiza UpdatedAt via Touch().
//
//  Invariantes:
//   - Id != Guid.Empty.
//   - UserId != Guid.Empty.
//   - IsActive default true en Create.
// ============================================================================

public sealed class Strategy : AggregateRoot<Guid>
{
    public const int MaxNameLength = 64;
    public const int MaxDescriptionLength = 1000;
    public const int MaxRulesLength = 2000;

    public Guid UserId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public string? Symbol { get; private set; }
    public Timeframe? Timeframe { get; private set; }
    public string? Rules { get; private set; }
    public bool IsActive { get; private set; }

    // EF Core.
    private Strategy() { }

    private Strategy(
        Guid id,
        Guid userId,
        string name,
        string? description,
        string? symbol,
        Timeframe? timeframe,
        string? rules,
        IClock clock) : base(id)
    {
        UserId = userId;
        Name = name;
        Description = description;
        Symbol = symbol;
        Timeframe = timeframe;
        Rules = rules;
        IsActive = true;

        // Override CreatedAt/UpdatedAt via el clock inyectado (testeable).
        var now = clock.UtcNow;
        SetCreatedAt(now);
        UpdatedAt = now;
    }

    /// <summary>
    /// Factory: crea una strategy activa para el user.
    /// Validaciones (defense in depth con la DB):
    /// <list type="number">
    ///   <item>userId != Guid.Empty.</item>
    ///   <item>name no vacio, trimmed, length 1..MaxNameLength.</item>
    ///   <item>description null o length &lt;= MaxDescriptionLength.</item>
    ///   <item>rules null o length &lt;= MaxRulesLength.</item>
    ///   <item>timeframe null o ∈ {M1..MN} (byte 1..9). El aggregate rechaza
    ///   Unspecified=0 y cualquier valor fuera del rango.</item>
    ///   <item>symbol null o no vacio (validacion contra instruments es en handler).</item>
    /// </list>
    /// En exito emite un <see cref="StrategyCreatedDomainEvent"/>.
    /// </summary>
    public static Result<Strategy> Create(
        Guid userId,
        string name,
        string? description,
        string? symbol,
        Timeframe? timeframe,
        string? rules,
        IClock clock)
    {
        if (userId == Guid.Empty)
            return Result.Failure<Strategy>(TradingDomainErrors.Strategy.UserIdRequired);

        var nameResult = NormalizeName(name);
        if (nameResult.IsFailure)
            return Result.Failure<Strategy>(nameResult.Error);

        if (description is not null && description.Length > MaxDescriptionLength)
            return Result.Failure<Strategy>(TradingDomainErrors.Strategy.DescriptionTooLong);

        if (rules is not null && rules.Length > MaxRulesLength)
            return Result.Failure<Strategy>(TradingDomainErrors.Strategy.RulesTooLong);

        if (timeframe.HasValue && !IsValidTimeframe(timeframe.Value))
            return Result.Failure<Strategy>(TradingDomainErrors.Strategy.InvalidTimeframe);

        var normalizedSymbol = NormalizeSymbol(symbol);
        if (normalizedSymbol.IsFailure)
            return Result.Failure<Strategy>(normalizedSymbol.Error);

        var normalizedDescription = NormalizeText(description);
        var normalizedRules = NormalizeText(rules);

        var id = Guid.NewGuid();
        var strategy = new Strategy(
            id, userId, nameResult.Value, normalizedDescription,
            normalizedSymbol.Value, timeframe, normalizedRules, clock);

        strategy.RaiseDomainEvent(new StrategyCreatedDomainEvent(
            strategy.Id, strategy.UserId, clock.UtcNow));

        return Result.Success(strategy);
    }

    /// <summary>
    /// Actualiza los campos mutables de la strategy. Preserva CreatedAt e
    /// IsActive; bumpea UpdatedAt via <paramref name="clock"/> en exito.
    ///
    /// Las mismas validaciones que <see cref="Create"/> (name 1..Max,
    /// description/rules length caps, timeframe en rango).
    /// </summary>
    public Result Update(
        string name,
        string? description,
        string? symbol,
        Timeframe? timeframe,
        string? rules,
        IClock clock)
    {
        var nameResult = NormalizeName(name);
        if (nameResult.IsFailure)
            return Result.Failure(nameResult.Error);

        if (description is not null && description.Length > MaxDescriptionLength)
            return Result.Failure(TradingDomainErrors.Strategy.DescriptionTooLong);

        if (rules is not null && rules.Length > MaxRulesLength)
            return Result.Failure(TradingDomainErrors.Strategy.RulesTooLong);

        if (timeframe.HasValue && !IsValidTimeframe(timeframe.Value))
            return Result.Failure(TradingDomainErrors.Strategy.InvalidTimeframe);

        var normalizedSymbol = NormalizeSymbol(symbol);
        if (normalizedSymbol.IsFailure)
            return Result.Failure(normalizedSymbol.Error);

        var now = clock.UtcNow;
        Name = nameResult.Value;
        Description = NormalizeText(description);
        Symbol = normalizedSymbol.Value;
        Timeframe = timeframe;
        Rules = NormalizeText(rules);
        UpdatedAt = now;

        RaiseDomainEvent(new StrategyUpdatedDomainEvent(Id, UserId, now));

        return Result.Success();
    }

    /// <summary>
    /// Soft-delete: flipea IsActive a false. Idempotente (segunda llamada
    /// no produce error). Bumpea UpdatedAt via <paramref name="clock"/>.
    /// </summary>
    public Result Deactivate(IClock clock)
    {
        if (!IsActive)
            return Result.Success();

        var now = clock.UtcNow;
        IsActive = false;
        UpdatedAt = now;
        RaiseDomainEvent(new StrategySoftDeletedDomainEvent(Id, UserId, now));
        return Result.Success();
    }

    /// <summary>
    /// Re-activa una strategy previamente soft-deleted. Idempotente.
    /// Bumpea UpdatedAt via <paramref name="clock"/>.
    /// </summary>
    public Result Activate(IClock clock)
    {
        if (IsActive)
            return Result.Success();

        IsActive = true;
        UpdatedAt = clock.UtcNow;
        return Result.Success();
    }

    // ===== Helpers =====

    private static Result<string> NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<string>(TradingDomainErrors.Strategy.NameRequired);

        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
            return Result.Failure<string>(TradingDomainErrors.Strategy.NameTooLong);

        return Result.Success(trimmed);
    }

    private static Result<string?> NormalizeSymbol(string? symbol)
    {
        if (symbol is null)
            return Result.Success<string?>(null);

        var trimmed = symbol.Trim();
        if (trimmed.Length == 0)
            return Result.Success<string?>(null);

        // Limite upper bound: VARCHAR(20) en DB. NO aplicamos regex aqui:
        // la validacion contra trading.instruments vive en el handler de
        // application (instrumentos son referencias dinamicas, no enum).
        if (trimmed.Length > 20)
            return Result.Failure<string?>(TradingDomainErrors.Strategy.SymbolTooLong);

        return Result.Success<string?>(trimmed.ToUpperInvariant());
    }

    private static string? NormalizeText(string? input)
        => string.IsNullOrWhiteSpace(input) ? null : input.Trim();

    private static bool IsValidTimeframe(Timeframe tf)
    {
        var t = (byte)tf;
        return t is >= 1 and <= 9;
    }
}