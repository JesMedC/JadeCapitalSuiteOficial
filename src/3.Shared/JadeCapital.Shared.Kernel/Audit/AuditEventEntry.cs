namespace JadeCapital.Shared.Kernel.Audit;

/// <summary>
/// Audit event entry record (Wave 6, slice 6d.1).
///
/// <para>
/// The transport shape passed to <see cref="IAuditLogger.LogAsync"/>. The
/// 6d.2 <c>AuditLogger</c> impl enriches the entry from
/// <c>ITenantContext.Current</c> + <c>ICurrentUserId</c> when null, then
/// maps it to an <c>AuditEvent</c> aggregate (append-only) via
/// <c>AuditEvent.Create</c>.
/// </para>
///
/// <para>
/// <b>Field semantics</b>:
/// <list type="bullet">
///   <item><see cref="EntityType"/> — short string identifying the entity
///         (e.g. "ImportJob", "Tenant"). Length 1..80 enforced at the
///         <c>AuditEvent</c> aggregate level.</item>
///   <item><see cref="EntityId"/> — Guid of the entity being audited.</item>
///   <item><see cref="Action"/> — Created/Updated/Deleted/Restored.</item>
///   <item><see cref="TenantId"/> — tenant scope. May be null for
///         cross-tenant admin operations; the 6d.2 logger enriches from
///         <c>ITenantContext.Current</c> when null.</item>
///   <item><see cref="UserId"/> — actor. May be null for system actors
///         (e.g. webhook handlers); the 6d.2 logger enriches from
///         <c>ICurrentUserId</c> when null.</item>
///   <item><see cref="ChangesJson"/> — JSONB diff payload. Format:
///         <c>{ "field": { "before": ..., "after": ... } }</c>. Null for
///         Created (no before-state).</item>
///   <item><see cref="OccurredAt"/> — UTC timestamp; the 6d.2 logger
///         overrides with the IClock-injected value for testability.</item>
/// </list>
/// </para>
/// </summary>
public sealed record AuditEventEntry(
    string EntityType,
    Guid EntityId,
    AuditAction Action,
    Guid? TenantId,
    Guid? UserId,
    string? ChangesJson,
    DateTimeOffset OccurredAt);
