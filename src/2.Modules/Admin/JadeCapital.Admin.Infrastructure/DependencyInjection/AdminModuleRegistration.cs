using JadeCapital.Admin.Application.Abstractions;
using JadeCapital.Admin.Application.Features.Audit;
using JadeCapital.Admin.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Admin.Infrastructure.DependencyInjection;

/// <summary>
/// Composition surface for the Admin module (Wave 9, slice 9b.1 +
/// Wave 0 slice 0f).
///
/// <para>
/// Slice 0f wires the existing Admin.Api endpoints (admin subscriptions).
/// Slice 9b.1 adds:
/// <list type="bullet">
///   <item><see cref="IAuditEventQueryStore"/> → <see cref="AuditEventQueryStore"/>
///         (Scoped — wraps <c>AuditDbContext</c> from
///         <c>Identity.Infrastructure</c>).</item>
///   <item><see cref="ListAuditEventsHandler"/> — MediatR-resolved handler
///         for the admin audit query endpoint.</item>
/// </list>
/// </para>
///
/// <para>
/// MediatR also auto-resolves the handler via assembly scanning (Program.cs
/// registers the Admin.Application assembly). The explicit
/// <c>AddScoped</c> line below gives DI an entry that takes precedence
/// over the assembly scan (matches the Wave 6 6d.2 Identity handler
/// registration pattern).
/// </para>
/// </summary>
public static class AdminModuleRegistration
{
    public static IServiceCollection AddAdminInfrastructure(this IServiceCollection services)
    {
        // ===== Wave 9 slice 9b.1 — Audit query side =====
        services.AddScoped<IAuditEventQueryStore, AuditEventQueryStore>();
        services.AddScoped<ListAuditEventsHandler>();

        return services;
    }
}