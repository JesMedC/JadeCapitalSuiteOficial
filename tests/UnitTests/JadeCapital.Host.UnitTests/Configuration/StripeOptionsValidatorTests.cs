// Wave 10 slice 10.6 - Unit tests for StripeOptionsValidator.
//
// RED scenarios (matches tasks.md Phase 8.1):
//   1. Validate_ProductionEnv_EmptyApiKey_Fails
//   2. Validate_ProductionEnv_NonLiveKey_Fails (sk_test_ in prod)
//   3. Validate_ProductionEnv_LiveKey_Passes (sk_live_ in prod)
//   4. Validate_ProductionEnv_MissingWebhookSecret_Fails
//   5. Validate_DevelopmentEnv_EmptyApiKey_Passes (falls back to StubStripeGateway)
//   6. Validate_DevelopmentEnv_TestKey_Passes
//   7. Validate_DevelopmentEnv_NonStandardKey_Fails (random string in dev)
//
// The validator dual-reads Stripe:ApiKey (canonical) AND Stripe:SecretKey
// (legacy docker-compose alias). This bridges an existing env-var binding
// mismatch: the compose file uses Stripe__SecretKey while StripeOptions
// (Billing) reads ApiKey. Failures surface the migration path.

using FluentAssertions;
using JadeCapital.Host.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace JadeCapital.Host.UnitTests.Configuration;

public class StripeOptionsValidatorTests : IDisposable
{
    private readonly string _originalEnv;

    public StripeOptionsValidatorTests()
    {
        _originalEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _originalEnv);
        GC.SuppressFinalize(this);
    }

    private static StripeOptionsValidator BuildValidator(IConfiguration? config = null)
    {
        return new StripeOptionsValidator(config ?? new ConfigurationBuilder().Build());
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    [Fact]
    public void Validate_ProductionEnv_EmptyApiKey_Fails()
    {
        // Arrange
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        var sut = BuildValidator(BuildConfig(new Dictionary<string, string?>
        {
            ["Stripe:ApiKey"] = "",
            ["Stripe:WebhookSecret"] = "whsec_xyz"
        }));

        // Act
        var result = sut.Validate("Stripe", new StripeOptions
        {
            ApiKey = string.Empty,
            WebhookSecret = "whsec_xyz"
        });

        // Assert
        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("sk_live_"));
    }

    [Fact]
    public void Validate_ProductionEnv_NonLiveKey_Fails()
    {
        // Arrange - sk_test_ in prod is rejected so we don't ship test-mode
        // Stripe keys to production by accident.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        var sut = BuildValidator();

        // Act
        var result = sut.Validate("Stripe", new StripeOptions
        {
            ApiKey = "sk_test_abcdef",
            WebhookSecret = "whsec_xyz"
        });

        // Assert
        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("sk_live_"));
    }

    [Fact]
    public void Validate_ProductionEnv_LiveKey_Passes()
    {
        // Arrange
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        var sut = BuildValidator();

        // Act
        var result = sut.Validate("Stripe", new StripeOptions
        {
            ApiKey = "sk_live_REDACTED_long_enough_to_be_real",
            WebhookSecret = "whsec_REDACTED"
        });

        // Assert
        result.Failed.Should().BeFalse();
    }

    [Fact]
    public void Validate_ProductionEnv_MissingWebhookSecret_Fails()
    {
        // Arrange - webhook secret is mandatory in Prod so unsigned webhooks
        // can't reach /api/billing/webhook and trigger double-charges.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        var sut = BuildValidator();

        // Act
        var result = sut.Validate("Stripe", new StripeOptions
        {
            ApiKey = "sk_live_REDACTED",
            WebhookSecret = ""
        });

        // Assert
        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("WebhookSecret"));
    }

    [Fact]
    public void Validate_DevelopmentEnv_EmptyApiKey_Passes()
    {
        // Arrange - empty key in dev means "use StubStripeGateway".
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        var sut = BuildValidator();

        // Act
        var result = sut.Validate("Stripe", new StripeOptions
        {
            ApiKey = string.Empty,
            WebhookSecret = string.Empty
        });

        // Assert
        result.Failed.Should().BeFalse();
    }

    [Fact]
    public void Validate_DevelopmentEnv_TestKey_Passes()
    {
        // Arrange
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        var sut = BuildValidator();

        // Act
        var result = sut.Validate("Stripe", new StripeOptions
        {
            ApiKey = "sk_test_abcdef",
            WebhookSecret = "whsec_xyz"
        });

        // Assert
        result.Failed.Should().BeFalse();
    }

    [Fact]
    public void Validate_DevelopmentEnv_NonStandardKey_Fails()
    {
        // Arrange - reject placeholder / typo values in dev (anything not
        // starting with sk_test_ or sk_live_).
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        var sut = BuildValidator();

        // Act
        var result = sut.Validate("Stripe", new StripeOptions
        {
            ApiKey = "not-a-stripe-key",
            WebhookSecret = "whsec_xyz"
        });

        // Assert
        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("sk_test_") || f.Contains("sk_live_"));
    }

    [Fact]
    public void Validate_ProductionEnv_SecretKeyAlias_Passes()
    {
        // Arrange - legacy env var Stripe__SecretKey is mapped to ApiKey via
        // raw IConfiguration read (it's NOT bound by the options system
        // because StripeOptions.ApiKey would need Stripe:ApiKey). This bridge
        // prevents the validator from blocking the existing prod deploy while
        // we migrate the env var name.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = "sk_live_REDACTED_long_enough",
            ["Stripe:WebhookSecret"] = "whsec_REDACTED"
        });
        var sut = BuildValidator(config);

        // Act - ApiKey NOT populated, but raw config has SecretKey
        var result = sut.Validate("Stripe", new StripeOptions
        {
            ApiKey = string.Empty,
            WebhookSecret = "whsec_REDACTED"
        });

        // Assert
        result.Failed.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithNullConfig_ThrowsArgumentNullException()
    {
        // Arrange + Act
        Action act = () => _ = new StripeOptionsValidator(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configuration");
    }
}
