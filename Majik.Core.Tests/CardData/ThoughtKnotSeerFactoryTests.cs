using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ThoughtKnotSeerFactory"/>
/// (Oath of the Gatewatch, {3}{C}).
///
/// Creature — Eldrazi 4/4. Oracle text:
///   "When this creature enters, target opponent reveals their hand. You
///    choose a nonland card from it and exile that card.
///    When this creature leaves the battlefield, target opponent draws a
///    card."
///
/// Covers:
///   - Identity (Creature — Eldrazi, {3}{C}, 4/4, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - ETB + LTB triggers attached.
///   - ETB predicate matches when this card enters the battlefield.
///   - ETB resolves to exile a nonland from target opponent's hand.
///   - ETB skips lands when picking.
///   - LTB predicate matches when this card leaves the battlefield.
///   - LTB resolves to draw a card for the chosen target.
/// </summary>
public class ThoughtKnotSeerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ThoughtKnotSeer_Identity()
    {
        var tks = ThoughtKnotSeerFactory.Create(_alice);

        tks.Name.Should().Be("Thought-Knot Seer");
        tks.ManaCost.Should().Be("{3}{C}");
        tks.HasType(CardType.Creature).Should().BeTrue();
        tks.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        tks.BasePower.Should().Be(4);
        tks.BaseToughness.Should().Be(4);
        tks.Owner.Should().BeSameAs(_alice);
        tks.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ThoughtKnotSeer_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Thought-Knot Seer", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Thought-Knot Seer");
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(4);
        ((Creature)card).BaseToughness.Should().Be(4);
    }

    [Fact]
    public void ThoughtKnotSeer_HasEtbAndLtbTriggers()
    {
        var tks = ThoughtKnotSeerFactory.Create(_alice);
        var triggers = tks.Abilities.OfType<TriggeredAbility>().ToList();

        triggers.Should().HaveCount(2,
            "one ETB trigger (reveal + exile) + one LTB trigger (target opponent draws)");
        triggers.Should().AllSatisfy(t => t.TargetRequests.Should().HaveCount(1,
            "each trigger has a single 'target opponent' request"));
    }

    // -----------------------------------------------------------------------
    // ETB trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void ThoughtKnotSeer_EtbTrigger_MatchesOnSelfEnter()
    {
        var tks = ThoughtKnotSeerFactory.Create(_alice);
        var etb = GetEtbTrigger(tks);

        var moveEvent = new CardMovedEvent(
            card: tks,
            fromZone: ZoneType.Stack,
            toZone: ZoneType.Battlefield);

        etb.Condition.Matches(moveEvent, etb).Should().BeTrue();
    }

    [Fact]
    public void ThoughtKnotSeer_EtbTrigger_DoesNotMatchOnOtherCardEnter()
    {
        var tks = ThoughtKnotSeerFactory.Create(_alice);
        var etb = GetEtbTrigger(tks);

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        other.SetController(_alice);
        var moveEvent = new CardMovedEvent(
            card: other,
            fromZone: ZoneType.Hand,
            toZone: ZoneType.Battlefield);

        etb.Condition.Matches(moveEvent, etb).Should().BeFalse();
    }

    [Fact]
    public void ThoughtKnotSeer_EtbEffect_ExilesFirstNonlandFromTargetOpponentsHand()
    {
        // Bob's hand: one land + one nonland → factory's fallback path
        // exiles the nonland.
        var tks = ThoughtKnotSeerFactory.Create(_alice);
        tks.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(tks);

        var bobLand = NamedCardFactory.Create("Swamp", _bob);
        bobLand.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(bobLand);

        var bobSpell = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobSpell.SetOwner(_bob);
        bobSpell.SetController(_bob);
        bobSpell.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(bobSpell);

        var etb = GetEtbTrigger(tks);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });

        foreach (var effect in etb.Effects) effect.Execute();

        // Nonland exiled, land stays in hand.
        bobSpell.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(bobSpell);
        bobLand.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(bobLand);
    }

    [Fact]
    public void ThoughtKnotSeer_EtbEffect_LandsOnlyHand_NoExile()
    {
        var tks = ThoughtKnotSeerFactory.Create(_alice);
        tks.SetZone(ZoneType.Battlefield);

        var land1 = NamedCardFactory.Create("Swamp", _bob);
        land1.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(land1);
        var land2 = NamedCardFactory.Create("Mountain", _bob);
        land2.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(land2);

        var etb = GetEtbTrigger(tks);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });

        foreach (var effect in etb.Effects) effect.Execute();

        // Lands untouched, exile zone empty.
        land1.Zone.Should().Be(ZoneType.Hand);
        land2.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Exile.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // LTB trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void ThoughtKnotSeer_LtbTrigger_MatchesOnSelfLeavesBattlefield()
    {
        var tks = ThoughtKnotSeerFactory.Create(_alice);
        var ltb = GetLtbTrigger(tks);

        var moveEvent = new CardMovedEvent(
            card: tks,
            fromZone: ZoneType.Battlefield,
            toZone: ZoneType.Graveyard);

        ltb.Condition.Matches(moveEvent, ltb).Should().BeTrue();
    }

    [Fact]
    public void ThoughtKnotSeer_LtbEffect_TargetOpponentDrawsACard()
    {
        var tks = ThoughtKnotSeerFactory.Create(_alice);

        // Seed Bob's library so the draw resolves.
        var topCard = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        topCard.SetOwner(_bob);
        topCard.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(topCard);

        var ltb = GetLtbTrigger(tks);
        ltb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });

        foreach (var effect in ltb.Effects) effect.Execute();

        // Library is empty, card moved to hand.
        _bob.Zones.Library.GetCards().Should().NotContain(topCard);
        _bob.Zones.Hand.GetCards().Should().Contain(topCard);
        topCard.Zone.Should().Be(ZoneType.Hand);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Pick the ETB trigger — first <see cref="TriggeredAbility"/> the
    /// factory attaches; matches a self-enter <see cref="CardMovedEvent"/>.
    /// </summary>
    private static TriggeredAbility GetEtbTrigger(ICard card)
    {
        var probe = new CardMovedEvent(card, ZoneType.Stack, ZoneType.Battlefield);
        return card.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Condition.Matches(probe, t));
    }

    /// <summary>
    /// Pick the LTB trigger — matches a self-leave <see cref="CardMovedEvent"/>.
    /// </summary>
    private static TriggeredAbility GetLtbTrigger(ICard card)
    {
        var probe = new CardMovedEvent(card, ZoneType.Battlefield, ZoneType.Graveyard);
        return card.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Condition.Matches(probe, t));
    }
}
