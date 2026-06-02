using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Nahiri, the Harbinger (Shadows over Innistrad, {2}{R}{W}).
///
/// Covers:
///   - Card identity (Legendary Planeswalker — Nahiri, starting loyalty 4,
///     mana cost {2}{R}{W}), materialised from the embedded JSON definition.
///   - +2: you may discard a card; if you do, draw a card (and the empty-hand
///     "no discard ⇒ no draw" rider, CR 700.6).
///   - −2: exile target enchantment / tapped artifact / tapped creature
///     (and the filter that skips untapped artifacts/creatures and lands).
///   - −8: search library for an artifact or creature card, put it onto the
///     battlefield, grant haste, and the delayed end-step return to hand.
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "M")]
public class NahiriTheHarbingerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Nahiri_IsLegendaryPlaneswalker_Nahiri_4Loyalty_AtCost2RW()
    {
        var nahiri = NahiriTheHarbingerFactory.Create(_alice);

        nahiri.Name.Should().Be("Nahiri, the Harbinger");
        nahiri.ManaCost.Should().Be("{2}{R}{W}");
        nahiri.HasType(CardType.Planeswalker).Should().BeTrue();
        nahiri.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        nahiri.HasSubtype(CardSubtype.Nahiri).Should().BeTrue();
        nahiri.Loyalty.Should().Be(4);
        nahiri.StartingLoyalty.Should().Be(4);
        nahiri.Owner.Should().BeSameAs(_alice);
        nahiri.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Nahiri_HasThreeLoyaltyAbilities_Plus2_Minus2_Minus8()
    {
        var nahiri = NahiriTheHarbingerFactory.Create(_alice);

        var loyalty = nahiri.Abilities.OfType<LoyaltyAbility>().ToList();
        loyalty.Should().HaveCount(3);
        loyalty.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +2, -2, -8 });
    }
    // -----------------------------------------------------------------------
    // +2: discard a card, then draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void Plus2_DiscardsOne_ThenDrawsOne_AndAddsLoyalty()
    {
        var inHand = new Card("Hand Card", "{1}") { Owner = _alice };
        _alice.Zones.Hand.AddCard(inHand);
        inHand.SetZone(ZoneType.Hand);

        var deckTop = new Card("Deck Top", "{1}") { Owner = _alice };
        _alice.Zones.Library.AddCard(deckTop);
        deckTop.SetZone(ZoneType.Library);

        var nahiri = NahiriTheHarbingerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(nahiri);
        nahiri.SetZone(ZoneType.Battlefield);

        nahiri.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +2).Activate();

        nahiri.Loyalty.Should().Be(6, "4 + 2 = 6");
        _alice.Zones.Graveyard.GetCards().Should().Contain(inHand, "discarded from hand");
        _alice.Zones.Hand.GetCards().Should().Contain(deckTop, "drew the deck top");
        _alice.Zones.Hand.GetCards().Should().NotContain(inHand);
        _alice.Zones.Library.GetCards().Should().NotContain(deckTop);
    }

    [Fact]
    public void Plus2_EmptyHand_DoesNotDraw_ButStillAddsLoyalty()
    {
        // CR 700.6 — "If you do" gates the draw on a discard actually
        // happening. With an empty hand there is no discard, so no draw.
        var deckTop = new Card("Deck Top", "{1}") { Owner = _alice };
        _alice.Zones.Library.AddCard(deckTop);
        deckTop.SetZone(ZoneType.Library);

        var nahiri = NahiriTheHarbingerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(nahiri);
        nahiri.SetZone(ZoneType.Battlefield);

        nahiri.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +2).Activate();

        nahiri.Loyalty.Should().Be(6, "loyalty change still applies (CR 606.3)");
        _alice.Zones.Hand.GetCards().Should().BeEmpty("no discard ⇒ no draw");
        _alice.Zones.Library.GetCards().Should().Contain(deckTop, "the deck top was not drawn");
    }

    // -----------------------------------------------------------------------
    // −2: exile target enchantment / tapped artifact / tapped creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus2_ExilesEnchantment_RegardlessOfTap()
    {
        var enchantment = new Enchantment("Aura", "{1}{W}");
        enchantment.SetOwner(_bob); enchantment.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(enchantment);
        enchantment.SetZone(ZoneType.Battlefield);

        var nahiri = NahiriTheHarbingerFactory.Create(
            _alice,
            targetResolver: () => new Permanent[] { enchantment },
            zoneService: null, triggers: null, eventBus: null, random: null);

        nahiri.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -2).Activate();

        nahiri.Loyalty.Should().Be(2, "4 - 2 = 2");
        _bob.Zones.Exile.GetCards().Should().Contain(enchantment);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(enchantment);
        enchantment.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Minus2_ExilesTappedCreature_ButSkipsUntappedOne()
    {
        var untapped = new Creature("Untapped Bear", "{1}{G}", 2, 2);
        untapped.SetOwner(_bob); untapped.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(untapped);
        untapped.SetZone(ZoneType.Battlefield);

        var tapped = new Creature("Tapped Bear", "{1}{G}", 2, 2);
        tapped.SetOwner(_bob); tapped.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(tapped);
        tapped.SetZone(ZoneType.Battlefield);
        Fx.Tap(tapped);

        // Resolver offers the untapped creature first — the filter must skip
        // it and exile the tapped one (and only one).
        var nahiri = NahiriTheHarbingerFactory.Create(
            _alice,
            targetResolver: () => new Permanent[] { untapped, tapped },
            zoneService: null, triggers: null, eventBus: null, random: null);

        nahiri.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -2).Activate();

        _bob.Zones.Exile.GetCards().Should().Contain(tapped);
        _bob.Zones.Exile.GetCards().Should().NotContain(untapped,
            "an untapped creature is not a legal −2 target");
        _bob.Zones.Battlefield.GetCards().Should().Contain(untapped);
    }

    [Fact]
    public void Minus2_SkipsUntappedArtifact_AndDoesNotExileLand()
    {
        var land = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        land.SetOwner(_bob); land.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var untappedArtifact = new Artifact("Idle Relic", "{2}");
        untappedArtifact.SetOwner(_bob); untappedArtifact.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(untappedArtifact);
        untappedArtifact.SetZone(ZoneType.Battlefield);

        var nahiri = NahiriTheHarbingerFactory.Create(
            _alice,
            targetResolver: () => new Permanent[] { land, untappedArtifact },
            zoneService: null, triggers: null, eventBus: null, random: null);

        nahiri.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -2).Activate();

        _bob.Zones.Exile.GetCards().Should().BeEmpty(
            "a land and an untapped artifact are not legal −2 targets");
        _bob.Zones.Battlefield.GetCards().Should().Contain(land);
        _bob.Zones.Battlefield.GetCards().Should().Contain(untappedArtifact);
    }

    // -----------------------------------------------------------------------
    // −8: tutor a creature/artifact to battlefield + haste + delayed return
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus8_PutsArtifactOrCreatureOntoBattlefield_AndGrantsHaste()
    {
        var creature = new Creature("Eager Construct", "{2}", 3, 3);
        creature.SetOwner(_alice);
        _alice.Zones.Library.AddCard(creature);
        creature.SetZone(ZoneType.Library);

        // A noncreature, nonartifact card that must be ignored by the search.
        var sorcery = new Card("Just a Sorcery", "{1}{R}", new[] { CardType.Sorcery }) { Owner = _alice };
        _alice.Zones.Library.AddCard(sorcery);
        sorcery.SetZone(ZoneType.Library);

        var nahiri = NahiriTheHarbingerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(nahiri);
        nahiri.SetZone(ZoneType.Battlefield);
        nahiri.AddLoyalty(4); // 4 + 4 = 8 (enough for −8)

        var ult = nahiri.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -8);
        ult.CanActivate().Should().BeTrue();
        ult.Activate();

        nahiri.Loyalty.Should().Be(0, "8 - 8 = 0");
        _alice.Zones.Battlefield.GetCards().Should().Contain(creature);
        _alice.Zones.Library.GetCards().Should().NotContain(creature);
        _alice.Zones.Library.GetCards().Should().Contain(sorcery, "the sorcery is not artifact/creature");
        creature.Controller.Should().BeSameAs(_alice);
        creature.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Haste", "It gains haste (CR 702.10)");
    }

    [Fact]
    public void Minus8_RegistersDelayedReturn_ThatReturnsToHandAtNextEndStep()
    {
        var creature = new Creature("Eager Construct", "{2}", 3, 3);
        creature.SetOwner(_alice);
        _alice.Zones.Library.AddCard(creature);
        creature.SetZone(ZoneType.Library);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var nahiri = NahiriTheHarbingerFactory.Create(
            _alice,
            targetResolver: null,
            zoneService: null,
            triggers: triggers,
            eventBus: null,
            random: null);
        _alice.Zones.Battlefield.AddCard(nahiri);
        nahiri.SetZone(ZoneType.Battlefield);
        nahiri.AddLoyalty(4);

        nahiri.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -8).Activate();
        _alice.Zones.Battlefield.GetCards().Should().Contain(creature);

        // Fire the next end step — the delayed return trigger should queue.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PendingCount.Should().Be(1,
            "the delayed end-step return trigger is pending after the End step starts");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(creature, "returned to its owner's hand");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(creature);
        creature.Zone.Should().Be(ZoneType.Hand);
    }
}
