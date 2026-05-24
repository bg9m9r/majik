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
/// Unit tests for <see cref="OverloadAltCostProbe"/> — surfaces
/// <see cref="OverloadAlternativeCost"/> candidates for the heuristic bot's
/// CR 702.96 enumeration. Mizzium Mortars is the canonical ship-list entry.
/// </summary>
public class OverloadAltCostProbeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Majik.Core.Stack.Stack _stack;

    public OverloadAltCostProbeTests()
    {
        _stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
    }

    [Fact]
    public void CandidatesFor_MizziumMortars_InHand_EmitsOverloadCandidate()
    {
        var mortars = InHand(_alice, MizziumMortarsFactory.Create(_alice));

        var probe = new OverloadAltCostProbe();
        var ctx = NewContext(activePlayer: _alice);

        var candidates = probe.CandidatesFor(mortars, _alice, ctx).ToList();
        candidates.Should().HaveCount(1);
        var overload = candidates[0].Should().BeOfType<OverloadAlternativeCost>().Subject;
        overload.AlternativeManaCost.Should().Be(ManaCost.Parse("{4}{R}{R}"));
    }

    [Fact]
    public void CandidatesFor_NonOverloadCard_EmitsNothing()
    {
        var bolt = InHand(_alice, new Instant("Lightning Bolt", "{R}"));

        var probe = new OverloadAltCostProbe();
        var ctx = NewContext(activePlayer: _alice);

        probe.CandidatesFor(bolt, _alice, ctx).Should().BeEmpty();
    }

    [Fact]
    public void CandidatesFor_CardInGraveyard_EmitsNothing()
    {
        // Overload is a cast-from-hand alt cost — yard cards aren't legal.
        var mortars = MizziumMortarsFactory.Create(_alice);
        mortars.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(mortars);

        var probe = new OverloadAltCostProbe();
        var ctx = NewContext(activePlayer: _alice);

        probe.CandidatesFor(mortars, _alice, ctx).Should().BeEmpty();
    }

    [Fact]
    public void CandidatesFor_OpponentsCard_EmitsNothing()
    {
        var mortars = MizziumMortarsFactory.Create(_bob);
        mortars.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(mortars);

        var probe = new OverloadAltCostProbe();
        var ctx = NewContext(activePlayer: _alice);

        probe.CandidatesFor(mortars, _alice, ctx).Should().BeEmpty();
    }

    [Fact]
    public void CustomLookup_SuppliedByCaller_OverridesDefault()
    {
        // Custom lookup: every card overloads for {U}{U}.
        var custom = new OverloadAltCostProbe(_ => ManaCost.Parse("{U}{U}"));
        var bolt = InHand(_alice, new Instant("Lightning Bolt", "{R}"));
        var ctx = NewContext(activePlayer: _alice);

        var candidates = custom.CandidatesFor(bolt, _alice, ctx).ToList();
        candidates.Should().ContainSingle(c =>
            c.AlternativeManaCost.Equals(ManaCost.Parse("{U}{U}")));
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
        new(_alice, new[] { _alice, _bob }, activePlayer, 1, PhaseStateType.Main, _stack);
}
