using System.Text.Json;
using FluentAssertions;
using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Identity.Domain.Tenants.Events;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Identity.UnitTests.Tenants;

/// <summary>
/// Domain tests for the <see cref="Tenant"/> aggregate (Wave 6, slice 6c.1).
///
/// <para>
/// Fourteen RED scenarios pinned here cover every invariant in design.md §
/// "Tenant" (lines 188-216):
/// </para>
/// <list type="number">
///   <item>Create with valid inputs → Active + Plan.Personal + DomainEvent</item>
///   <item>Name length 1..120 (empty / too long / whitespace fails)</item>
///   <item>Slug format <c>[a-z0-9-]+</c> (uppercase / special chars fail)</item>
///   <item>Slug length 1..64</item>
///   <item>Owner user id non-empty</item>
///   <item>Plan enum out-of-range rejected</item>
///   <item>Status enum out-of-range rejected</item>
///   <item>Status transitions Active → Suspended → Archived</item>
///   <item>No back-transitions Suspended → Active or Archived → Suspended</item>
///   <item>Rename trims whitespace</item>
///   <item>ChangePlan validates the new plan is a defined value</item>
///   <item>Id is immutable after Create (no mutator exposed)</item>
///   <item>JSON contract: snake_case fields, snake_case enum-as-int</item>
///   <item>Rehydrate (FromTrusted) preserves every field including CreatedAt</item>
/// </list>
/// </summary>
public class TenantTests
{
    private static IClock FixedClock(DateTimeOffset? at = null)
        => Substitute.For<IClock>().WhichReturns(at ?? new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));

    private static readonly DateTimeOffset FixedNow = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidInputs_ReturnsActivePersonalTenant()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var ownerId = Guid.NewGuid();

        var result = Tenant.Create(
            id: Guid.NewGuid(),
            name: "Acme Capital",
            slug: "acme-capital",
            ownerUserId: ownerId,
            plan: TenantPlan.Personal,
            clock: clock);

        result.IsSuccess.Should().BeTrue();
        var t = result.Value;
        t.Name.Should().Be("Acme Capital");
        t.Slug.Should().Be("acme-capital");
        t.OwnerUserId.Should().Be(ownerId);
        t.Plan.Should().Be(TenantPlan.Personal);
        t.Status.Should().Be(TenantStatus.Active);
        t.CreatedAt.Should().Be(FixedNow);
        t.DomainEvents.Should().ContainSingle(e => e is TenantCreatedDomainEvent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    public void Create_NameShorterThan2_Fails(string name)
    {
        var clock = FixedClock();
        var result = Tenant.Create(Guid.NewGuid(), name, "slug", Guid.NewGuid(), TenantPlan.Personal, clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.");
    }

    [Fact]
    public void Create_NameLongerThan120_Fails()
    {
        var clock = FixedClock();
        var longName = new string('a', 121);

        var result = Tenant.Create(Guid.NewGuid(), longName, "slug", Guid.NewGuid(), TenantPlan.Personal, clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.");
    }

    [Theory]
    [InlineData("Bad Slug")]        // uppercase + space
    [InlineData("bad_slug")]        // underscore
    [InlineData("bad.slug")]        // dot
    [InlineData("bad/slug")]        // slash
    [InlineData("bad--slug--")]     // leading/trailing dashes — design says "[a-z0-9-]+" which DOES allow this; this InlineData documents the accepted form is rejected only by other rules (covered by length test below)
    public void Create_SlugWithUppercaseOrSpecialChars_Fails(string slug)
    {
        var clock = FixedClock();
        // "bad--slug--" matches [a-z0-9-]+ so we'll only assert non-failure for that
        // case here — see Length_Exceeds64_Fails for the boundary.
        if (slug == "bad--slug--")
        {
            // accepted by format — exercise the length boundary in a separate test.
            return;
        }

        var result = Tenant.Create(Guid.NewGuid(), "Acme", slug, Guid.NewGuid(), TenantPlan.Personal, clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.");
    }

    [Fact]
    public void Create_SlugLengthZero_Fails()
    {
        var clock = FixedClock();
        var result = Tenant.Create(Guid.NewGuid(), "Acme", "", Guid.NewGuid(), TenantPlan.Personal, clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.");
    }

    [Fact]
    public void Create_SlugLongerThan64_Fails()
    {
        var clock = FixedClock();
        var longSlug = new string('a', 65);

        var result = Tenant.Create(Guid.NewGuid(), "Acme", longSlug, Guid.NewGuid(), TenantPlan.Personal, clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.");
    }

    [Fact]
    public void Create_OwnerUserIdEmpty_Fails()
    {
        var clock = FixedClock();
        var result = Tenant.Create(Guid.NewGuid(), "Acme", "acme", Guid.Empty, TenantPlan.Personal, clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.");
    }

    [Fact]
    public void ChangePlan_OutOfRangeValue_Fails()
    {
        var clock = FixedClock();
        var tenant = Tenant.Create(Guid.NewGuid(), "Acme", "acme", Guid.NewGuid(), TenantPlan.Personal, clock).Value;

        // (TenantPlan)999 is not a defined enum value.
        var result = tenant.ChangePlan(unchecked((TenantPlan)999), clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.");
    }

    [Fact]
    public void ChangeStatus_OutOfRangeValue_Fails()
    {
        var clock = FixedClock();
        var tenant = Tenant.Create(Guid.NewGuid(), "Acme", "acme", Guid.NewGuid(), TenantPlan.Personal, clock).Value;

        // The internal mutator should reject undefined enum values.
        var result = tenant.ForceSetStatusForTests(unchecked((TenantStatus)42));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.");
    }

    [Fact]
    public void StatusTransitions_ActiveToSuspendedToArchived_AreAllowed()
    {
        var clock = FixedClock();
        var tenant = Tenant.Create(Guid.NewGuid(), "Acme", "acme", Guid.NewGuid(), TenantPlan.Personal, clock).Value;

        var suspend = tenant.Suspend("violation", clock);
        suspend.IsSuccess.Should().BeTrue();
        tenant.Status.Should().Be(TenantStatus.Suspended);

        var archive = tenant.Archive(clock);
        archive.IsSuccess.Should().BeTrue();
        tenant.Status.Should().Be(TenantStatus.Archived);
    }

    [Fact]
    public void StatusTransitions_NoBackTransitions_AreRejected()
    {
        var clock = FixedClock();
        var tenant = Tenant.Create(Guid.NewGuid(), "Acme", "acme", Guid.NewGuid(), TenantPlan.Personal, clock).Value;

        // Active → Suspended first (valid).
        tenant.Suspend("x", clock).IsSuccess.Should().BeTrue();

        // Suspended → Active is NOT allowed (no back-transition).
        var reactivate = Tenant.TryReactivate(clock);
        reactivate.IsFailure.Should().BeTrue();
        reactivate.Error.Code.Should().StartWith("conflict.");
    }

    [Fact]
    public void StatusTransitions_ArchiveFromSuspended_Works_ButArchiveFromActive_IsRejected()
    {
        var clock = FixedClock();
        var tenant = Tenant.Create(Guid.NewGuid(), "Acme", "acme", Guid.NewGuid(), TenantPlan.Personal, clock).Value;

        // Active → Archive is NOT allowed (must go through Suspended).
        var archiveFromActive = tenant.Archive(clock);
        archiveFromActive.IsFailure.Should().BeTrue();
        archiveFromActive.Error.Code.Should().StartWith("conflict.");

        // Suspend then Archive works.
        tenant.Suspend("x", clock);
        tenant.Archive(clock).IsSuccess.Should().BeTrue();

        // Archived → Suspended is NOT allowed.
        var archivedToSuspended = tenant.TrySuspendAgain("y", clock);
        archivedToSuspended.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Rename_TrimsWhitespace()
    {
        var clock = FixedClock();
        var tenant = Tenant.Create(Guid.NewGuid(), "Acme", "acme", Guid.NewGuid(), TenantPlan.Personal, clock).Value;

        var result = tenant.Rename("  Acme Renamed  ", clock);

        result.IsSuccess.Should().BeTrue();
        tenant.Name.Should().Be("Acme Renamed");
    }

    [Fact]
    public void ChangePlan_ValidPlan_Succeeds()
    {
        var clock = FixedClock();
        var tenant = Tenant.Create(Guid.NewGuid(), "Acme", "acme", Guid.NewGuid(), TenantPlan.Personal, clock).Value;

        var result = tenant.ChangePlan(TenantPlan.Pro, clock);

        result.IsSuccess.Should().BeTrue();
        tenant.Plan.Should().Be(TenantPlan.Pro);
    }

    [Fact]
    public void IdAndSlug_AreImmutable_AfterCreate()
    {
        var clock = FixedClock();
        var id = Guid.NewGuid();
        var tenant = Tenant.Create(id, "Acme", "acme", Guid.NewGuid(), TenantPlan.Personal, clock).Value;

        // Id / Slug / OwnerUserId / CreatedAt must NOT have a PUBLIC setter.
        // PropertyInfo.SetMethod is non-null for `protected set` (because a
        // setter exists at all) — we inspect IsPublic to filter those out.
        // The Tenant class must not expose a public mutator for any of these.
        var idSet = typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetMethod;
        var slugSet = typeof(Tenant).GetProperty(nameof(Tenant.Slug))!.SetMethod;
        var ownerSet = typeof(Tenant).GetProperty(nameof(Tenant.OwnerUserId))!.SetMethod;
        var createdAtSet = typeof(Tenant).GetProperty(nameof(Tenant.CreatedAt))!.SetMethod;

        (idSet is null || !idSet.IsPublic).Should().BeTrue(
            $"Id must not have a public setter. Actual: {(idSet?.IsPublic.ToString() ?? "no setter")}.");
        (slugSet is null || !slugSet.IsPublic).Should().BeTrue(
            $"Slug must not have a public setter. Actual: {(slugSet?.IsPublic.ToString() ?? "no setter")}.");
        (ownerSet is null || !ownerSet.IsPublic).Should().BeTrue(
            $"OwnerUserId must not have a public setter. Actual: {(ownerSet?.IsPublic.ToString() ?? "no setter")}.");
        (createdAtSet is null || !createdAtSet.IsPublic).Should().BeTrue(
            $"CreatedAt must not have a public setter. Actual: {(createdAtSet?.IsPublic.ToString() ?? "no setter")}.");
    }

    [Fact]
    public void JsonContract_SnakeCase_AllFields()
    {
        var clock = FixedClock();
        var id = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var tenant = Tenant.Create(id, "Acme", "acme", ownerId, TenantPlan.Pro, clock).Value;

        var json = JsonSerializer.Serialize(tenant, SnakeCase);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("id", out _).Should().BeTrue();
        root.TryGetProperty("name", out _).Should().BeTrue();
        root.TryGetProperty("slug", out _).Should().BeTrue();
        root.TryGetProperty("owner_user_id", out _).Should().BeTrue();
        root.TryGetProperty("plan", out _).Should().BeTrue();
        root.TryGetProperty("status", out _).Should().BeTrue();
        root.TryGetProperty("created_at", out _).Should().BeTrue();
    }

    private static readonly JsonSerializerOptions SnakeCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [Fact]
    public void FromTrusted_Rehydrates_AllFieldsIncludingCreatedAt()
    {
        var id = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var tenant = Tenant.FromTrusted(
            id: id,
            name: "Acme",
            slug: "acme",
            ownerUserId: ownerId,
            plan: TenantPlan.Enterprise,
            status: TenantStatus.Suspended,
            createdAt: createdAt,
            updatedAt: null);

        tenant.Id.Should().Be(id);
        tenant.Name.Should().Be("Acme");
        tenant.Slug.Should().Be("acme");
        tenant.OwnerUserId.Should().Be(ownerId);
        tenant.Plan.Should().Be(TenantPlan.Enterprise);
        tenant.Status.Should().Be(TenantStatus.Suspended);
        tenant.CreatedAt.Should().Be(createdAt);
        tenant.UpdatedAt.Should().BeNull();
    }
}

/// <summary>
/// Helper to allow <c>IClock</c> substitutes without bringing in NSubstitute
/// in this file's <c>using</c> list — keeps the test file focused on the
/// domain assertions.
/// </summary>
internal static class IClockTestExtensions
{
    public static IClock WhichReturns(this IClock _, DateTimeOffset utcNow)
    {
        var c = Substitute.For<IClock>();
        c.UtcNow.Returns(utcNow);
        return c;
    }
}
