using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// Tests for <see cref="DredgeFactory"/> — the shared graveyard-anchored
/// draw replacement builder for the Dredge keyword (CR 702.52).
///
/// Covers:
/// - Build attaches a <see cref="KeywordAbility"/> "Dredge" marker with
///   <see cref="KeywordAbility.Arg"/> = N.
/// - Argument validation: N must be positive; source.Owner must be wired.
/// - Replacement fires only while source is in controller's graveyard
///   (CR 702.52a).
/// - Library &lt; N gates out the offer (CR 702.52b).
/// - Agent yes path: mill N + return source to hand + cancel underlying
///   draw.
/// - Agent no path: draw resolves normally.
/// - Mid-mill empty library halts cleanly without stamping the empty-
///   library loss flag (CR 704.5b is for draws, not mills).
/// - No-bus shape-only path attaches the marker without firing a
///   replacement.
/// </summary>
public class DredgeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Shape — KeywordAbility marker
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_AttachesDredgeKeywordMarker_WithArg()
    {
        var card = MakeCardInGraveyard("Stinkweed Imp");

        DredgeFactory.Build(card, n: 5);

        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Dredge")
            .Which.Arg.Should().Be(5);
    }

    [Fact]
    public void Build_ShapeOnlyWithoutBus_AttachesMarker_NoReplacementFired()
    {
        var card = MakeCardInGraveyard("Stinkweed Imp");

        DredgeFactory.Build(card, n: 5, replacementBus: null);

        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Dredge");
        // No bus -> Fx.DrawCards doesn't route through one; an attempted
        // draw resolves directly (shape-only path).
        FillLibrary(_alice, 10);
        Fx.DrawCards(_alice, 1);
        _alice.Zones.Hand.Count.Should().Be(1, "shape-only path: no replacement, draw resolves");
    }

    // -----------------------------------------------------------------------
    // Argument validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_ThrowsWhenNNonPositive()
    {
        var card = MakeCardInGraveyard("Stinkweed Imp");
        var act = () => DredgeFactory.Build(card, n: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Build_ThrowsWhenOwnerNotWired()
    {
        var card = new Card("Stinkweed Imp", "{1}{B}"); // no SetOwner
        var act = () => DredgeFactory.Build(card, n: 5);
        act.Should().Throw<ArgumentException>("replacement gates on source.Owner");
    }

    [Fact]
    public void Build_ThrowsOnNullSource()
    {
        var act = () => DredgeFactory.Build(null!, n: 5);
        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Primitive — accept path
    // -----------------------------------------------------------------------

    [Fact]
    public void Dredge_AgentYes_MillsN_ReturnsSourceToHand_CancelsDraw()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var imp = MakeCardInGraveyard("Stinkweed Imp");
        DredgeFactory.Build(imp, n: 5, replacementBus: bus);

        var lib = FillLibrary(_alice, 8);

        // Agent says yes to the dredge prompt.
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(_alice, agent);

        try
        {
            var drawn = Fx.DrawCards(_alice, 1);

            // Original draw cancelled — no card added to hand from the
            // library top.
            drawn.Should().BeEmpty("Dredge consumed the draw");

            // Source returned from graveyard to hand.
            imp.Zone.Should().Be(ZoneType.Hand);
            _alice.Zones.Hand.GetCards().Should().Contain(imp);

            // N=5 cards milled from top into graveyard.
            _alice.Zones.Graveyard.GetCards()
                .Where(c => !ReferenceEquals(c, imp))
                .Should().HaveCount(5,
                    "Dredge milled exactly N=5 cards on top of the previous graveyard contents");
            _alice.Zones.Library.GetCards().Should().HaveCount(3,
                "8 - 5 milled = 3 remaining");
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // Primitive — decline path
    // -----------------------------------------------------------------------

    [Fact]
    public void Dredge_AgentNo_DrawResolvesNormally()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var imp = MakeCardInGraveyard("Stinkweed Imp");
        DredgeFactory.Build(imp, n: 5, replacementBus: bus);

        FillLibrary(_alice, 8);

        // Agent declines the dredge.
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(_alice, agent);

        try
        {
            var drawn = Fx.DrawCards(_alice, 1);

            drawn.Should().HaveCount(1, "decline -> straight draw");
            imp.Zone.Should().Be(ZoneType.Graveyard,
                "decline leaves Stinkweed Imp in graveyard for a future dredge");
            _alice.Zones.Library.Count.Should().Be(7, "1 drawn from top, none milled");
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // CR 702.52b — Library < N gates the offer out
    // -----------------------------------------------------------------------

    [Fact]
    public void Dredge_LibraryFewerThanN_OfferGated_AgentNotPrompted()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var imp = MakeCardInGraveyard("Stinkweed Imp");
        DredgeFactory.Build(imp, n: 5, replacementBus: bus);

        FillLibrary(_alice, 4); // only 4 < N=5

        var agent = new ScriptedAgent();
        // Don't queue any yes/no — if the agent IS prompted Pop would throw.
        AgentRegistry.Set(_alice, agent);

        try
        {
            var drawn = Fx.DrawCards(_alice, 1);

            drawn.Should().HaveCount(1,
                "library < N gates the Dredge offer out, so the underlying draw resolves");
            imp.Zone.Should().Be(ZoneType.Graveyard);
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // Source-anchored — only fires while in controller's graveyard
    // -----------------------------------------------------------------------

    [Fact]
    public void Dredge_SourceNotInGraveyard_ReplacementDoesNotApply()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var imp = MakeCardInGraveyard("Stinkweed Imp");
        DredgeFactory.Build(imp, n: 5, replacementBus: bus);

        FillLibrary(_alice, 8);

        // Move the source out of graveyard (e.g. exiled / reanimated).
        _alice.Zones.Graveyard.RemoveCard(imp);
        _alice.Zones.Exile.AddCard(imp);
        imp.SetZone(ZoneType.Exile);

        var agent = new ScriptedAgent();
        AgentRegistry.Set(_alice, agent);

        try
        {
            var drawn = Fx.DrawCards(_alice, 1);
            drawn.Should().HaveCount(1, "source not in graveyard -> replacement gated out");
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // CR 701.13 mid-mill empty library — halts cleanly
    // -----------------------------------------------------------------------

    [Fact]
    public void Dredge_EmptyLibraryMidMill_HaltsCleanly()
    {
        // Build a library with EXACTLY N cards. Library.Count >= N gate
        // passes; the mill empties the library completely. Subsequent
        // draws would fire the empty-library loss flag but Dredge
        // consumed THIS draw so the flag stays clear.
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var imp = MakeCardInGraveyard("Stinkweed Imp");
        DredgeFactory.Build(imp, n: 5, replacementBus: bus);

        FillLibrary(_alice, 5);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(_alice, agent);

        try
        {
            Fx.DrawCards(_alice, 1);

            _alice.Zones.Library.Count.Should().Be(0, "all 5 cards milled");
            imp.Zone.Should().Be(ZoneType.Hand, "Dredge returned the source to hand");
            _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse(
                "Dredge mills (CR 701.13); milling an empty library is not a draw from empty (CR 704.5b)");
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // No agent registered — straight draw posture
    // -----------------------------------------------------------------------

    [Fact]
    public void Dredge_NoAgent_DefaultsToStraightDraw()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var imp = MakeCardInGraveyard("Stinkweed Imp");
        DredgeFactory.Build(imp, n: 5, replacementBus: bus);

        FillLibrary(_alice, 8);

        // No AgentRegistry.Set call.
        var drawn = Fx.DrawCards(_alice, 1);

        drawn.Should().HaveCount(1, "no agent -> conservative posture: straight draw");
        imp.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Card MakeCardInGraveyard(string name)
    {
        var card = new Card(name, "{1}{B}");
        card.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
        return card;
    }

    private static List<Card> FillLibrary(Player p, int count)
    {
        var made = new List<Card>(count);
        for (int i = 0; i < count; i++)
        {
            var c = new Card($"Lib-{i}", "{0}");
            c.SetOwner(p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
            made.Add(c);
        }
        return made;
    }
}
