using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
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
/// CR 702.32 — <see cref="MultikickerAltCostProbe"/> discovery surface. Like
/// kicker, multikicker is an additional cost (CR 601.2f), so the probe yields
/// zero <see cref="IAlternativeCost"/> candidates; the value is in
/// <see cref="MultikickerAltCostProbe.MultikickerCostFor"/> (per-kick cost
/// lookup) and <see cref="MultikickerAltCostProbe.BuildAdditionalCost"/>
/// (the additional-cost rail the bot's how-many-times heuristic feeds).
/// </summary>
public class MultikickerAltCostProbeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private GameContext Ctx() => new(
        _alice, new[] { _alice, _bob }, _alice,
        1, StepStateType.PreCombatMain, new Majik.Core.Stack.Stack());

    [Fact]
    public void CandidatesFor_AlwaysEmpty_MultikickerIsAnAdditionalCost()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(chalice);

        new MultikickerAltCostProbe()
            .CandidatesFor(chalice, _alice, Ctx())
            .Should().BeEmpty();
    }

    [Fact]
    public void MultikickerCostFor_ReturnsPerKickCost_ForEverflowingChaliceInHand()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(chalice);

        new MultikickerAltCostProbe()
            .MultikickerCostFor(chalice, _alice)
            .Should().Be(ManaCost.Parse("{2}"));
    }

    [Fact]
    public void MultikickerCostFor_Null_WhenCardNotInHand()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(chalice);

        new MultikickerAltCostProbe()
            .MultikickerCostFor(chalice, _alice)
            .Should().BeNull();
    }

    [Fact]
    public void MultikickerCostFor_Null_ForNonMultikickerCard()
    {
        var bolt = new Instant("Lightning Bolt", "R");
        bolt.ChangeOwner(_alice);
        _alice.Zones.Hand.AddCard(bolt);

        new MultikickerAltCostProbe()
            .MultikickerCostFor(bolt, _alice)
            .Should().BeNull();
    }

    [Fact]
    public void BuildAdditionalCost_ProducesMultikickerCost_WithChosenTimes()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(chalice);

        var cost = new MultikickerAltCostProbe().BuildAdditionalCost(chalice, times: 3);
        cost.Should().BeOfType<MultikickerAdditionalCost>()
            .Which.Times.Should().Be(3);
    }

    [Fact]
    public void BuildAdditionalCost_Null_ForNonMultikickerCard()
    {
        var bolt = new Instant("Lightning Bolt", "R");
        bolt.ChangeOwner(_alice);
        _alice.Zones.Hand.AddCard(bolt);

        new MultikickerAltCostProbe()
            .BuildAdditionalCost(bolt, times: 2)
            .Should().BeNull();
    }

    [Fact]
    public void DefaultRegistry_WiresMultikickerProbe_YieldsNoAltCostForChalice()
    {
        // The registry composes Multikicker in; since it's an additional cost,
        // the alt-cost stream stays empty for the chalice (regression that the
        // probe doesn't accidentally surface a phantom alternative cost).
        var chalice = EverflowingChaliceFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(chalice);

        AlternativeCostProbeRegistry.CreateDefault()
            .CandidatesFor(chalice, _alice, Ctx())
            .Should().BeEmpty();
    }
}
