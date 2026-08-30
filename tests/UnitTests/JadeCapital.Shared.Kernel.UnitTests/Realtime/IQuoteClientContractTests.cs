using System.Reflection;
using JadeCapital.Shared.Kernel.Realtime;
using FluentAssertions;

namespace JadeCapital.Shared.Kernel.UnitTests.Realtime;

// ============================================================================
//  IQuoteClient contract tests — slice 4c (Realtime).
//
//  Locks the public surface of the SignalR client interface so a future
//  rename / signature change breaks the build instead of silently breaking
//  the FE SignalR callback registration. Reflection-based because we want
//  to verify the wire-shape contract, not the runtime behavior (which is
//  tested at the Hub layer in QuoteHubTests).
// ============================================================================

public class IQuoteClientContractTests
{
    [Fact]
    public void Interface_IsPublic_AndLivesInRealtimeNamespace()
    {
        typeof(IQuoteClient).IsPublic.Should().BeTrue();
        typeof(IQuoteClient).Namespace.Should().Be("JadeCapital.Shared.Kernel.Realtime");
    }

    [Fact]
    public void OnQuoteUpdate_TakesQuoteUpdateAndReturnsTask()
    {
        var m = typeof(IQuoteClient).GetMethod("OnQuoteUpdate");
        m.Should().NotBeNull();
        m!.ReturnType.Should().Be<Task>();
        m.GetParameters().Should().HaveCount(1);
        m.GetParameters()[0].ParameterType.Should().Be<QuoteUpdate>();
    }

    [Fact]
    public void OnError_TakesCodeAndMessageAndReturnsTask()
    {
        var m = typeof(IQuoteClient).GetMethod("OnError");
        m.Should().NotBeNull();
        m!.ReturnType.Should().Be<Task>();
        var p = m.GetParameters();
        p.Should().HaveCount(2);
        p[0].ParameterType.Should().Be<string>();
        p[1].ParameterType.Should().Be<string>();
    }

    [Fact]
    public void Interface_ExposesExactlyTwoMethods()
    {
        var methods = typeof(IQuoteClient).GetMethods();
        methods.Should().HaveCount(2);
        methods.Select(m => m.Name).Should().BeEquivalentTo(new[] { "OnQuoteUpdate", "OnError" });
    }
}