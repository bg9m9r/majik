using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="KaldraCompleatFactory"/> (Modern Horizons 2, {7}).
///
/// Covers:
/// - Identity (Legendary Artifact — Equipment, {7}).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Indestructible printed on the equipment itself (always-on marker).
/// - Static +5/+5 boost via <see cref="AttachedBoostEffect"/>.
/// - First strike / trample / indestructible / haste grants on the equipped
///   creature, and that they lapse on detach.
/// - Granted "deals combat damage to a creature → exile it" trigger.
/// - Living-weapon ETB trigger spawns a 0/0 black Phyrexian Germ and attaches.
/// - Equip {7} activated ability shape.
/// </summary>
public class KaldraCompleatTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void KaldraCompleat_Identity()
    {
        var k = KaldraCompleatFactory.Create(_alice);

        k.Name.Should().Be("Kaldra Compleat");
        k.ManaCost.Should().Be("{7}");
        k.HasType(CardType.Artifact).Should().BeTrue();
        k.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Kaldra Compleat is a Legendary artifact");
        k.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Kaldra Compleat is an Equipment");
        k.Owner.Should().BeSameAs(_alice);
        k.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KaldraCompleat_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Kaldra Compleat", _alice);

        c.Should().BeOfType<Artifact>("Kaldra Compleat is an Artifact");
        c.Name.Should().Be("Kaldra Compleat");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the living-weapon ETB trigger is attached");
        c.Abilities.OfType<EquipActivatedAbility>().Should().ContainSingle(
            "Equip {7} is wired");
    }

    [Fact]
    public void KaldraCompleat_IsIndestructibleItself()
    {
        // Indestructible is printed on Kaldra Compleat the artifact directly,
        // so the marker lives on the card on every construction path.
        var k = KaldraCompleatFactory.Create(_alice);

        k.Abilities.OfType<KeywordAbility>()
            .Select(a => a.Keyword)
            .Should().Contain(
                kw => string.Equals(kw, "Indestructible", System.StringComparison.OrdinalIgnoreCase),
                "Kaldra Compleat is Indestructible");
    }

    // -----------------------------------------------------------------------
    // Equip cost shape
    // -----------------------------------------------------------------------

    [Fact]
    public void KaldraCompleat_EquipAbility_HasGenericSevenCost()
    {
        var k = KaldraCompleatFactory.Create(_alice);

        var equip = k.Abilities.OfType<EquipActivatedAbility>().Single();
        equip.EquipCost.Generic.Should().Be(7, "Equip {7} is the printed activation cost");
    }

    // -----------------------------------------------------------------------
    // Static +5/+5 + keyword grants on the equipped creature
    // -----------------------------------------------------------------------

    [Fact]
    public void KaldraCompleat_Equipped_Bear_Becomes_7_7_WithKeywordSoup()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var k = KaldraCompleatFactory.Create(_alice, svc, triggers: null, zoneService: null);
        k.Zone = ZoneType.Battlefield;
        k.AttachTo(bear);

        bear.GetPower().Should().Be(7, "+5 power from Kaldra Compleat");
        bear.GetToughness().Should().Be(7, "+5 toughness from Kaldra Compleat");
        CombatAbilities.HasFirstStrike(bear).Should().BeTrue();
        CombatAbilities.HasTrample(bear).Should().BeTrue();
        CombatAbilities.HasIndestructible(bear).Should().BeTrue();
        CombatAbilities.HasHaste(bear).Should().BeTrue();
    }

    [Fact]
    public void KaldraCompleat_Detach_RestoresPT_AndKeywordsLapse()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var k = KaldraCompleatFactory.Create(_alice, svc, triggers: null, zoneService: null);
        k.Zone = ZoneType.Battlefield;
        k.AttachTo(bear);

        bear.GetPower().Should().Be(7);
        CombatAbilities.HasTrample(bear).Should().BeTrue();

        k.Unattach();

        bear.GetPower().Should().Be(2, "boost lapses on detach");
        CombatAbilities.HasFirstStrike(bear).Should().BeFalse();
        CombatAbilities.HasTrample(bear).Should().BeFalse();
        CombatAbilities.HasIndestructible(bear).Should().BeFalse();
        CombatAbilities.HasHaste(bear).Should().BeFalse();
    }

    [Fact]
    public void KaldraCompleat_ShapeOnly_CarriesKeywordMarkers()
    {
        var k = KaldraCompleatFactory.Create(_alice);

        var keywords = k.Abilities.OfType<KeywordAbility>()
            .Select(a => a.Keyword)
            .ToList();

        foreach (var kw in new[] { "First strike", "Trample", "Haste" })
        {
            keywords.Should().Contain(
                x => string.Equals(x, kw, System.StringComparison.OrdinalIgnoreCase),
                $"shape-only path stamps {kw} on Kaldra Compleat");
        }
    }

    // -----------------------------------------------------------------------
    // Granted combat-damage exile trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void KaldraCompleat_GrantsExileOnCombatDamageTrigger_ToBearer()
    {
        var svc = new ContinuousEffectsService();
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var k = KaldraCompleatFactory.Create(_alice, svc, triggers, zoneService: null);
        k.Zone = ZoneType.Battlefield;
        k.AttachTo(bear);

        // Force a layer pass so the grant lifecycle syncs (the grant
        // materialises as a side effect of Compute — CR 613).
        _ = bear.GetPower();

        // The grant projects a combat trigger onto the bearer.
        bear.Abilities.OfType<TriggeredAbility>().Should().NotBeEmpty(
            "the exile-on-combat-damage trigger is granted to the equipped creature");
    }

    [Fact]
    public void KaldraCompleat_GrantedTrigger_ExilesDamagedCreature()
    {
        var svc = new ContinuousEffectsService();
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var k = KaldraCompleatFactory.Create(_alice, svc, triggers, zoneService: null);
        k.Zone = ZoneType.Battlefield;
        k.AttachTo(bear);

        var victim = new Creature("Victim", "2B", 3, 3)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(victim);

        // Force a layer pass so the grant lifecycle syncs the trigger onto
        // the bearer (CR 613 — grant materialises as a side effect of Compute).
        _ = bear.GetPower();

        // Resolve the granted trigger directly: feed its condition the combat
        // event (bearer → victim), then execute the effect.
        var granted = bear.Abilities.OfType<TriggeredAbility>().Single();
        var evt = new CombatDamageDealtEvent(source: bear, target: victim, amount: 7);
        granted.Condition.Matches(evt, granted).Should().BeTrue(
            "the trigger fires when the bearer deals combat damage to a creature");

        foreach (var effect in granted.Effects) effect.Execute();

        victim.Zone.Should().Be(ZoneType.Exile,
            "the damaged creature is exiled (CR 701.10)");
    }

    // -----------------------------------------------------------------------
    // Living weapon — ETB spawns Germ + auto-attaches
    // -----------------------------------------------------------------------

    [Fact]
    public void KaldraCompleat_LivingWeapon_SpawnsBlackPhyrexianGermAndAttaches()
    {
        var k = KaldraCompleatFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(k);
        k.SetZone(ZoneType.Battlefield);

        var etb = k.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        var germ = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .SingleOrDefault(c => c.Name == "Germ");

        germ.Should().NotBeNull("living weapon spawns a Germ token");
        germ!.IsToken.Should().BeTrue();
        germ.BasePower.Should().Be(0, "Germ enters as 0/0 (CR 702.91)");
        germ.BaseToughness.Should().Be(0);
        germ.HasSubtype(CardSubtype.Germ).Should().BeTrue();
        germ.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue(
            "Kaldra Compleat's Germ is a Phyrexian Germ");
        Majik.Core.Cards.CardColors.GetColors(germ).Should()
            .ContainSingle(c => c == Majik.Core.ValueObjects.ManaColor.Black,
                "Germ is a black creature token");

        k.AttachedTo.Should().BeSameAs(germ,
            "Kaldra Compleat attaches itself to the freshly-spawned Germ");
    }
}
