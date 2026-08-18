using System.Text.Json;
using FluentAssertions;
using JadeCapital.Shared.Kernel.MultiTenancy;

namespace JadeCapital.Shared.Kernel.UnitTests.MultiTenancy;

/// <summary>
/// Contract tests for <see cref="TenantId"/> (Wave 6, slice 6c.1).
///
/// <para>
/// Strongly-typed Guid wrapper that prevents accidental Guid/TenantId
/// confusion in handler signatures. Five RED scenarios pinned here:
/// </para>
/// <list type="number">
///   <item>Construct from an inner Guid value</item>
///   <item>Equality is value-based (two wrappers over the same Guid are equal)</item>
///   <item><see cref="TenantId.Empty"/> sentinel matches <see cref="Guid.Empty"/></item>
///   <item><see cref="TenantId.ToString"/> round-trips back to the inner Guid</item>
///   <item>JSON contract is a single <c>value</c> property (snake_case)</item>
/// </list>
///
/// <para>
/// Changing these semantics is a breaking change for the multi-tenant
/// persistence layer (migration 0024 stores <c>tenants.id</c> as a raw UUID,
/// and the handler signature uses <c>TenantId</c> not <c>Guid</c>).
/// </para>
/// </summary>
public class TenantIdTests
{
    [Fact]
    public void Constructor_StoresInnerGuid()
    {
        var inner = Guid.NewGuid();
        var sut = new TenantId(inner);

        sut.Value.Should().Be(inner);
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        var inner = Guid.NewGuid();
        var a = new TenantId(inner);
        var b = new TenantId(inner);

        // Positive: same Guid → equal.
        (a == b).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());

        // Negative: different Guid → not equal.
        var c = new TenantId(Guid.NewGuid());
        (a == c).Should().BeFalse();
        a.Equals(c).Should().BeFalse();
    }

    [Fact]
    public void Empty_Sentinel_MapsToGuidEmpty()
    {
        TenantId.Empty.Value.Should().Be(Guid.Empty);
    }

    [Fact]
    public void ToString_RoundTrips_ToInnerGuid()
    {
        var inner = Guid.NewGuid();
        var sut = new TenantId(inner);

        var roundTripped = Guid.Parse(sut.ToString());
        roundTripped.Should().Be(inner);
    }

    [Fact]
    public void JsonContract_ExposesValueProperty()
    {
        var inner = Guid.NewGuid();
        var sut = new TenantId(inner);

        var json = JsonSerializer.Serialize(sut, SnakeCase);

        // Snake_case lower: {"value":"..."}
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("value", out var valueElement).Should().BeTrue();
        valueElement.GetGuid().Should().Be(inner);
    }

    private static readonly JsonSerializerOptions SnakeCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}
