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
/// Unit tests for <see cref="PitchAltCostProbe"/> — surfaces
/// <see cref="PitchAlternativeCost"/> candidates for the heuristic bot's
/// CR 118.9 enumeration. Validates timing-gate filtering and color-match.
/// </summary>
public class PitchAltCostProbeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Majik.Core.Stack.Stack _stack;

    public PitchAltCostProbeTests()
    {
        _stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
    }

    [Fact]
    public void CandidatesFor_OpponentsTurn_BlueCardInHand_EmitsCandidate()
    {
        var fow = InHand(_alice, new Instant("Force of Will", "{3}{U}{U}"));
        var brainstorm = InHand(_alice, new Instant("Brainstorm", "{U}"));

        var probe = new PitchAltCostProbe(PitchAltCostProbe.DefaultLookup);
        var ctx = NewContext(activePlayer: _bob);

        var candidates = probe.CandidatesFor(fow, _alice, ctx).ToList();
        candidates.Should().HaveCount(1);
        candidates[0].Should().BeOfType<PitchAlternativeCost>()
            .Which.ExiledCard.Should().BeSameAs(brainstorm);
    }

    [Fact]
    public void CandidatesFor_OwnTurn_NoCandidates()
    {
        var fow = InHand(_alice, new Instant("Force of Will", "{3}{U}{U}"));
        InHand(_alice, new Instant("Brainstorm", "{U}"));

        var probe = new PitchAltCostProbe(PitchAltCostProbe.DefaultLookup);
        // Active player is the caster — pitch is illegal (CR 118.9 timing).
        var ctx = NewContext(activePlayer: _alice);

        probe.CandidatesFor(fow, _alice, ctx).Should().BeEmpty();
    }

    [Fact]
    public void CandidatesFor_NoBlueCard_NoCandidates()
    {
        var fow = InHand(_alice, new Instant("Force of Will", "{3}{U}{U}"));
        // Only a red card in hand — can't pitch for a blue pitch cost.
        InHand(_alice, new Instant("Lightning Bolt", "{R}"));

        var probe = new PitchAltCostProbe(PitchAltCostProbe.DefaultLookup);
        var ctx = NewContext(activePlayer: _bob);

        probe.CandidatesFor(fow, _alice, ctx).Should().BeEmpty();
    }

    [Fact]
    public void CandidatesFor_NonPitchCard_NoCandidates()
    {
        // A non-pitch card (no descriptor) should never emit candidates.
        var counterspell = InHand(_alice, new Instant("Counterspell", "{U}{U}"));
        InHand(_alice, new Instant("Brainstorm", "{U}"));

        var probe = new PitchAltCostProbe(PitchAltCostProbe.DefaultLookup);
        var ctx = NewContext(activePlayer: _bob);

        probe.CandidatesFor(counterspell, _alice, ctx).Should().BeEmpty();
    }

    [Fact]
    public void CandidatesFor_ForceOfNegation_NoLifeRider_EmitsZeroLifeCostCandidate()
    {
        var fon = InHand(_alice, new Instant("Force of Negation", "{1}{U}{U}"));
        InHand(_alice, new Instant("Brainstorm", "{U}"));

        var probe = new PitchAltCostProbe(PitchAltCostProbe.DefaultLookup);
        var ctx = NewContext(activePlayer: _bob);

        var candidates = probe.CandidatesFor(fon, _alice, ctx).ToList();
        candidates.Should().HaveCount(1);
        candidates[0].Should().BeOfType<PitchAlternativeCost>()
            .Which.LifeCost.Should().Be(0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static T InHand<T>(Player owner, T card) where T : Card
    {
        card.SetOwner(owner);
        card.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(card);
        return card;
    }

    private GameContext NewContext(Player activePlayer) =>
        new(_alice, new[] { _alice, _bob }, activePlayer, 1, PhaseStateType.PreCombatMain, _stack);
}
