namespace JadeCapital.Identity.Contracts.Projections;

/// <summary>
/// Narrow, read-only projection of a user that the Admin API is allowed to
/// inspect. Slice 0f of jade-trader-os-core-portals.
///
/// Invariants:
/// <list type="bullet">
///   <item>Exposes ONLY <see cref="Email"/> + <see cref="DisplayName"/> — the
///   two fields the Admin subscription surface needs to label each row and
///   detail view. NO role, status, lockout, password hash, session version,
///   or refresh-token hint MUST ever leak through this projection.</item>
///   <item>Identity.Contracts is shared across modules (Billing, Admin).
///   The interface deliberately does NOT depend on the Identity.Domain
///   <c>User</c> aggregate: the implementation lives in Identity.Infrastructure
///   and maps to the two properties only.</item>
///   <item>Mutations are forbidden by design. Admin API MUST NOT expose any
///   user-admin route (suspend, role change, impersonation, password reset)
///   through Wave 0; that is enforced at the endpoint surface, not here.</item>
/// </list>
///
/// The Admin authorization tests assert this narrowing via reflection so
/// accidental property additions are caught before they reach production.
/// </summary>
public interface IUserOwnerProjection
{
    /// <summary>User's email address (already normalized to lowercase by the
    /// Identity module before the projection is built).</summary>
    string Email { get; }

    /// <summary>User's display name (public, never a username or identifier).</summary>
    string DisplayName { get; }
}
