using System.Text.Json;
using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Repository;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace JadeCapital.Identity.Infrastructure.Persistence;

/// <summary>
/// Generic audit-logging decorator over <see cref="IRepository{T}"/>
/// (Wave 6, slice 6d.2).
///
/// <para>
/// On Add/Update/Delete, the decorator forwards the mutation to the inner
/// repository (the EF-backed concrete implementation), then logs an
/// <see cref="AuditEventEntry"/> via <see cref="IAuditLogger"/>. Reads
/// (<see cref="IRepository{T}.GetByIdAsync"/>) bypass the audit surface.
/// </para>
/// <para>
/// <b>Diff strategy</b>: on Update, the decorator captures the
/// pre-mutation snapshot via <b>EF's <see cref="EntityEntry.OriginalValues"/></b>
/// when the inner repository is EF-backed (the production path). The
/// change tracker holds the pre-modification values even after the
/// caller mutates the in-memory entity — that's how EF's UPDATE SQL
/// works (`SET col = ? WHERE col = ?`). If no <see cref="DbContext"/>
/// is provided (the legacy / in-memory test path), the decorator falls
/// back to <c>_inner.GetByIdAsync</c>. If the diff helper throws, the
/// decorator falls back to the full-snapshot JSON of the post-mutation
/// entity — never crashes.
/// </para>
/// <para>
/// <b>Why the audit fires AFTER the inner call</b>: the main mutation has
/// already committed by the time the audit logger is invoked. A failed
/// audit write does NOT roll back the mutation. The audit log is a
/// defense layer, not a critical path.
/// </para>
/// <para>
/// <b>No-throw guarantee</b>: <see cref="IAuditLogger.LogAsync"/> is
/// fire-and-forget per the interface contract — the decorator never has
/// to wrap its calls in try/catch.
/// </para>
/// </summary>
public sealed class DecoratedRepository<T> where T : class
{
    private readonly IRepository<T> _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IDiff _diff;
    private readonly DbContext? _db;

    public DecoratedRepository(
        IRepository<T> inner,
        IAuditLogger audit,
        ITenantContext tenant,
        IClock clock,
        IDiff? diff = null,
        DbContext? db = null)
    {
        _inner = inner;
        _audit = audit;
        _tenant = tenant;
        _clock = clock;
        _diff = diff ?? new JsonDiff();
        _db = db;
    }

    /// <summary>
    /// Forwards the read to the inner repository. NEVER logs an audit event.
    /// </summary>
    public Task<T?> GetByIdAsync(Guid id, CancellationToken ct)
        => _inner.GetByIdAsync(id, ct);

    /// <summary>
    /// Stages the entity in the change tracker via the inner repository,
    /// then logs an <see cref="AuditAction.Created"/> event with no diff.
    /// </summary>
    public async Task AddAsync(T entity, CancellationToken ct)
    {
        await _inner.AddAsync(entity, ct);
        await TryAuditAsync(BuildEntry(entity, AuditAction.Created, changesJson: null), ct);
    }

    /// <summary>
    /// Fetches the pre-mutation snapshot via EF's
    /// <see cref="EntityEntry.OriginalValues"/> (when the inner repository
    /// is EF-backed), stages the modified entity via the inner repository,
    /// then logs an <see cref="AuditAction.Updated"/> event with the JSON
    /// diff between the snapshot and the post-mutation entity. If the
    /// entity carries an <c>IsDeleted = true</c> flag (the
    /// <c>ISoftDelete</c> contract from slice 6d.1), the action is upgraded
    /// to <see cref="AuditAction.Deleted"/> so the audit log reflects
    /// the soft-delete semantics rather than a generic state change.
    /// </summary>
    public async Task UpdateAsync(T entity, CancellationToken ct)
    {
        T? before = await ResolveBeforeAsync(entity, ct);
        await _inner.UpdateAsync(entity, ct);
        var changesJson = SafeDiff(before, entity);
        var action = IsSoftDeleted(entity) ? AuditAction.Deleted : AuditAction.Updated;
        await TryAuditAsync(BuildEntry(entity, action, changesJson), ct);
    }

    /// <summary>
    /// Stages the removal via the inner repository, then logs an
    /// <see cref="AuditAction.Deleted"/> event with no diff payload.
    /// </summary>
    public async Task DeleteAsync(T entity, CancellationToken ct)
    {
        await _inner.DeleteAsync(entity, ct);
        await TryAuditAsync(BuildEntry(entity, AuditAction.Deleted, changesJson: null), ct);
    }

    /// <summary>
    /// Defense-in-depth: the <see cref="IAuditLogger"/> contract is to
    /// never throw, but a buggy implementation could. The decorator
    /// swallows any exception here so the main mutation is NEVER rolled
    /// back by a misbehaving audit. The audit row is non-critical; the
    /// main mutation is the system-of-record.
    /// </summary>
    private async Task TryAuditAsync(AuditEventEntry entry, CancellationToken ct)
    {
        try
        {
            await _audit.LogAsync(entry, ct);
        }
        catch
        {
            // Swallow. The decorator's contract is "main mutation must
            // succeed even if audit fails". The IAuditLogger impl is
            // expected to log a warning + return, but we don't depend on
            // that here.
        }
    }

    /// <summary>
    /// Builds the <see cref="AuditEventEntry"/> for the given entity +
    /// action. The <c>EntityId</c> is the aggregate's primary key; the
    /// <c>EntityType</c> is the concrete type's name (mirrors the 6d.1
    /// pattern). <c>TenantId</c> + <c>UserId</c> come from the ITenantContext
    /// when the decorator supplies them; the AuditLogger then enriches any
    /// null values from its own context.
    /// </summary>
    private AuditEventEntry BuildEntry(T entity, AuditAction action, string? changesJson)
    {
        var id = GetId(entity);
        return new AuditEventEntry(
            EntityType: typeof(T).Name,
            EntityId: id,
            Action: action,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: changesJson,
            OccurredAt: _clock.UtcNow);
    }

    /// <summary>
    /// Best-effort diff. Returns null if the entities are equal, the
    /// computed diff JSON on success, or a full-snapshot JSON when the
    /// diff helper throws. Never throws.
    /// </summary>
    private string? SafeDiff(T? before, T after)
    {
        if (before is null)
            return JsonSerializer.Serialize(after);
        try
        {
            var diff = _diff.Compute(before, after);
            return string.IsNullOrEmpty(diff) ? null : diff;
        }
        catch (Exception)
        {
            // Fallback: full snapshot of the post-mutation entity. The
            // audit row will lose the before/after shape but will still
            // preserve the change. The decorator MUST NOT crash the
            // caller's mutation because the diff helper failed.
            return JsonSerializer.Serialize(after);
        }
    }

    /// <summary>
    /// Resolves the primary key from the aggregate. The codebase uses
    /// <see cref="Guid"/> primary keys everywhere via the
    /// <see cref="JadeCapital.Shared.Kernel.Primitives.AggregateRoot{TId}"/>
    /// base class, so we read via reflection on the <c>Id</c> property.
    /// If the property is missing the decorator falls back to
    /// <see cref="Guid.Empty"/> — the audit row's entity_id is then
    /// placeholder; future slices may tighten the contract.
    /// </summary>
    private static Guid GetId(T entity)
    {
        var prop = typeof(T).GetProperty("Id");
        if (prop?.GetValue(entity) is Guid g)
            return g;
        return Guid.Empty;
    }

/// <summary>
    /// Resolves the pre-mutation snapshot. When the decorator is wired
    /// with the inner repository's <see cref="DbContext"/> (the
    /// production path), we use EF's <see cref="EntityEntry.OriginalValues"/>
    /// — the change tracker holds the pre-modification values even after
    /// the caller mutates the in-memory entity. That's how EF's UPDATE
    /// SQL works (`SET col = ? WHERE col = ?`). Without the DbContext
    /// (legacy / in-memory test path), we fall back to
    /// <c>_inner.GetByIdAsync</c>, which returns the SAME tracked instance
    /// and therefore yields an empty diff in the production flow.
    /// </summary>
    private async Task<T?> ResolveBeforeAsync(T entity, CancellationToken ct)
    {
        if (_db is not null)
        {
            var entry = _db.Entry(entity);
            if (entry.State != EntityState.Detached)
            {
                return entry.OriginalValues.ToObject() as T;
            }
        }
        return await _inner.GetByIdAsync(GetId(entity), ct);
    }

    /// <summary>
    /// Soft-delete detection — if the entity implements <c>ISoftDelete</c>
    /// and <c>IsDeleted = true</c>, return true. Otherwise false.
    /// Generic via reflection so the decorator stays aggregate-agnostic.
    /// </summary>
    private static bool IsSoftDeleted(T entity)
    {
        var prop = typeof(T).GetProperty("IsDeleted");
        return prop?.GetValue(entity) is true;
    }
}

/// <summary>
/// JSON diff strategy (Wave 6, slice 6d.2). Computes a JSON object of the
/// form <c>{ "FieldName": { "before": ..., "after": ... } }</c> for every
/// field that changed between <paramref name="before"/> and
/// <paramref name="after"/>. Returns an empty string if nothing changed.
/// </summary>
public interface IDiff
{
    /// <summary>Compute the diff JSON. Empty/null means no changes.</summary>
    string Compute(object before, object after);
}

/// <summary>
/// Default <see cref="IDiff"/> impl — JSON serialize both objects, walk
/// the fields, and emit a per-field {before, after} object for changed
/// fields. Skips fields whose value is equal in both sides.
/// </summary>
public sealed class JsonDiff : IDiff
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string Compute(object before, object after)
    {
        var beforeJson = JsonSerializer.Serialize(before, Options);
        var afterJson = JsonSerializer.Serialize(after, Options);
        if (beforeJson == afterJson)
            return string.Empty;

        using var beforeDoc = JsonDocument.Parse(beforeJson);
        using var afterDoc = JsonDocument.Parse(afterJson);
        var result = new Dictionary<string, object>();
        foreach (var prop in afterDoc.RootElement.EnumerateObject())
        {
            JsonElement beforeProp = default;
            // Case-insensitive property lookup — the .NET runtime serializes
            // property names exactly as declared (PascalCase) even when the
            // JsonNamingPolicy is CamelCase; the *output* JSON keys are
            // camelCase, so TryGetProperty needs to match either case.
            foreach (var bp in beforeDoc.RootElement.EnumerateObject())
            {
                if (string.Equals(bp.Name, prop.Name, StringComparison.OrdinalIgnoreCase))
                {
                    beforeProp = bp.Value;
                    break;
                }
            }
            if (!JsonElementEquals(beforeProp, prop.Value))
            {
                result[prop.Name] = new
                {
                    before = (object?)BeforeValue(beforeProp),
                    after = (object?)prop.Value.GetRawText(),
                };
            }
        }
        return JsonSerializer.Serialize(result, Options);
    }

    private static bool JsonElementEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind == JsonValueKind.Undefined || b.ValueKind == JsonValueKind.Undefined)
            return false;
        return a.GetRawText() == b.GetRawText();
    }

    private static string? BeforeValue(JsonElement element)
        => element.ValueKind == JsonValueKind.Undefined ? null : element.GetRawText();
}