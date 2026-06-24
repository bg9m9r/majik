using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HollowMarauderFactory"/> — Hollow Marauder
/// ({6}{B}, Creature — Specter Rogue 4/2).
///
/// Oracle text (Scryfall verified):
///   "This spell costs {1} less to cast for each creature card in your
///    graveyard.
///    Flying
///    When this creature enters, any number of target opponents each discard
///    a card. For each of those opponents who didn't discard a card with mana
///    value 4 or greater, draw a card."
///
/// Covers ONLY the card's unique behaviour:
/// - Identity ({6}{B}, black, 4/2, Specter Rogue, Flying).
/// - Graveyard cost reduction: {1} less per creature card in the caster's
///   graveyard; {B} pip untouched (CR 117.7 / 117.7c).
/// - ETB trigger: a 0..N "any number of target opponents" target request.
/// - ETB resolution: each chosen opponent discards a card (CR 701.8); the
///   controller draws once per chosen opponent who did NOT discard a card with
///   mana value 4+ (cheaper discard OR empty hand) — CR 121.1.
/// </summary>
[Trait("Color", "B")]
public class HollowMarauderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void HollowMarauder_Identity()
    {
        var c = HollowMarauderFactory.Create(_alice);

        c.Name.Should().Be("Hollow Marauder");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(2);
        c.HasSubtype(CardSubtype.Specter).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        c.ManaCost.Should().Be("{6}{B}");
        c.ManaCostValue.TotalValue.Should().Be(7);
        CardColors.GetColors(c).Should().Contain(ManaColor.Black);
        c.HasEffectiveKeyword("Flying").Should().BeTrue("CR 702.9 — Hollow Marauder has Flying");
    }

    // -----------------------------------------------------------------------
    // Cost reduction — creature cards in the caster's graveyard (CR 117.7)
    // -----------------------------------------------------------------------

    [Fact]
    public void EffectiveCost_PrintedSixB_WhenGraveyardEmpty()
    {
        var c = HollowMarauderFactory.Create(_alice);

        var effective = CostReduction.GetEffectiveCost(c, _alice);

        effective.Generic.Should().Be(6);
        effective.Black.Should().Be(1);
    }

    [Fact]
    public void EffectiveCost_DroppedByCreatureCount_ButNotByNoncreatures()
    {
        var c = HollowMarauderFactory.Create(_alice);

        // Three creature cards + one sorcery in the graveyard.
        for (var i = 0; i < 3; i++)
        {
            var beast = new Creature($"Beast{i}", "{2}{G}", 3, 3);
            beast.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(beast);
        }
        var bolt = new Sorcery("Some Sorcery", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(bolt);

        var effective = CostReduction.GetEffectiveCost(c, _alice);

        effective.Generic.Should().Be(3,
            because: "three creature cards drop {3} ({6}{B} → {3}{B}); the sorcery doesn't count");
        effective.Black.Should().Be(1, because: "the {B} pip is untouched (CR 117.7c)");
    }

    [Fact]
    public void EffectiveCost_GenericFlooredAtZero_WhenManyCreaturesInGraveyard()
    {
        var c = HollowMarauderFactory.Create(_alice);

        for (var i = 0; i < 8; i++)
        {
            var beast = new Creature($"Beast{i}", "{2}{G}", 3, 3);
            beast.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(beast);
        }

        var effective = CostReduction.GetEffectiveCost(c, _alice);

        effective.Generic.Should().Be(0,
            because: "8 creatures drops more than the printed {6}; generic floors at 0");
        effective.Black.Should().Be(1, because: "the {B} pip can never be reduced (CR 117.7c)");
    }

    // -----------------------------------------------------------------------
    // ETB trigger shape — "any number of target opponents" (CR 115)
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbTrigger_DeclaresAnyNumberOfTargetOpponents()
    {
        var c = HollowMarauderFactory.Create(_alice);

        var etb = SelectEtbTrigger(c);

        etb.TargetRequests.Should().HaveCount(1);
        etb.TargetRequests[0].MinTargets.Should().Be(0,
            because: "\"any number of\" allows zero target opponents (CR 115)");
        etb.TargetRequests[0].MaxTargets.Should().Be(int.MaxValue);
        etb.TargetRequests[0].Description.Should().ContainEquivalentOf("opponent");
    }

    // -----------------------------------------------------------------------
    // ETB resolution — discard + conditional draw (CR 701.8 / CR 121.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_OpponentDiscardsCheapCard_ControllerDraws()
    {
        var bob = new Player("Bob", 20);

        // Bob's only card is a cheap (MV 1) nonland → he discards it; it has
        // MV < 4, so Alice draws a card.
        var cheap = new Sorcery("Shock", "{R}");
        cheap.SetOwner(bob);
        bob.Zones.Hand.AddCard(cheap);
        cheap.SetZone(ZoneType.Hand);

        // Alice has a card to draw.
        var libCard = new Sorcery("Top Card", "{1}");
        libCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(libCard);
        libCard.SetZone(ZoneType.Library);

        var marauder = HollowMarauderFactory.Create(_alice);
        var etb = SelectEtbTrigger(marauder);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });

        foreach (var effect in etb.Effects) effect.Execute();

        bob.Zones.Graveyard.GetCards().Should().Contain(cheap,
            "CR 701.8 — the target opponent discards a card");
        _alice.Zones.Hand.GetCards().Should().Contain(libCard,
            "Bob discarded a MV<4 card, so Alice draws (CR 121.1)");
        _alice.Zones.Library.Count.Should().Be(0);
    }

    [Fact]
    public void Etb_OpponentDiscardsExpensiveCard_ControllerDoesNotDraw()
    {
        var bob = new Player("Bob", 20);

        // Bob's only card is MV 4 → discarding it denies Alice the draw.
        var pricey = new Sorcery("Big Spell", "{3}{B}"); // MV 4
        pricey.SetOwner(bob);
        bob.Zones.Hand.AddCard(pricey);
        pricey.SetZone(ZoneType.Hand);

        var libCard = new Sorcery("Top Card", "{1}");
        libCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(libCard);
        libCard.SetZone(ZoneType.Library);

        var marauder = HollowMarauderFactory.Create(_alice);
        var etb = SelectEtbTrigger(marauder);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });

        foreach (var effect in etb.Effects) effect.Execute();

        bob.Zones.Graveyard.GetCards().Should().Contain(pricey,
            "CR 701.8 — the target opponent discards a card");
        _alice.Zones.Hand.GetCards().Should().NotContain(libCard,
            "Bob discarded a MV>=4 card, so Alice draws nothing");
        _alice.Zones.Library.Count.Should().Be(1);
    }

    [Fact]
    public void Etb_OpponentWithEmptyHand_ControllerStillDraws()
    {
        // Bob has an empty hand — he can't discard at all, which still counts as
        // "didn't discard a card with mana value 4 or greater" → Alice draws.
        var bob = new Player("Bob", 20);

        var libCard = new Sorcery("Top Card", "{1}");
        libCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(libCard);
        libCard.SetZone(ZoneType.Library);

        var marauder = HollowMarauderFactory.Create(_alice);
        var etb = SelectEtbTrigger(marauder);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });

        foreach (var effect in etb.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(libCard,
            "an empty-handed opponent didn't discard a MV>=4 card, so Alice draws (CR 121.1)");
    }

    [Fact]
    public void Etb_MultipleTargets_DrawsOncePerOpponentBelowFour()
    {
        // Two opponents targeted: one discards a cheap card, one discards an
        // expensive card. Alice draws exactly ONE (for the cheap discarder).
        var bob = new Player("Bob", 20);
        var carol = new Player("Carol", 20);

        var cheap = new Sorcery("Shock", "{R}"); // MV 1
        cheap.SetOwner(bob);
        bob.Zones.Hand.AddCard(cheap);
        cheap.SetZone(ZoneType.Hand);

        var pricey = new Sorcery("Big Spell", "{3}{B}"); // MV 4
        pricey.SetOwner(carol);
        carol.Zones.Hand.AddCard(pricey);
        pricey.SetZone(ZoneType.Hand);

        for (var i = 0; i < 3; i++)
        {
            var l = new Sorcery($"Lib{i}", "{1}");
            l.SetOwner(_alice);
            _alice.Zones.Library.AddCard(l);
            l.SetZone(ZoneType.Library);
        }

        var marauder = HollowMarauderFactory.Create(_alice);
        var etb = SelectEtbTrigger(marauder);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob, carol } });

        foreach (var effect in etb.Effects) effect.Execute();

        bob.Zones.Graveyard.GetCards().Should().Contain(cheap);
        carol.Zones.Graveyard.GetCards().Should().Contain(pricey);
        _alice.Zones.Hand.Count.Should().Be(1,
            "exactly one chosen opponent (Bob) didn't discard a MV>=4 card → one draw");
        _alice.Zones.Library.Count.Should().Be(2);
    }

    private static TriggeredAbility SelectEtbTrigger(Creature marauder) =>
        marauder.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1
                && t.TargetRequests[0].Description.Contains("opponent"));
}
