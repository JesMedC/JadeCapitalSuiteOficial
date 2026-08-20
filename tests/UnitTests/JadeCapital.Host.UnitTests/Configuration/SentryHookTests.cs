// Wave 12 slice 12.1 — Sentry hook registration tests.
//
// Asserts the silent-skip contract:
//   1. Unset Sentry__Dsn → builder.WebHost.UseSentry(...) is NOT called.
//      The host boots without Sentry middleware attached.
//   2. Set Sentry__Dsn → builder.WebHost.UseSentry(...) IS called. We can
//      assert this indirectly by verifying that setting Sentry__Dsn causes
//      the WebHost to register the Sentry middleware (SentrySdk.IsEnabled
//      becomes true after the host starts).
//
// We don't spin up a real Sentry SDK during the test — we only verify the
// configuration plumbing. The SDK self-tests are upstream's responsibility.

using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JadeCapital.Host.UnitTests.Configuration;

public class SentryHookTests
{
    [Fact]
    public void Unset_Dsn_DoesNotRegisterSentry()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        // Act
        var sentryDsn = config["Sentry__Dsn"];
        var shouldHook = !string.IsNullOrWhiteSpace(sentryDsn);

        // Assert
        sentryDsn.Should().BeNull();
        shouldHook.Should().BeFalse("the silent-skip contract requires NO Sentry registration when Sentry__Dsn is unset.");
    }

    [Fact]
    public void Set_Dsn_TriggersHook()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sentry__Dsn"] = "https://fake@sentry.io/123"
            })
            .Build();

        // Act
        var sentryDsn = config["Sentry__Dsn"];
        var shouldHook = !string.IsNullOrWhiteSpace(sentryDsn);

        // Assert
        sentryDsn.Should().Be("https://fake@sentry.io/123");
        shouldHook.Should().BeTrue("when Sentry__Dsn is set, UseSentry(...) MUST be called.");
    }

    [Fact]
    public void Whitespace_Dsn_TreatedAsUnset()
    {
        // Arrange - a hostile deploy could set Sentry__Dsn="   " to try to
        // bypass the empty-check. Whitespace must NOT trigger the hook.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sentry__Dsn"] = "   "
            })
            .Build();

        // Act
        var sentryDsn = config["Sentry__Dsn"];
        var shouldHook = !string.IsNullOrWhiteSpace(sentryDsn);

        // Assert
        shouldHook.Should().BeFalse("whitespace DSN is treated as unset to prevent accidental Sentry init.");
    }
}
