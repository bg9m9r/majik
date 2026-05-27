using FluentAssertions;
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
/// Unit tests for <see cref="EscapeAltCostProbe"/> — surfaces
/// <see cref="EscapeAlternativeCost"/> candidates for the heuristic
/// bot's CR 702.138 enumeration. Validates zone filtering + the
/// "exile N OTHER graveyard cards" pre-filter.
/// </summary>
public class EscapeAltCostProbeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Majik.Core.Stack.Stack _stack;

    public EscapeAltCostProbeTests()
    {
        _stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
    }

    [Fact]
    public void CandidatesFor_InGraveyard_SufficientOthers_EmitsCandidate()
    {
        var phlage = InGrave(_alice, new Creature("Phlage, Titan of Fire's Fury", "{2}{R}{W}", 4, 4));
        // Phlage's Escape rider is 5 OTHER cards.
        for (int i = 0; i < 5; i++) InGrave(_alice, new Instant($"F{i}", "{1}"));

        var probe = new EscapeAltCostProbe(EscapeAltCostProbe.DefaultLookup);
        var candidates = probe.CandidatesFor(phlage, _alice, Ctx()).ToList();

        candidates.Should().HaveCount(1);
        candidates[0].Should().BeOfType<EscapeAlternativeCost>()
            .Which.ExileFromGraveyardCount.Should().Be(5);
    }

    [Fact]
    public void CandidatesFor_InHand_NoCandidates()
    {
        // Phlage in hand, not graveyard — Escape not legal (CR 702.138a).
        var phlage = new Creature("Phlage, Titan of Fire's Fury", "{2}{R}{W}", 4, 4);
        phlage.SetOwner(_alice);
        phlage.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(phlage);

        var probe = new EscapeAltCostProbe(EscapeAltCostProbe.DefaultLookup);
        probe.CandidatesFor(phlage, _alice, Ctx()).Should().BeEmpty();
    }

    [Fact]
    public void CandidatesFor_InsufficientOthers_NoCandidates()
    {
        // Phlage + only 2 others → pre-filter skips, needs 5 others.
        var phlage = InGrave(_alice, new Creature("Phlage, Titan of Fire's Fury", "{2}{R}{W}", 4, 4));
        InGrave(_alice, new Instant("F1", "{1}"));
        InGrave(_alice, new Instant("F2", "{1}"));

        var probe = new EscapeAltCostProbe(EscapeAltCostProbe.DefaultLookup);
        probe.CandidatesFor(phlage, _alice, Ctx()).Should().BeEmpty();
    }

    [Fact]
    public void CandidatesFor_OpponentGraveyard_NoCandidates()
    {
        var phlage = InGrave(_bob, new Creature("Phlage, Titan of Fire's Fury", "{2}{R}{W}", 4, 4));
        for (int i = 0; i < 5; i++) InGrave(_bob, new Instant($"F{i}", "{1}"));

        var probe = new EscapeAltCostProbe(EscapeAltCostProbe.DefaultLookup);
        // Caster is Alice — card lives in Bob's graveyard.
        probe.CandidatesFor(phlage, _alice, Ctx()).Should().BeEmpty();
    }

    [Fact]
    public void CandidatesFor_NonEscapeCard_NoCandidates()
    {
        var random = InGrave(_alice, new Instant("Random Card", "{R}"));
        // Stock graveyard with extras so absence of candidates is about the
        // lookup, not the other-card pool.
        InGrave(_alice, new Instant("F1", "{1}"));
        InGrave(_alice, new Instant("F2", "{1}"));
        InGrave(_alice, new Instant("F3", "{1}"));
        InGrave(_alice, new Instant("F4", "{1}"));
        InGrave(_alice, new Instant("F5", "{1}"));

        var probe = new EscapeAltCostProbe(EscapeAltCostProbe.DefaultLookup);
        probe.CandidatesFor(random, _alice, Ctx()).Should().BeEmpty();
    }

    [Fact]
    public void DefaultLookup_KnowsTheFourShipListCards()
    {
        // Identity table sanity-check — descriptor counts match Scryfall.
        EscapeAltCostProbe.DefaultLookup(
            new Creature("Uro, Titan of Nature's Wrath", "{1}{G}{U}", 6, 6))!.Value.ExileCount.Should().Be(5);
        EscapeAltCostProbe.DefaultLookup(
            new Creature("Phlage, Titan of Fire's Fury", "{2}{R}{W}", 4, 4))!.Value.ExileCount.Should().Be(5);
        EscapeAltCostProbe.DefaultLookup(
            new Creature("Phoenix of Ash", "{2}{R}{R}", 3, 2))!.Value.ExileCount.Should().Be(4);
        EscapeAltCostProbe.DefaultLookup(
            new Instant("Cling to Dust", "{B}"))!.Value.ExileCount.Should().Be(5);
    }

    [Fact]
    public void Registry_CreateDefault_IncludesEscapeProbe()
    {
        var registry = AlternativeCostProbeRegistry.CreateDefault();
        registry.Probes.OfType<EscapeAltCostProbe>().Should().HaveCount(1,
            "the default registry must ship with the Escape probe registered (CR 702.138)");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static T InGrave<T>(Player owner, T card) where T : Card
    {
        card.SetOwner(owner);
        card.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(card);
        return card;
    }

    private GameContext Ctx() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);
}
