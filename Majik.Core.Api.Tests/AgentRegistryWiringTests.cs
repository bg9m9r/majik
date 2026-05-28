using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Slice: a fetchland's activated-ability resolution closure consults
/// <see cref="AgentRegistry.Get"/> to pick which library card to fetch.
/// Pre-fix, GameFacade never called <see cref="AgentRegistry.Set"/>, so
/// <see cref="AgentRegistry.Get"/> always returned null and the closure
/// silently fell back to the first deterministic candidate — no agent
/// prompt ever fired at the live table. These tests pin the wiring.
/// </summary>
public class AgentRegistryWiringTests : IDisposable
{
    public AgentRegistryWiringTests() => AgentRegistry.Clear();

    public void Dispose() => AgentRegistry.Clear();

    [Fact]
    public void GameFacade_Ctor_RegistersBothPlayersAgents()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());

        AgentRegistry.Get(facade.Alice).Should().NotBeNull(
            because: "GameFacade must register Alice's agent so effect closures can prompt her");
        AgentRegistry.Get(facade.Bob).Should().NotBeNull(
            because: "GameFacade must register Bob's agent so effect closures can prompt him");
    }

    [Fact]
    public void GameFacade_ReplaceAgent_UpdatesRegistry()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());

        var swap = new ScriptedAgent();
        facade.ReplaceAliceAgent(swap);

        AgentRegistry.Get(facade.Alice).Should().BeSameAs(swap,
            because: "swapping in a bot/test agent must update the registry so effect closures see the new agent");
    }

    [Fact]
    public void GameFacade_Dispose_UnregistersAgents()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        AgentRegistry.Get(facade.Alice).Should().NotBeNull();

        facade.Dispose();

        AgentRegistry.Get(facade.Alice).Should().BeNull(
            because: "GameFacade.Dispose must remove its seat agents so concurrent matches don't leak");
        AgentRegistry.Get(facade.Bob).Should().BeNull(
            because: "GameFacade.Dispose must remove its seat agents so concurrent matches don't leak");
    }

    [Fact]
    public void GameFacade_ConcurrentMatches_KeepIndependentAgentRegistrations()
    {
        // Two facades alive at once — each must have its own seats registered.
        var first = GameFacade.Create("Alice1", "Bob1", Array.Empty<ICard>(), Array.Empty<ICard>());
        var second = GameFacade.Create("Alice2", "Bob2", Array.Empty<ICard>(), Array.Empty<ICard>());

        AgentRegistry.Get(first.Alice).Should().NotBeNull();
        AgentRegistry.Get(second.Alice).Should().NotBeNull();

        first.Dispose();

        AgentRegistry.Get(first.Alice).Should().BeNull(because: "first match was disposed");
        AgentRegistry.Get(second.Alice).Should().NotBeNull(
            because: "Dispose on first match must not leak into the second");
        AgentRegistry.Get(second.Bob).Should().NotBeNull();
    }
}
