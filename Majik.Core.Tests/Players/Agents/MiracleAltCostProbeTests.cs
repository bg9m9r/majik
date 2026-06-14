using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData.Factories;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Players.Agents;

/// <summary>
/// Unit tests for <see cref="MiracleAltCostProbe"/> — CR 702.94 Miracle, the
/// live-engine seam that surfaces a freshly-drawn miracle card's miracle cost
/// to the bot's spell-cast enumeration. The probe reads the runtime miracle
/// window grant (<see cref="Card.RuntimeMiracleCost"/>) the draw hook stamps
/// on the first card a player drew this turn.
/// </summary>
public class MiracleAltCostProbeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Majik.Core.Stack.Stack _stack;

    public MiracleAltCostProbeTests()
    {
        _stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
    }

    [Fact]
    public void CandidatesFor_WindowOpen_EmitsMiracleAltCost()
    {
        var terminus = InHand(_alice, TerminusFactory.Create(_alice));
        terminus.GrantRuntimeMiracle(ManaCost.Parse(TerminusFactory.MiracleCostText));

        var probe = new MiracleAltCostProbe();
        var candidates = probe.CandidatesFor(terminus, _alice, Ctx()).ToList();

        candidates.Should().HaveCount(1);
        var alt = candidates[0].Should().BeOfType<MiracleAlternativeCost>().Subject;
        alt.CanCastFor(terminus, _alice).Should().BeTrue();
        alt.AlternativeManaCost.Should().Be(ManaCost.Parse("{W}"));
    }

    [Fact]
    public void CandidatesFor_NoWindow_NoCandidates()
    {
        var terminus = InHand(_alice, TerminusFactory.Create(_alice));
        // No GrantRuntimeMiracle — the window was never opened (the card was
        // not the first card drawn this turn).

        var probe = new MiracleAltCostProbe();
        probe.CandidatesFor(terminus, _alice, Ctx()).Should().BeEmpty(
            "the miracle alt-cost surfaces only while the draw window is open (CR 702.94b)");
    }

    [Fact]
    public void CandidatesFor_NotInHand_NoCandidates()
    {
        var terminus = TerminusFactory.Create(_alice);
        terminus.SetOwner(_alice);
        terminus.SetZone(ZoneType.Graveyard);
        terminus.GrantRuntimeMiracle(ManaCost.Parse(TerminusFactory.MiracleCostText));

        var probe = new MiracleAltCostProbe();
        probe.CandidatesFor(terminus, _alice, Ctx()).Should().BeEmpty(
            "miracle is cast from the hand (CR 702.94a)");
    }

    [Fact]
    public void CandidatesFor_OpponentOwnsCard_NoCandidates()
    {
        var terminus = InHand(_bob, TerminusFactory.Create(_bob));
        terminus.GrantRuntimeMiracle(ManaCost.Parse(TerminusFactory.MiracleCostText));

        var probe = new MiracleAltCostProbe();
        probe.CandidatesFor(terminus, _alice, Ctx()).Should().BeEmpty(
            "Alice has no window on a card sitting in Bob's hand");
    }

    [Fact]
    public void Registry_CreateDefault_IncludesMiracleProbe()
    {
        var registry = AlternativeCostProbeRegistry.CreateDefault();
        registry.Probes.OfType<MiracleAltCostProbe>().Should().HaveCount(1,
            "the default registry must ship with the Miracle alt-cost probe (CR 702.94)");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static T InHand<T>(Player owner, T card) where T : Card
    {
        card.SetOwner(owner);
        card.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(card);
        return card;
    }

    private GameContext Ctx() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1,
            StepStateType.PreCombatMain, _stack, landPlayAvailable: true);
}
