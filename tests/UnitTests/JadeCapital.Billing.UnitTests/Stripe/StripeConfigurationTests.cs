using JadeCapital.Billing.Infrastructure.DependencyInjection;
using JadeCapital.Billing.Infrastructure.Stripe;
using JadeCapital.Shared.Kernel.Stripe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stripe;

namespace JadeCapital.Billing.UnitTests.Stripe;

public class StripeConfigurationTests
{
    [Fact]
    public void StripeOptions_Exposes_Only_Canonical_SecretKey()
    {
        typeof(StripeOptions).GetProperty("SecretKey").Should().NotBeNull();
        typeof(StripeOptions).GetProperty("ApiKey").Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_SecretKey_Selects_Stub(string? secretKey)
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = secretKey,
            ["Stripe:ApiKey"] = "sk_test_legacy_must_be_ignored"
        });

        provider.GetRequiredService<IStripeGateway>()
            .Should().BeOfType<StubStripeGateway>();
    }

    [Fact]
    public void SecretKey_Selects_Real_Gateway_And_Reaches_StripeClient()
    {
        const string secretKey = "sk_test_canonical_contract";
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = secretKey,
            ["Stripe:ApiKey"] = "sk_test_legacy_must_be_ignored"
        });

        provider.GetRequiredService<IStripeGateway>()
            .Should().BeOfType<StripeGateway>();
        provider.GetRequiredService<IStripeClient>().ApiKey.Should().Be(secretKey);
    }

    private static ServiceProvider BuildProvider(
        IReadOnlyDictionary<string, string?> stripeConfiguration)
    {
        var values = new Dictionary<string, string?>(stripeConfiguration)
        {
            ["ConnectionStrings:Postgres"] =
                "Host=localhost;Database=jade;Username=jade;Password=test"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBillingInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}
