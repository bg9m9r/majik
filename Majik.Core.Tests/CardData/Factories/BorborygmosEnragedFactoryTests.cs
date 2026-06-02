using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BorborygmosEnragedFactory"/> — Legendary Creature —
/// Cyclops {4}{R}{R}{G}{G} 7/6 (Gatecrash). Oracle text (verified against
/// Scryfall):
///   "Trample
///    Whenever Borborygmos Enraged deals combat damage to a player, reveal
///    the top three cards of your library. Put all land cards revealed this
///    way into your hand and the rest into your graveyard.
///    Discard a land card: Borborygmos Enraged deals 3 damage to any target."
///
/// Covers:
///   - Card identity (Legendary Creature, Cyclops, 7/6, {4}{R}{R}{G}{G}).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Trample keyword marker.
///   - One combat-damage triggered ability + one activated ability.
///   - Reveal trigger: lands among the top three → HAND, rest → GRAVEYARD.
///   - Reveal trigger: nonbasic land is eligible; empty library no-op.
///   - Discard-a-land burn: cost gate (needs a land in hand) + 3 damage to
///     any target (creature / player).
/// </summary>
[Trait("Color", "RG")]
public class BorborygmosEnragedFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ───────────────────────────────────────────────────────────────────
    // Identity / dispatch
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void BorborygmosEnraged_IsLegendaryCyclops7_6_AtCost4RRGG()
    {
        var card = BorborygmosEnragedFactory.Create(_alice);

        card.Name.Should().Be("Borborygmos Enraged");
        card.ManaCost.Should().Be("{4}{R}{R}{G}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cyclops).Should().BeTrue();
        card.Power.Should().Be(7);
        card.Toughness.Should().Be(6);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BorborygmosEnraged()
    {
        var card = NamedCardFactory.Create("Borborygmos Enraged", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Borborygmos Enraged");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{4}{R}{R}{G}{G}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BorborygmosEnraged_HasTrample()
    {
        var card = BorborygmosEnragedFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Trample",
                "Borborygmos Enraged has Trample (CR 702.19)");
    }

    [Fact]
    public void BorborygmosEnraged_HasOneCombatTrigger_AndOneActivatedAbility()
    {
        var card = BorborygmosEnragedFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one combat-damage reveal trigger");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "one discard-a-land burn ability");
    }

    // ───────────────────────────────────────────────────────────────────
    // Combat-damage reveal trigger
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void CombatTrigger_PutsLandsToHand_RestToGraveyard()
    {
        // Top three: 1 basic land + 2 nonland. The land → hand, the rest →
        // graveyard.
        var bolt = SeedLibraryCard(new Instant("Lightning Bolt", "{R}"));
        var doom = SeedLibraryCard(new Sorcery("Doom Blade", "{1}{B}"));
        var forest = SeedLibraryCard(new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest }));

        ResolveCombatTrigger();

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(forest, "all land cards go to hand");
        forest.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Graveyard.GetCards()
            .Should().Contain(new Card[] { bolt, doom }, "the rest go to graveyard");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(forest);
    }

    [Fact]
    public void CombatTrigger_AllThreeLands_AllGoToHand()
    {
        var f1 = SeedLibraryCard(new Land("Forest", subtypes: new[] { CardSubtype.Forest }));
        var f2 = SeedLibraryCard(new Land("Mountain", subtypes: new[] { CardSubtype.Mountain }));
        var dual = SeedLibraryCard(new Land("Stomping Ground",
            subtypes: new[] { CardSubtype.Mountain, CardSubtype.Forest }));

        ResolveCombatTrigger();

        _alice.Zones.Hand.GetCards().Should().Contain(new Card[] { f1, f2, dual },
            "all three (incl. a nonbasic dual) are land cards → all to hand");
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty("no nonland cards revealed");
    }

    [Fact]
    public void CombatTrigger_NoLands_AllMilled()
    {
        var cards = new[] { "A", "B", "C" }
            .Select(n => SeedLibraryCard(new Instant(n, "")))
            .ToList();

        ResolveCombatTrigger();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no land among the top three → nothing goes to hand");
        _alice.Zones.Graveyard.GetCards().Should().Contain(cards,
            "with no land revealed, all three go to the graveyard");
    }

    [Fact]
    public void CombatTrigger_EmptyLibrary_IsNoOp()
    {
        var card = BorborygmosEnragedFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        Action act = () => { foreach (var e in trigger.Effects) e.Execute(); };
        act.Should().NotThrow("empty library → clean no-op (CR 701.21).");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void CombatTrigger_FiresOnlyOnCombatDamageToPlayer()
    {
        var card = BorborygmosEnragedFactory.Create(_alice);
        // IsTriggered gates on the source being in an active zone (Battlefield).
        card.SetZone(ZoneType.Battlefield);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var bob = new Player("Bob", 20);

        // Combat damage from this card to a player → fires.
        var toPlayer = new CombatDamageDealtEvent(card, bob, amount: 7);
        trigger.IsTriggered(toPlayer).Should().BeTrue(
            "combat damage to a player fires the reveal trigger");

        // Combat damage from this card to a creature (not a player) → no fire.
        var victim = new Creature("Bear", "1G", 2, 2);
        var toCreature = new CombatDamageDealtEvent(card, victim, amount: 7);
        trigger.IsTriggered(toCreature).Should().BeFalse(
            "combat damage to a creature does not fire (printed 'to a player')");
    }

    // ───────────────────────────────────────────────────────────────────
    // Discard-a-land burn ability
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void BurnAbility_HasDiscardALandCardCost_AndOneAnyTarget()
    {
        var card = BorborygmosEnragedFactory.Create(_alice);
        var burn = card.Abilities.OfType<ActivatedAbility>().Single();

        burn.Costs.OfType<DiscardALandCardCost>().Should().ContainSingle(
            "the discard-a-land cost (CR 118.5)");
        burn.TargetRequests.Should().HaveCount(1);
        burn.TargetRequests[0].MinTargets.Should().Be(1);
        burn.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void BurnCost_CannotPayWithoutALandCardInHand()
    {
        var card = BorborygmosEnragedFactory.Create(_alice);
        var cost = card.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<DiscardALandCardCost>().Single();

        // A creature card in hand does not satisfy "discard a land card".
        var bear = new Creature("Bear", "1G", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(bear);

        cost.CanPay(_alice).Should().BeFalse(
            "no land card in hand to discard");
    }

    [Fact]
    public void BurnCost_CanPayWithALandCardInHand_AndDiscardsIt()
    {
        var card = BorborygmosEnragedFactory.Create(_alice);
        var cost = card.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<DiscardALandCardCost>().Single();

        var land = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        land.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        cost.CanPay(_alice).Should().BeTrue("a land card is available to discard");

        cost.Pay(_alice);
        _alice.Zones.Hand.GetCards().Should().NotContain(land);
        _alice.Zones.Graveyard.GetCards().Should().Contain(land,
            "CR 701.16a — the discarded land card moves to the graveyard");
    }

    [Fact]
    public void BurnEffect_DealsThreeDamageToTargetCreature()
    {
        var bob = new Player("Bob", 20);
        var card = BorborygmosEnragedFactory.Create(_alice);

        var target = new Creature("Grizzly Bears", "1G", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Battlefield);

        var burn = card.Abilities.OfType<ActivatedAbility>().Single();
        burn.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        foreach (var effect in burn.Effects) effect.Execute();

        target.Damage.Should().Be(3, "deals 3 damage to any target");
    }

    [Fact]
    public void BurnEffect_DealsThreeDamageToPlayer()
    {
        var bob = new Player("Bob", 20);
        var card = BorborygmosEnragedFactory.Create(_alice);

        var burn = card.Abilities.OfType<ActivatedAbility>().Single();
        burn.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });
        foreach (var effect in burn.Effects) effect.Execute();

        bob.LifeTotal.Should().Be(17, "20 - 3 = 17");
    }

    // ───────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────

    private void ResolveCombatTrigger()
    {
        var card = BorborygmosEnragedFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();
    }

    private T SeedLibraryCard<T>(T card) where T : Card
    {
        card.SetOwner(_alice);
        _alice.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        return card;
    }
}
