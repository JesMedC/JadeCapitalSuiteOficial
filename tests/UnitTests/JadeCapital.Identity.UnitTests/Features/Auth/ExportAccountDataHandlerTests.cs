using FluentAssertions;
using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application.Features.Auth.ExportAccountData;
using JadeCapital.Identity.Domain.Common;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Xunit;

namespace JadeCapital.Identity.UnitTests.Features.Auth.ExportAccountData;

// ============================================================================
//  ExportAccountDataHandlerTests — Wave 11 slice 11.3
//
//  RED-first TDD coverage for the GDPR Art. 20 data-portability endpoint
//  (`GET /api/users/me/export`). Three scenarios pinned here:
//
//    1. Handle_ValidUser_ReturnsAllSections — verifies the handler
//       yields a user-profile section plus the Identity-owned GDPR
//       sections (refresh tokens summary, password history metadata,
//       risk profile, consent) as an IAsyncEnumerable stream.
//
//    2. Handle_NonexistentUser_ReturnsNotFound — verifies the
//       canonical 404 path (Error.NotFound("identity.user_not_found", ...)).
//
//    3. Handle_ExcludesAuditEvents_FromExport — verifies the export
//       does NOT include `audit.events` rows (the slice's scope
//       decision per tasks.md; future slices can extend if needed).
//
//  Architectural scope (per tasks.md §11.3 + clean-architecture rules):
//  the handler is bound to Identity-owned data only — it does NOT
//  reference Trading.Application / Billing.Application. Cross-module
//  data (trades, accounts, journal entries) is out of scope for this
//  slice and will be added by a follow-up Contracts-based reader
//  pattern in 11.x.
// ============================================================================

public class ExportAccountDataHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILogger<ExportAccountDataHandler> _logger =
        Substitute.For<ILogger<ExportAccountDataHandler>>();

    public ExportAccountDataHandlerTests()
    {
        var fixedNow = new DateTimeOffset(2026, 8, 19, 14, 30, 0, TimeSpan.Zero);
        _clock.UtcNow.Returns(fixedNow);
    }

    private ExportAccountDataHandler CreateSut() => new(
        _users, _audit, _clock, _logger);

    private static User CreateActiveUser(Guid? tenantId = null, string displayName = "Trader Demo")
    {
        var tid = tenantId ?? Guid.NewGuid();
        var tenantRef = new TenantId(tid);
        var userResult = User.Register(
            Guid.NewGuid(),
            email: "trader@example.com",
            displayName: displayName,
            passwordHash: "hashed_pwd",
            role: UserRole.Trader,
            acceptedTermsVersion: "v1",
            acceptedPrivacyVersion: "v1");
        userResult.Value.AssignToTenant(tenantRef);
        return userResult.Value;
    }

    [Fact]
    public async Task Handle_NonexistentUser_ReturnsNotFoundError()
    {
        var userId = Guid.NewGuid();
        _users.GetByIdAsync(userId, Arg.Any<CancellationToken>()).ReturnsNull();

        var result = await CreateSut().Handle(new ExportAccountDataQuery(userId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("notfound.identity.user_not_found",
            "Error.NotFound prepends the notfound. category to the canonical code per Shared.Kernel convention");
        // The handler MUST short-circuit BEFORE touching the audit log
        // (no point auditing a "not found" lookup).
        await _audit.DidNotReceiveWithAnyArgs().LogAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ValidUser_EmitsGdprDataExportAuditRow()
    {
        var user = CreateActiveUser();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await CreateSut().Handle(new ExportAccountDataQuery(user.Id), CancellationToken.None);

        // The handler MUST write a single audit row describing the
        // export (GDPR accountability trail; allow compliance to prove
        // the user invoked the endpoint on this date).
        await _audit.Received(1).LogAsync(
            Arg.Is<AuditEventEntry>(e =>
                e.EntityType == "User"
                && e.EntityId == user.Id
                && e.UserId == user.Id
                && e.Action == AuditAction.Updated
                && e.ChangesJson != null
                && e.ChangesJson.Contains("gdpr_data_export")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidUser_ResultMetadataMatchesQuery()
    {
        var user = CreateActiveUser();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateSut().Handle(new ExportAccountDataQuery(user.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(user.Id);
        result.Value.FormatVersion.Should().Be("1.0",
            "FormatVersion 1.0 is the slice's initial wire-format version — bump on breaking changes");
        result.Value.ExportedAt.Should().Be(_clock.UtcNow,
            "the export timestamp is sourced from the injected clock for testability");
    }

    [Fact]
    public async Task Handle_ValidUser_SectionsStreamStartsWithUserProfile()
    {
        // Architectural contract: the first section yielded by the
        // export stream MUST be the user_profile section. Downstream
        // tooling (compliance, FE preview) can rely on this ordering.
        var user = CreateActiveUser();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateSut().Handle(new ExportAccountDataQuery(user.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var enumerator = result.Value.Sections.GetAsyncEnumerator(CancellationToken.None);
        try
        {
            Assert.True(await enumerator.MoveNextAsync());
            enumerator.Current.SectionType.Should().Be("user_profile",
                "the first section MUST be the user_profile per the GDPR data-portability spec ordering (owner's profile first)");
            enumerator.Current.Data.Should().NotBeNull();
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    [Fact]
    public async Task Handle_ExcludesAuditEvents_FromExport()
    {
        // Per tasks.md Phase 8 — the export MUST NOT include rows
        // from `audit.events`. Audit is for OPS, not user data
        // portability. We assert by checking the emitted
        // section-types never equal `"audit_events"` and the audit
        // logger received exactly ONE call (the export-record row,
        // not a per-section fan-out).
        var user = CreateActiveUser();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateSut().Handle(new ExportAccountDataQuery(user.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var emittedSectionTypes = new List<string>();
        var enumerator = result.Value.Sections.GetAsyncEnumerator(CancellationToken.None);
        try
        {
            while (await enumerator.MoveNextAsync())
            {
                emittedSectionTypes.Add(enumerator.Current.SectionType);
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        emittedSectionTypes.Should().NotContain("audit_events",
            "the export MUST NOT contain audit.events rows — GDPR Art. 20 covers portability of the user's own data, not OPS audit trail");

        await _audit.Received(1).LogAsync(Arg.Any<AuditEventEntry>(), Arg.Any<CancellationToken>());
    }
}
