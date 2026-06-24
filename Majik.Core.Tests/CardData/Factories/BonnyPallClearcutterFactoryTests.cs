using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Tests.Helpers;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BonnyPallClearcutterFactory"/>.
///
/// Bonny Pall, Clearcutter — {3}{G}{U}{U} Legendary Creature — Giant Scout,
/// 6/5. Oracle text (verified against Scryfall 2026-06-24):
///   "Reach
///    When Bonny Pall enters, create Beau, a legendary blue Ox creature token
///    with 'Beau's power and toughness are each equal to the number of lands
///    you control.'
///    Whenever you attack, draw a card, then you may put a land card from your
///    hand or graveyard onto the battlefield."
///
/// Covers (the card's UNIQUE behaviour only — the contract test already asserts
/// dispatch + well-formedness):
///   - Identity: {3}{G}{U}{U} Legendary Giant Scout 6/5, mana value 6.
///   - Reach keyword marker (CR 702.17).
///   - ETB trigger mints Beau (legendary blue Ox) whose CDA P/T = lands you
///     control (CR 604.3 / 613.7a).
///   - Attack trigger: "Whenever you attack" draws a card, then auto-takes a
///     land from hand (preferred) or graveyard onto the battlefield (CR 508.1).
///   - Attack trigger does NOT fire on an opponent's attack.
/// </summary>
[Trait("Color", "M")]
public class BonnyPallClearcutterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void BonnyPall_Identity_LegendaryGiantScout_6_5_At3GUU()
    {
        var card = BonnyPallClearcutterFactory.Create(_alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Bonny Pall, Clearcutter");
        card.ManaCost.Should().Be("{3}{G}{U}{U}");
        card.ManaCostValue.TotalValue.Should().Be(6, "{3}{G}{U}{U} is mana value 6");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Giant).Should().BeTrue();
        card.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        card.BasePower.Should().Be(6);
        card.BaseToughness.Should().Be(5);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BonnyPall_HasReachKeywordMarker()
    {
        var card = BonnyPallClearcutterFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Reach", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("the printed line includes Reach");
    }

    [Fact]
    public void BonnyPall_HasEtbAndAttackTriggers()
    {
        var card = BonnyPallClearcutterFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB (create Beau) + 'whenever you attack'");
    }

    // ── ETB: create Beau (legendary blue Ox; CDA P/T = lands you control) ──

    [Fact]
    public void CreateBeauToken_IsLegendaryBlueOx_TokenWithDynamicPT()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService(bus);

        // Three lands under Alice's control.
        for (var i = 0; i < 3; i++)
        {
            var forest = new Land("Forest");
            forest.SetOwner(_alice);
            _alice.Zones.Battlefield.AddCard(forest);
            forest.SetZone(ZoneType.Battlefield);
        }

        var beau = BonnyPallClearcutterFactory.CreateBeauToken(_alice, zoneService: null, effects);

        beau.Name.Should().Be("Beau");
        beau.IsToken.Should().BeTrue();
        beau.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Beau is a legendary token");
        beau.HasSubtype(CardSubtype.Ox).Should().BeTrue();
        CardColors.GetColors(beau).Should().Contain(ManaColor.Blue, "blue Ox token");

        // CDA: P/T each equal to the number of lands you control → 3/3.
        beau.Power.Should().Be(3, "three lands you control");
        beau.Toughness.Should().Be(3, "three lands you control");

        // Add another land → 4/4 (read live on Compute).
        var swamp = new Land("Swamp");
        swamp.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(swamp);
        swamp.SetZone(ZoneType.Battlefield);
        bus.Publish(new Majik.Core.Events.CardMovedEvent(
            swamp, ZoneType.Hand, ZoneType.Battlefield));

        beau.Power.Should().Be(4, "four lands you control");
        beau.Toughness.Should().Be(4, "four lands you control");
    }

    [Fact]
    public void EtbTrigger_Resolving_CreatesBeauOnBattlefield()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService(bus);

        var card = BonnyPallClearcutterFactory.Create(
            _alice, effects, zoneService: null, triggers: triggers);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var etb = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<Majik.Core.Events.CardMovedEvent>);
        ContextResolve.Resolve(etb, _alice, _alice, _bob);

        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => c.IsToken && c.Name == "Beau")
            .Should().Be(1, "the ETB trigger creates exactly one Beau");
    }

    // ── Attack trigger: draw a card, then may put a land ───────────────────

    [Fact]
    public void AttackTrigger_YouAttack_DrawsACardAndPutsLandFromHand()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var combat = new CombatManager(bus);
        var effects = new ContinuousEffectsService(bus);

        var card = BonnyPallClearcutterFactory.Create(
            _alice, effects, zoneService: null, triggers: triggers);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        card.ClearSummoningSickness();

        // A card in library so the draw has something to take.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        // A land in hand → the optional "put a land" auto-takes it.
        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(forest);
        forest.SetZone(ZoneType.Hand);

        combat.StartCombat(_alice);
        combat.DeclareAttackers(_alice, new[]
        {
            new AttackerDeclaration(card, targetPlayer: _bob),
        });

        var attack = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<Majik.Core.Domain.DomainEvents.AttackersDeclaredEvent>);
        ContextResolve.Resolve(attack, _alice, _alice, _bob);

        // Drew the bolt.
        _alice.Zones.Hand.GetCards().Should().Contain(bolt, "you draw a card");
        _alice.Zones.Library.GetCards().Should().BeEmpty("the only library card was drawn");

        // Put the land onto the battlefield (untapped — oracle doesn't say tapped).
        forest.Zone.Should().Be(ZoneType.Battlefield, "the land is put onto the battlefield");
        forest.IsTapped.Should().BeFalse("the oracle does not say the land enters tapped");
    }

    [Fact]
    public void AttackTrigger_NoLandInHand_PutsLandFromGraveyard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var combat = new CombatManager(bus);
        var effects = new ContinuousEffectsService(bus);

        var card = BonnyPallClearcutterFactory.Create(
            _alice, effects, zoneService: null, triggers: triggers);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        card.ClearSummoningSickness();

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        // No land in hand; a land in graveyard.
        var island = new Land("Island");
        island.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(island);
        island.SetZone(ZoneType.Graveyard);

        combat.StartCombat(_alice);
        combat.DeclareAttackers(_alice, new[]
        {
            new AttackerDeclaration(card, targetPlayer: _bob),
        });

        var attack = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<Majik.Core.Domain.DomainEvents.AttackersDeclaredEvent>);
        ContextResolve.Resolve(attack, _alice, _alice, _bob);

        island.Zone.Should().Be(ZoneType.Battlefield,
            "with no land in hand the land comes from the graveyard");
    }

    [Fact]
    public void AttackTrigger_OpponentAttacks_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var combat = new CombatManager(bus);
        var effects = new ContinuousEffectsService(bus);

        var card = BonnyPallClearcutterFactory.Create(
            _alice, effects, zoneService: null, triggers: triggers);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var bobBear = new Creature("Bear", "{G}", 2, 2);
        bobBear.SetOwner(_bob);
        bobBear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobBear);
        bobBear.SetZone(ZoneType.Battlefield);
        bobBear.ClearSummoningSickness();

        // ETB has already been registered; clear nothing — just measure attack.
        var before = triggers.PendingCount;

        combat.StartCombat(_bob);
        combat.DeclareAttackers(_bob, new[]
        {
            new AttackerDeclaration(bobBear, targetPlayer: _alice),
        });

        triggers.PendingCount.Should().Be(before,
            "'Whenever you attack' only fires when Bonny Pall's controller is the attacker");
    }
}
