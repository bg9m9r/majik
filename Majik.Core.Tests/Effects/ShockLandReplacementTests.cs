using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;
using Land = Majik.Core.Cards.Land;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 614.1c — production binder-chain replacement. Mirrors the test-only
/// <c>ShockLandCycleFactory</c> shape: when an agent is registered for the
/// land's controller the replacement consults <c>ChooseYesNoAsync</c>; with
/// no agent it preserves the legacy "pay-if-life&gt;2, else tapped" fallback
/// so pre-agent integration tests / no-agent paths don't regress.
/// </summary>
public class ShockLandReplacementTests : IDisposable
{
    public ShockLandReplacementTests()
    {
        // Tests register agents into the global AgentRegistry — clear in
        // ctor + Dispose so we never inherit a stale registration from
        // a neighbour test in the same class.
        AgentRegistry.Clear();
    }

    public void Dispose() => AgentRegistry.Clear();

    private static (Player alice, Land land, ReplacementBus bus) MakeWorld(int life = 20)
    {
        var alice = new Player("Alice", life);
        var land = new Land("Overgrown Tomb") { Owner = alice, Zone = ZoneType.Hand };
        var bus = new ReplacementBus();
        bus.Register(new ShockLandReplacement(land));
        return (alice, land, bus);
    }

    private static ZoneMoveIntent EtbIntent(Land land, Player controller) =>
        new(land, ZoneType.Hand, ZoneType.Battlefield, Controller: controller);

    // -----------------------------------------------------------------
    // No-agent fallback (preserves legacy ShockLandBinderTests posture)
    // -----------------------------------------------------------------

    [Fact]
    public void NoAgent_HighLife_AutoPays2Life_EntersUntapped()
    {
        var (alice, land, bus) = MakeWorld(life: 20);
        // No AgentRegistry.Set — explicit no-agent fallback.

        var after = bus.Apply(EtbIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse();
        alice.LifeTotal.Should().Be(18,
            "no-agent fallback preserves legacy auto-pay-2-life posture");
    }

    [Fact]
    public void NoAgent_LowLife_EntersTapped_NoLifePaid()
    {
        var (alice, land, bus) = MakeWorld(life: 2);
        // No AgentRegistry.Set.

        var after = bus.Apply(EtbIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue();
        alice.LifeTotal.Should().Be(2, "CR 119.4 — refuse the auto-suicide");
    }

    // -----------------------------------------------------------------
    // Agent-driven prompt (new wiring)
    // -----------------------------------------------------------------

    [Fact]
    public void AgentSaysYes_HighLife_Pays2Life_EntersUntapped()
    {
        var (alice, land, bus) = MakeWorld(life: 20);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var after = bus.Apply(EtbIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "agent answered yes → land enters untapped");
        alice.LifeTotal.Should().Be(18, "yes path debits 2 life");
    }

    [Fact]
    public void AgentSaysNo_HighLife_EntersTapped_NoLifePaid()
    {
        var (alice, land, bus) = MakeWorld(life: 20);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(alice, agent);

        var after = bus.Apply(EtbIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "agent declined → land enters tapped");
        alice.LifeTotal.Should().Be(20, "no payment when declined");
    }

    [Fact]
    public void AgentRegistered_LifeTwoOrLess_NoPromptFired_EntersTapped()
    {
        // Per spec deferral: at LifeTotal <= 2 the production replacement
        // skips the prompt entirely and enters tapped. ScriptedAgent
        // would throw if prompted (empty yes/no queue) — surviving means
        // no prompt fired. CR 119.4 conservative posture.
        var (alice, land, bus) = MakeWorld(life: 2);
        var agent = new ScriptedAgent();
        // No QueueYesNo.
        AgentRegistry.Set(alice, agent);

        var after = bus.Apply(EtbIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue();
        alice.LifeTotal.Should().Be(2, "no prompt → no payment");
    }
}
