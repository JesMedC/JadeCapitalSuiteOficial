using JadeCapital.Shared.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Instruments;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Audit;

/// <summary>
/// Per-aggregate audit decorator for <see cref="IInstrumentRepository"/>
/// (Wave 8, slice 8a.1).
///
/// <para>
/// Co-located with the <see cref="Instrument"/> aggregate (Trading
/// bounded context) so the decorator can use the Trading-bounded
/// <see cref="DbContext"/> for EF <c>ChangeTracker.OriginalValues</c>
/// — the pre-mutation diff source. Putting it in
/// <c>Trading.Infrastructure</c> avoids an Identity.Infrastructure →
/// Trading.Infrastructure → Identity.Infrastructure circular dep
/// that would arise if the decorator lived in Identity.Infrastructure
/// (which already references <c>DecoratedRepository&lt;T&gt;</c> for Tenant).
/// </para>
/// <list type="bullet">
///   <item>Forwarding <see cref="IInstrumentRepository.FindByIdAsync"/> +
///         <see cref="IInstrumentRepository.GetByIdAsync"/> +
///         <see cref="IInstrumentRepository.FindBySymbolAsync"/> +
///         <see cref="IInstrumentRepository.ListActiveAsync"/> +
///         <see cref="IInstrumentRepository.ListAllAsync"/> to the inner
///         without audit logging. Reads never log audit events
///         (matches the Wave 6 + 7a.1 + 7b.1 precedent).</item>
///   <item>Wrapping <see cref="IInstrumentRepository.AddAsync"/> +
///         <see cref="IInstrumentRepository.UpdateAsync"/> +
///         <see cref="IInstrumentRepository.DeleteAsync"/> with audit logging
///         via the generic <see cref="DecoratedRepository{T}"/> core
///         (the slice 8a.1 <c>IRepository&lt;Instrument&gt;</c> extension +
///         <c>RemoveAsync</c> → <c>DeleteAsync</c> rename make the
///         interface fit the canonical generic CRUD surface).</item>
///   <item><b>NO cross-tenant <c>IsOwner</c> check</b> on
///         <see cref="IInstrumentRepository.UpdateAsync"/>. <see cref="Instrument"/>
///         is a CATALOG ENTITY ("NO es Aggregate Root: es una Entity
///         compartida por todos los usuarios" — <c>Instrument.cs</c>
///         docstring). The catalog is shared across all users; admin
///         mutations on the catalog are legitimate. <see cref="Instrument"/>
///         carries NO <c>UserId</c> FK — the cross-tenant check is
///         inapplicable. Mirrors the Wave 6 <c>TenantAuditDecorator</c>
///         precedent: Tenant IS the tenant boundary; Instrument is catalog
///         data, not user-scoped.</item>
/// </list>
///
/// <para>
/// <b>DbContext parameter type</b>: the decorator accepts
/// <see cref="DbContext"/> (base type) rather than the concrete
/// <see cref="Persistence.TradingDbContext"/> so the unit-test fixture
/// can register a SQLite-compatible helper DbContext (the full
/// <c>TradingDbContext</c> carries Npgsql-specific array mappings for
/// <c>JournalEntry.Tags</c> that fail to compose on SQLite). In production
/// DI, the registered <c>TradingDbContext</c> is resolved into the
/// <see cref="DbContext"/> parameter.
/// </para>
///
/// <para>
/// Registered via Scrutor:
/// <c>services.Decorate&lt;IInstrumentRepository, InstrumentAuditDecorator&gt;()</c>
/// in <see cref="DependencyInjection.TradingModuleRegistration"/>.
/// </para>
/// </summary>
public sealed class InstrumentAuditDecorator : IInstrumentRepository
{
    private readonly IInstrumentRepository _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly DecoratedRepository<Instrument> _decorated;

    public InstrumentAuditDecorator(
        IInstrumentRepository inner,
        DbContext db,
        IAuditLogger audit,
        ITenantContext tenant,
        IClock clock)
    {
        _inner = inner;
        _audit = audit;
        _tenant = tenant;
        _clock = clock;
        _decorated = new DecoratedRepository<Instrument>(inner, audit, tenant, clock, db: db);
    }

    // ===== Read methods — no audit logging (matches Wave 6 + 7 precedent) =====

    public Task<Instrument?> FindByIdAsync(Guid id, CancellationToken ct)
        => _inner.FindByIdAsync(id, ct);

    public Task<Instrument?> GetByIdAsync(Guid id, CancellationToken ct)
        => _inner.GetByIdAsync(id, ct);

    public Task<Instrument?> FindBySymbolAsync(string symbol, CancellationToken ct)
        => _inner.FindBySymbolAsync(symbol, ct);

    public Task<IReadOnlyList<Instrument>> ListActiveAsync(CancellationToken ct)
        => _inner.ListActiveAsync(ct);

    public Task<IReadOnlyList<Instrument>> ListAllAsync(CancellationToken ct)
        => _inner.ListAllAsync(ct);

    // ===== Mutation methods with audit logging =====

    public Task AddAsync(Instrument instrument, CancellationToken ct)
        => _decorated.AddAsync(instrument, ct);

    /// <summary>
    /// NO <c>IsOwner</c> check — Instrument is a shared catalog entity.
    /// Mirrors the Wave 6 TenantAuditDecorator precedent: Tenant IS the
    /// tenant boundary; Instrument is catalog data, not user-scoped.
    /// </summary>
    public Task UpdateAsync(Instrument instrument, CancellationToken ct)
        => _decorated.UpdateAsync(instrument, ct);

    public Task DeleteAsync(Instrument instrument, CancellationToken ct)
        => _decorated.DeleteAsync(instrument, ct);
}