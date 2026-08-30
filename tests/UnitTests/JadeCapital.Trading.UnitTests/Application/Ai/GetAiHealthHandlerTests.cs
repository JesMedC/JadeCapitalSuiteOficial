using JadeCapital.Shared.Kernel.Ai;
using JadeCapital.Trading.Application.Features.Ai.GetAiHealth;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Xunit;

namespace JadeCapital.Trading.UnitTests.Application.Ai;

/// <summary>
/// Tests for <c>GetAiHealthHandler</c> (Wave 5, slice 5b.1).
///
/// <para>
/// The handler is a thin shim around <see cref="IAIProvider.IsHealthyAsync"/>:
/// when the provider says "up" we surface 200 + the configured model; when
/// it says "down" we surface 503. The handler does NOT touch HttpClient
/// directly — the IAIProvider abstraction is the swap point for Wave 6
/// (OpenAI / Claude impls).
/// </para>
/// </summary>
public class GetAiHealthHandlerTests
{
    private static GetAiHealthHandler Build(IAIProvider provider, AIProviderOptions? options = null)
        => new(provider, options ?? new AIProviderOptions(), NullLogger<GetAiHealthHandler>.Instance);

    [Fact]
    public async Task Handle_Returns_Ok_With_Model_When_Provider_Is_Healthy()
    {
        var provider = Substitute.For<IAIProvider>();
        provider.IsHealthyAsync(Arg.Any<CancellationToken>()).Returns(true);

        var opts = new AIProviderOptions { Model = "llama3.1:8b" };
        var handler = Build(provider, opts);

        var result = await handler.Handle(new GetAiHealthQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("ok");
        result.Value.Model.Should().Be("llama3.1:8b");
    }

    [Fact]
    public async Task Handle_Returns_Down_When_Provider_Is_Unhealthy()
    {
        // IsHealthyAsync returns false on connection refused / timeout — the
        // handler treats that as 503-equivalent (no model surfaced, since we
        // don't know if the configured model is even running).
        var provider = Substitute.For<IAIProvider>();
        provider.IsHealthyAsync(Arg.Any<CancellationToken>()).Returns(false);

        var handler = Build(provider);

        var result = await handler.Handle(new GetAiHealthQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("down");
        result.Value.Model.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Propagates_Cancellation_To_Provider()
    {
        var provider = Substitute.For<IAIProvider>();
        provider.IsHealthyAsync(Arg.Any<CancellationToken>()).Returns(true);

        var handler = Build(provider);

        using var cts = new CancellationTokenSource();
        await handler.Handle(new GetAiHealthQuery(), cts.Token);

        await provider.Received(1).IsHealthyAsync(cts.Token);
    }
}
