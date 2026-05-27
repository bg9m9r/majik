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
/// Unit tests for <see cref="DelveAltCostProbe"/> — surfaces
/// <see cref="DelveAlternativeCost"/> candidates for the heuristic bot's
/// CR 118.9/702.66 enumeration. Covers a spell-side delve card
/// (Treasure Cruise) and a creature-side delve card (Gurmag Angler), plus
/// the edge cases (empty yard, no generic pips, owner gate).
/// </summary>
public class DelveAltCostProbeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Majik.Core.Stack.Stack _stack;

    public DelveAltCostProbeTests()
    {
        _stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
    }

    [Fact]
    public void CandidatesFor_TreasureCruise_WithGraveyardFodder_EmitsDelveCost()
    {
        var cruise = InHand(_alice, TreasureCruiseFactory.Create(_alice));
        // 7 generic pips → 7 fodder cards = full delve.
        for (var i = 0; i < 7; i++)
        {
            ToYard(_alice, new Instant($"Bolt {i}", "{R}"));
        }

        var probe = new DelveAltCostProbe();
        var ctx = NewContext(activePlayer: _alice);

        var candidates = probe.CandidatesFor(cruise, _alice, ctx).ToList();
        candidates.Should().HaveCount(1);
        var delve = candidates[0].Should().BeOfType<DelveAlternativeCost>().Subject;
        delve.Chosen.Should().HaveCount(7);
        // Cruise's printed cost is {7}{U}; after full delve it's {U}.
        delve.AlternativeManaCost.Generic.Should().Be(0);
        delve.AlternativeManaCost.Blue.Should().Be(1);
    }

    [Fact]
    public void CandidatesFor_GurmagAngler_PartialDelve_ReducesGenericOnly()
    {
        var angler = InHand(_alice, GurmagAnglerFactory.Create(_alice));
        // 7 generic pips on {7}{B}, only 3 fodder available → partial delve.
        for (var i = 0; i < 3; i++)
        {
            ToYard(_alice, new Instant($"Thoughtseize {i}", "{B}"));
        }

        var probe = new DelveAltCostProbe();
        var ctx = NewContext(activePlayer: _alice);

        var candidates = probe.CandidatesFor(angler, _alice, ctx).ToList();
        candidates.Should().HaveCount(1);
        var delve = candidates[0].Should().BeOfType<DelveAlternativeCost>().Subject;
        delve.Chosen.Should().HaveCount(3);
        delve.AlternativeManaCost.Generic.Should().Be(4); // 7 - 3 = 4
        delve.AlternativeManaCost.Black.Should().Be(1);
    }

    [Fact]
    public void CandidatesFor_EmptyGraveyard_EmitsNothing()
    {
        var cruise = InHand(_alice, TreasureCruiseFactory.Create(_alice));

        var probe = new DelveAltCostProbe();
        var ctx = NewContext(activePlayer: _alice);

        probe.CandidatesFor(cruise, _alice, ctx).Should().BeEmpty();
    }

    [Fact]
    public void CandidatesFor_NonDelveCard_EmitsNothing()
    {
        var brainstorm = InHand(_alice, new Instant("Brainstorm", "{U}"));
        ToYard(_alice, new Instant("Bolt", "{R}"));

        var probe = new DelveAltCostProbe();
        var ctx = NewContext(activePlayer: _alice);

        probe.CandidatesFor(brainstorm, _alice, ctx).Should().BeEmpty();
    }

    [Fact]
    public void CandidatesFor_CardOwnedByOpponent_EmitsNothing()
    {
        // Opp's Treasure Cruise in opp's hand — Alice can't delve it.
        var cruise = TreasureCruiseFactory.Create(_bob);
        cruise.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(cruise);
        ToYard(_alice, new Instant("Bolt", "{R}"));

        var probe = new DelveAltCostProbe();
        var ctx = NewContext(activePlayer: _alice);

        probe.CandidatesFor(cruise, _alice, ctx).Should().BeEmpty();
    }

    [Fact]
    public void DelveAlternativeCost_OnResolved_ExilesChosenCards()
    {
        var cruise = InHand(_alice, TreasureCruiseFactory.Create(_alice));
        var fodder = ToYard(_alice, new Instant("Brainstorm", "{U}"));

        var probe = new DelveAltCostProbe();
        var ctx = NewContext(activePlayer: _alice);

        var delve = probe.CandidatesFor(cruise, _alice, ctx)
            .OfType<DelveAlternativeCost>().Single();

        delve.OnResolved(cruise, _alice);

        fodder.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(fodder);
        _alice.Zones.Exile.GetCards().Should().Contain(fodder);
    }

    [Fact]
    public void CandidatesFor_DigThroughTime_WithFodder_EmitsDelveCost()
    {
        // Spell side: Dig Through Time {6}{U}{U}. Confirms a second
        // spell-side delve card works through the same probe.
        var dig = InHand(_alice, DigThroughTimeFactory.Create(_alice));
        for (var i = 0; i < 6; i++) ToYard(_alice, new Instant($"X{i}", "{U}"));

        var probe = new DelveAltCostProbe();
        var ctx = NewContext(activePlayer: _alice);

        probe.CandidatesFor(dig, _alice, ctx).Should().ContainSingle(c => c is DelveAlternativeCost);
    }

    [Fact]
    public void CandidatesFor_MurktideRegent_WithFodder_EmitsDelveCost()
    {
        // Creature side: Murktide Regent {3}{U}{U}. Confirms a creature
        // delve card works through the same probe.
        var murktide = InHand(_alice, MurktideRegentFactory.Create(_alice));
        for (var i = 0; i < 3; i++) ToYard(_alice, new Instant($"X{i}", "{U}"));

        var probe = new DelveAltCostProbe();
        var ctx = NewContext(activePlayer: _alice);

        probe.CandidatesFor(murktide, _alice, ctx)
            .Should().ContainSingle(c => c is DelveAlternativeCost);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static T InHand<T>(Player owner, T card) where T : Card
    {
        card.SetOwner(owner);
        card.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(card);
        return card;
    }

    private static T ToYard<T>(Player owner, T card) where T : Card
    {
        card.SetOwner(owner);
        card.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(card);
        return card;
    }

    private GameContext NewContext(Player activePlayer) =>
        new(_alice, new[] { _alice, _bob }, activePlayer, 1, PhaseStateType.PreCombatMain, _stack);
}
