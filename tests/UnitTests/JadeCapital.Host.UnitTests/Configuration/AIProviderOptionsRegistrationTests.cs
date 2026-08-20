// Wave 12 slice 12.1 — AIProviderOptions DI bridge test.
//
// Asserts the fix for the GetAiHealthHandler DI failure:
//   1. IOptions<AIProviderOptions> is registered (preserves the existing
//      IHttpClientFactory typed-client delegate that reads IOptions<>).
//   2. The CONCRETE type AIProviderOptions is also registered as a singleton
//      (unblocks MediatR handlers like GetAiHealthHandler that take the
//      concrete type in their constructor).

using FluentAssertions;
using JadeCapital.Shared.Kernel.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JadeCapital.Host.UnitTests.Configuration;

public class AIProviderOptionsRegistrationTests
{
    [Fact]
    public void AddOptions_AndConcrete_Resolvable_BothForms()
    {
        // Arrange - mimic the Program.cs registration: AddOptions<>().Bind(...)
        // + AddSingleton(sp => sp.GetRequiredService<IOptions<>>().Value).
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ollama:BaseUrl"] = "http://test-ollama:11434",
                ["Ollama:Model"] = "llama3.1:8b-test",
                ["Ollama:Timeout"] = "00:00:45"
            })
            .Build();

        services.AddOptions<AIProviderOptions>()
            .Bind(config.GetSection("Ollama"));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AIProviderOptions>>().Value);

        using var sp = services.BuildServiceProvider();

        // Act
        var viaOptions = sp.GetRequiredService<IOptions<AIProviderOptions>>().Value;
        var viaConcrete = sp.GetRequiredService<AIProviderOptions>();

        // Assert - both resolution paths return the same instance
        viaOptions.BaseUrl.Should().Be("http://test-ollama:11434");
        viaOptions.Model.Should().Be("llama3.1:8b-test");
        viaConcrete.BaseUrl.Should().Be("http://test-ollama:11434");
        viaConcrete.Model.Should().Be("llama3.1:8b-test");
        ReferenceEquals(viaOptions, viaConcrete).Should().BeTrue(
            "the singleton bridge MUST return the same instance as IOptions<>.Value.");
    }

    [Fact]
    public void MissingConfig_UsesDefaults()
    {
        // Arrange - no "Ollama" section at all (the typical first-boot scenario
        // before ops sets Ollama__* env vars).
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        services.AddOptions<AIProviderOptions>()
            .Bind(config.GetSection("Ollama"));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AIProviderOptions>>().Value);

        using var sp = services.BuildServiceProvider();

        // Act
        var viaConcrete = sp.GetRequiredService<AIProviderOptions>();

        // Assert - falls back to the class defaults (matches Wave 5b.1 spec)
        viaConcrete.BaseUrl.Should().Be("http://localhost:11434");
        viaConcrete.Model.Should().Be("llama3.1:8b");
    }
}
