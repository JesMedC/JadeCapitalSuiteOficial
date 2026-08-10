using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Domain.Common;

namespace JadeCapital.Trading.Domain.Accounts;

/// <summary>
/// Aggregate Root de una cuenta de trading.
///
/// Una cuenta representa una posicion del usuario en un broker especifico
/// (e.g. "IC Markets EUR", "Deriv Synthetic"). Cada Account pertenece a
/// UN unico User; un User puede tener N Accounts.
///
/// Reglas de negocio:
/// - Name / Broker requeridos, max 80 chars.
/// - Currency es el codigo de la cuenta (3 letras mayusculas, mismo set
///   restringido que <see cref="Currency"/>).
/// - InitialBalance &gt;= 0 (cuentas sin deposito inicial validas, p.ej. demo).
/// - Leverage &gt; 0 (1:100 leverage -> 100.0).
/// - PayoutPercent en [0, 1]: para opciones binarias.
/// - IsActive default true; desactivacion preserva historial (los trades
///   existentes siguen siendo accesibles).
///
/// Cambios de estado disparan eventos para auditoria y para que el modulo
/// de aplicacion pueda re-proyectar caches / invalidar projections.
/// </summary>
public sealed class Account : AggregateRoot<Guid>
{
    public const int MaxNameLength = 80;
    public const int MaxBrokerLength = 80;

    public Guid UserId { get; private set; }
    public string Name { get; private set; } = default!;
    public string Broker { get; private set; } = default!;
    public string Currency { get; private set; } = default!;
    public decimal InitialBalance { get; private set; }
    public decimal Leverage { get; private set; }
    public decimal PayoutPercent { get; private set; }
    public bool IsActive { get; private set; }

    // EF Core.
    private Account() { }

    private Account(
        Guid id,
        Guid userId,
        string name,
        string broker,
        string currency,
        decimal initialBalance,
        decimal leverage,
        decimal payoutPercent,
        DateTimeOffset openedAt) : base(id)
    {
        UserId = userId;
        Name = name;
        Broker = broker;
        Currency = currency;
        InitialBalance = initialBalance;
        Leverage = leverage;
        PayoutPercent = payoutPercent;
        IsActive = true;
        // CreatedAt ya lo setea Entity<TId>; lo reescribimos al "openedAt" de negocio
        // para consistencia temporal con el resto del dominio.
        CreatedAt = openedAt;
    }

    /// <summary>
    /// Abre una nueva cuenta. Validaciones: id != Guid.Empty, userId != Guid.Empty,
    /// name y broker no vacios y dentro del max length, currency valida (3 letras
    /// mayusculas via <see cref="Currency.Create"/>), initialBalance &gt;= 0,
    /// leverage &gt; 0, payoutPercent en [0, 1]. Estado inicial: IsActive = true.
    /// Emite <see cref="AccountOpenedDomainEvent"/>.
    /// </summary>
    public static Result<Account> Open(
        Guid id,
        Guid userId,
        string name,
        string broker,
        string currency,
        decimal initialBalance,
        decimal leverage,
        decimal payoutPercent,
        IClock clock)
    {
        if (id == Guid.Empty)
            return Result.Failure<Account>(TradingDomainErrors.Account.IdRequired);

        if (userId == Guid.Empty)
            return Result.Failure<Account>(TradingDomainErrors.Account.UserIdRequired);

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Account>(TradingDomainErrors.Account.NameRequired);

        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
            return Result.Failure<Account>(TradingDomainErrors.Account.NameTooLong);

        if (string.IsNullOrWhiteSpace(broker))
            return Result.Failure<Account>(TradingDomainErrors.Account.BrokerRequired);

        var trimmedBroker = broker.Trim();
        if (trimmedBroker.Length > MaxBrokerLength)
            return Result.Failure<Account>(TradingDomainErrors.Account.BrokerTooLong);

        var currencyResult = JadeCapital.Shared.Kernel.Money.Currency.Create(currency);
        if (currencyResult.IsFailure)
            return Result.Failure<Account>(TradingDomainErrors.Account.CurrencyCodeInvalid);

        if (initialBalance < 0m)
            return Result.Failure<Account>(TradingDomainErrors.Account.InitialBalanceMustBeNonNegative);

        if (leverage <= 0m)
            return Result.Failure<Account>(TradingDomainErrors.Account.LeverageMustBePositive);

        if (payoutPercent < 0m || payoutPercent > 1m)
            return Result.Failure<Account>(TradingDomainErrors.Account.PayoutPercentOutOfRange);

        var openedAt = clock.UtcNow;

        var account = new Account(
            id,
            userId,
            trimmedName,
            trimmedBroker,
            currencyResult.Value.Code,
            initialBalance,
            leverage,
            payoutPercent,
            openedAt);

        account.RaiseDomainEvent(new AccountOpenedDomainEvent(
            account.Id,
            account.UserId,
            account.Name,
            account.Currency,
            account.InitialBalance,
            openedAt));

        return Result.Success(account);
    }

    /// <summary>
    /// Actualiza metadata de la cuenta. NO toca balances (los balances son
    /// derivados de trades + deposits; en 1.5A los ignoramos). Mismas
    /// validaciones que <see cref="Open"/>.
    /// </summary>
    public Result UpdateMetadata(
        string name,
        string broker,
        string currency,
        decimal leverage,
        decimal payoutPercent)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(TradingDomainErrors.Account.NameRequired);

        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
            return Result.Failure(TradingDomainErrors.Account.NameTooLong);

        if (string.IsNullOrWhiteSpace(broker))
            return Result.Failure(TradingDomainErrors.Account.BrokerRequired);

        var trimmedBroker = broker.Trim();
        if (trimmedBroker.Length > MaxBrokerLength)
            return Result.Failure(TradingDomainErrors.Account.BrokerTooLong);

        var currencyResult = JadeCapital.Shared.Kernel.Money.Currency.Create(currency);
        if (currencyResult.IsFailure)
            return Result.Failure(TradingDomainErrors.Account.CurrencyCodeInvalid);

        if (leverage <= 0m)
            return Result.Failure(TradingDomainErrors.Account.LeverageMustBePositive);

        if (payoutPercent < 0m || payoutPercent > 1m)
            return Result.Failure(TradingDomainErrors.Account.PayoutPercentOutOfRange);

        Name = trimmedName;
        Broker = trimmedBroker;
        Currency = currencyResult.Value.Code;
        Leverage = leverage;
        PayoutPercent = payoutPercent;
        Touch();

        RaiseDomainEvent(new AccountUpdatedDomainEvent(
            Id, UserId, UpdatedAt!.Value));

        return Result.Success();
    }

    /// <summary>Desactiva la cuenta. Falla si ya estaba inactive.</summary>
    public Result Deactivate(IClock clock)
    {
        if (!IsActive)
            return Result.Failure(TradingDomainErrors.Account.AlreadyInactive);

        IsActive = false;
        Touch();

        RaiseDomainEvent(new AccountDeactivatedDomainEvent(
            Id, UserId, clock.UtcNow));

        return Result.Success();
    }

    /// <summary>Reactiva una cuenta previamente desactivada. Falla si ya estaba active.</summary>
    public Result Reactivate(IClock clock)
    {
        if (IsActive)
            return Result.Failure(TradingDomainErrors.Account.AlreadyActive);

        IsActive = true;
        Touch();

        RaiseDomainEvent(new AccountReactivatedDomainEvent(
            Id, UserId, clock.UtcNow));

        return Result.Success();
    }
}
