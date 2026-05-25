using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="VanquishersBannerFactory"/> (Ixalan, {5}).
///
/// Covers:
/// - Identity (name, Artifact type, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - ETB type choice: chosen subtype stored and retrievable; no chooser =
///   null.
/// - Static +1/+1: creatures you control of the chosen type get +1/+1;
///   non-matching subtypes are unaffected; opponents' creatures of the
///   chosen type are unaffected (controller filter).
/// - Cast trigger: casting a creature spell of the chosen type draws 1;
///   casting a non-creature spell or a creature of a different type does
///   not; opponent casting a chosen-type creature spell does not.
/// - No chosen type: static effect is not registered AND cast trigger
///   never matches (no chooser supplied).
/// </summary>
public class VanquishersBannerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void VanquishersBanner_Identity()
    {
        var c = VanquishersBannerFactory.Create(_alice);

        c.Name.Should().Be("Vanquisher's Banner");
        c.ManaCost.Should().Be("{5}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void VanquishersBanner_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Vanquisher's Banner", _alice);

        c.Should().BeOfType<Artifact>("Vanquisher's Banner is an Artifact");
        c.Name.Should().Be("Vanquisher's Banner");
    }

    // -----------------------------------------------------------------------
    // ETB type choice
    // -----------------------------------------------------------------------

    [Fact]
    public void VanquishersBanner_NoChooser_LeavesChosenTypeUnset()
    {
        var banner = VanquishersBannerFactory.Create(_alice);

        VanquishersBannerFactory.GetChosenType(banner).Should().BeNull(
            "no chooser supplied = chosen-type slot stays empty");
    }

    [Fact]
    public void VanquishersBanner_WithChooser_CapturesChosenType()
    {
        var banner = VanquishersBannerFactory.Create(
            _alice,
            typeChooser: _ => CardSubtype.Goblin,
            continuousEffects: null,
            triggers: null);

        VanquishersBannerFactory.GetChosenType(banner)
            .Should().Be(CardSubtype.Goblin);
    }

    // -----------------------------------------------------------------------
    // Static +1/+1 effect — "Creatures you control of the chosen type
    // get +1/+1."
    // -----------------------------------------------------------------------

    [Fact]
    public void VanquishersBanner_BuffsControllerCreaturesOfChosenType()
    {
        var svc = new ContinuousEffectsService();

        var aliceGoblin = new Creature("Goblin Guide", "R", 2, 2,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var banner = VanquishersBannerFactory.Create(
            _alice,
            typeChooser: _ => CardSubtype.Goblin,
            continuousEffects: svc,
            triggers: null);
        banner.Zone = ZoneType.Battlefield;

        aliceGoblin.GetPower().Should().Be(3,
            "Goblin gets +1/+1 from the Banner with Goblin chosen");
        aliceGoblin.GetToughness().Should().Be(3);
    }

    [Fact]
    public void VanquishersBanner_DoesNotBuff_NonMatchingSubtype()
    {
        var svc = new ContinuousEffectsService();

        var aliceMerfolk = new Creature("Silvergill Adept", "1U", 2, 2,
            subtypes: new[] { CardSubtype.Merfolk })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var banner = VanquishersBannerFactory.Create(
            _alice,
            typeChooser: _ => CardSubtype.Goblin,
            continuousEffects: svc,
            triggers: null);
        banner.Zone = ZoneType.Battlefield;

        aliceMerfolk.GetPower().Should().Be(2,
            "Merfolk doesn't match the chosen Goblin type");
        aliceMerfolk.GetToughness().Should().Be(2);
    }

    [Fact]
    public void VanquishersBanner_DoesNotBuff_OpponentCreatures()
    {
        var svc = new ContinuousEffectsService();

        var bobGoblin = new Creature("Goblin Piledriver", "1R", 2, 2,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var banner = VanquishersBannerFactory.Create(
            _alice,
            typeChooser: _ => CardSubtype.Goblin,
            continuousEffects: svc,
            triggers: null);
        banner.Zone = ZoneType.Battlefield;

        bobGoblin.GetPower().Should().Be(2,
            "'creatures YOU control' excludes opponents' creatures");
        bobGoblin.GetToughness().Should().Be(2);
    }

    [Fact]
    public void VanquishersBanner_NoChosenType_DoesNotRegisterStatic()
    {
        var svc = new ContinuousEffectsService();

        var aliceGoblin = new Creature("Goblin Guide", "R", 2, 2,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var banner = VanquishersBannerFactory.Create(
            _alice,
            typeChooser: null,
            continuousEffects: svc,
            triggers: null);
        banner.Zone = ZoneType.Battlefield;

        aliceGoblin.GetPower().Should().Be(2,
            "no chosen type = no static = no buff");
    }

    // -----------------------------------------------------------------------
    // Cast trigger — "Whenever you cast a creature spell, if it shares a
    // creature type with the chosen type, draw a card."
    // -----------------------------------------------------------------------

    [Fact]
    public void VanquishersBanner_CastMatchingCreatureSpell_DrawsOne()
    {
        SeedLibrary(_alice, "Top1");
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var banner = VanquishersBannerFactory.Create(
            _alice,
            typeChooser: _ => CardSubtype.Goblin,
            continuousEffects: null,
            triggers: triggers);
        _alice.Zones.Battlefield.AddCard(banner);
        banner.SetZone(ZoneType.Battlefield);

        var goblin = new Creature("Goblin Piledriver", "1R", 2, 2,
            subtypes: new[] { CardSubtype.Goblin }) { Owner = _alice };
        bus.Publish(new SpellCastEvent(new Majik.Core.Spells.Spell(goblin, _alice)));

        triggers.PendingCount.Should().Be(1,
            "Goblin creature spell matches the chosen type");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Select(c => c.Name).Should().ContainSingle()
            .Which.Should().Be("Top1");
    }

    [Fact]
    public void VanquishersBanner_CastDifferentTypeCreatureSpell_DoesNotDraw()
    {
        SeedLibrary(_alice, "Top1");
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var banner = VanquishersBannerFactory.Create(
            _alice,
            typeChooser: _ => CardSubtype.Goblin,
            continuousEffects: null,
            triggers: triggers);
        _alice.Zones.Battlefield.AddCard(banner);
        banner.SetZone(ZoneType.Battlefield);

        var merfolk = new Creature("Silvergill Adept", "1U", 2, 2,
            subtypes: new[] { CardSubtype.Merfolk }) { Owner = _alice };
        bus.Publish(new SpellCastEvent(new Majik.Core.Spells.Spell(merfolk, _alice)));

        triggers.PendingCount.Should().Be(0,
            "Merfolk doesn't share a subtype with chosen Goblin");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void VanquishersBanner_CastNonCreatureSpell_DoesNotDraw()
    {
        SeedLibrary(_alice, "Top1");
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var banner = VanquishersBannerFactory.Create(
            _alice,
            typeChooser: _ => CardSubtype.Goblin,
            continuousEffects: null,
            triggers: triggers);
        _alice.Zones.Battlefield.AddCard(banner);
        banner.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        bus.Publish(new SpellCastEvent(new Majik.Core.Spells.Spell(bolt, _alice)));

        triggers.PendingCount.Should().Be(0,
            "non-creature spell never matches the cast trigger");
    }

    [Fact]
    public void VanquishersBanner_OpponentCastsMatchingCreature_DoesNotDraw()
    {
        SeedLibrary(_alice, "Top1");
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var banner = VanquishersBannerFactory.Create(
            _alice,
            typeChooser: _ => CardSubtype.Goblin,
            continuousEffects: null,
            triggers: triggers);
        _alice.Zones.Battlefield.AddCard(banner);
        banner.SetZone(ZoneType.Battlefield);

        var goblin = new Creature("Goblin Piledriver", "1R", 2, 2,
            subtypes: new[] { CardSubtype.Goblin }) { Owner = _bob };
        bus.Publish(new SpellCastEvent(new Majik.Core.Spells.Spell(goblin, _bob)));

        triggers.PendingCount.Should().Be(0,
            "'whenever YOU cast' restricts the trigger to Banner's controller");
    }

    [Fact]
    public void VanquishersBanner_NoChosenType_CastTriggerNeverMatches()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var banner = VanquishersBannerFactory.Create(
            _alice,
            typeChooser: null,
            continuousEffects: null,
            triggers: triggers);
        _alice.Zones.Battlefield.AddCard(banner);
        banner.SetZone(ZoneType.Battlefield);

        var goblin = new Creature("Goblin Piledriver", "1R", 2, 2,
            subtypes: new[] { CardSubtype.Goblin }) { Owner = _alice };
        bus.Publish(new SpellCastEvent(new Majik.Core.Spells.Spell(goblin, _alice)));

        triggers.PendingCount.Should().Be(0,
            "no chosen type = predicate always false (deterministic no-op)");
    }

    private static void SeedLibrary(Player p, params string[] names)
    {
        foreach (var n in names)
        {
            var card = new Instant(n, "1") { Owner = p };
            p.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }
    }
}
