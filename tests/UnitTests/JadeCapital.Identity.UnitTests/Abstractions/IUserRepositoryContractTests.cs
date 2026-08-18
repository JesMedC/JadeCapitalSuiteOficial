using System.Reflection;
using FluentAssertions;
using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Identity.UnitTests.Abstractions;

/// <summary>
/// Wave 7 slice 7a.1 — <see cref="IUserRepository"/> contract surgery.
///
/// <para>
/// Slice 7a.1 adds a <c>DeleteAsync(User, CancellationToken)</c> method to
/// the interface. The contract is:
/// </para>
/// <list type="bullet">
///   <item>Canonical User mutation surface is <c>User.Cancel(reason)</c>
///   + <c>User.AssignToTenant(tenantId)</c> / <c>User.ReassignToTenantByAdmin(tenantId)</c>.
///   There is NO domain op that hard-deletes a User.</item>
///   <item>The decorator (<c>UserAuditDecorator</c>) emits
///   <see cref="Audit.AuditAction.Failed"/> audit row + re-throws
///   <see cref="NotSupportedException"/> BEFORE the inner is reached.</item>
///   <item>The inner (<c>UserRepository</c>) is a defensive STUB that throws
///   <see cref="NotSupportedException"/> with the canonical message
///   <c>"User deletion happens via Tenant reassignment, not direct delete"</c>
///   so the failure mode is identical whether the caller accidentally bypasses
///   the decorator OR the decorator is misconfigured.</item>
/// </list>
///
/// Two RED scenarios pinned here:
/// <list type="number">
///   <item><c>IUserRepository.DeleteAsync(User, CancellationToken)</c> method
///         exists on the interface (reflection check) with the correct
///         return type <see cref="Task"/>.</item>
///   <item>The concrete <c>UserRepository.DeleteAsync(User, CancellationToken)</c>
///         STUB throws <see cref="NotSupportedException"/> with the canonical
///         message that mentions <c>"Tenant reassignment"</c>.</item>
/// </list>
/// </summary>
public class IUserRepositoryContractTests
{
    [Fact]
    public void IUserRepository_Exposes_DeleteAsyncMethod()
    {
        // Phase 2 #1: DeleteAsync(User, CancellationToken) exists on the interface.
        // Reflection is the right tool here — the interface contract is what
        // production code binds to at runtime; the method must be present.
        var method = typeof(IUserRepository).GetMethod(
            "DeleteAsync",
            new[] { typeof(User), typeof(CancellationToken) });

        method.Should().NotBeNull(
            "IUserRepository must expose DeleteAsync(User, CancellationToken) " +
            "as part of the Slice 7a.1 surface surgery.");
        method!.ReturnType.Should().Be<Task>(
            "DeleteAsync is a Task-returning async method (no result payload).");
    }

    [Fact]
    public async Task UserRepository_DeleteAsync_ThrowsNotSupportedException_WithCanonicalMessage()
    {
        // Phase 2 #2: the concrete UserRepository stub throws with a message
        // containing "Tenant reassignment" so the UserAuditDecorator can
        // capture the message verbatim in the audit row's ChangesJson
        // (the decorator short-circuits BEFORE this stub is reached, but
        // the inner must also throw — defense-in-depth).
        //
        // The DeleteAsync stub does NOT touch the DbContext — it throws
        // immediately. Passing a null IdentityDbContext is therefore safe
        // and keeps the contract test focused on the throw semantics
        // (the SQLite-in-memory wiring lives in UserRepositoryIntegrationTests).
        var userRepo = new UserRepository(db: null!);

        var user = User.Register(
            Guid.NewGuid(), "x@y.z", "Display Name", "hash", UserRole.Trader).Value;

        var act = async () => await userRepo.DeleteAsync(user, CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Tenant reassignment*");
    }
}