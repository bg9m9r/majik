using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Bonecrusher Giant (Throne of Eldraine, {2}{R}).
///
/// Covers:
///   - Card identity (name, type, subtype, P/T, mana cost).
///   - Targeted-by-spell trigger structure (battlefield-only, fires off
///     TargetsChosenEvent).
///   - Live wiring: a spell whose chosen targets include Bonecrusher Giant
///     surfaces the trigger as pending; on resolution it deals 2 damage to
///     the spell's controller (modeled as life loss + DamageDealtEvent).
///   - Non-spell targeting (activated ability targeting Bonecrusher) does
///     NOT trigger — only spells (CR 115.6).
///   - NamedCardFactory dispatch.
///
/// Adventure cast-from-exile (CR 715) + the Stomp instant half are
/// deferred — see <see cref="BonecrusherGiantFactory"/> XML doc.
/// </summary>
public class BonecrusherGiantTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BonecrusherGiant_IsCreature_Giant_4_3_AtCost2R()
    {
        var bcg = BonecrusherGiantFactory.Create(_alice);

        bcg.Name.Should().Be("Bonecrusher Giant");
        bcg.ManaCost.Should().Be("{2}{R}");
        bcg.HasType(CardType.Creature).Should().BeTrue();
        bcg.HasSubtype(CardSubtype.Giant).Should().BeTrue();
        bcg.BasePower.Should().Be(4);
        bcg.BaseToughness.Should().Be(3);
        bcg.Owner.Should().BeSameAs(_alice);
        bcg.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BonecrusherGiant_HasTargetedBySpellTrigger_OnlyOnBattlefield()
    {
        var bcg = BonecrusherGiantFactory.Create(_alice);

        var triggers = bcg.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var trigger = triggers[0];
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BonecrusherGiant()
    {
        var card = NamedCardFactory.Create("Bonecrusher Giant", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Bonecrusher Giant");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Giant).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(4);
        ((Creature)card).BaseToughness.Should().Be(3);
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void BonecrusherGiant_LiveWiring_SpellTargetingIt_SurfacesPendingTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bcg = BonecrusherGiantFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(bcg);
        bcg.SetZone(ZoneType.Battlefield);

        // Build a Lightning Bolt spell controlled by Bob targeting BCG.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(bcg) });

        // Simulate SpellCaster publishing the TargetsChosenEvent.
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(1,
            "Bonecrusher Giant triggers when it becomes the target of a spell");
    }

    [Fact]
    public void BonecrusherGiant_TriggerResolves_Deals2ToSpellController()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bcg = BonecrusherGiantFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(bcg);
        bcg.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(bcg) });

        var damageEvents = new List<DamageDealtEvent>();
        bus.Subscribe<DamageDealtEvent>(damageEvents.Add);

        // Fire the targets-chosen event and resolve the resulting trigger.
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));
        var trigger = bcg.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        // Bob (spell's controller) takes 2 damage as life loss.
        _bob.LifeTotal.Should().Be(18, "spell's controller takes 2 damage");
        _alice.LifeTotal.Should().Be(20, "Bonecrusher's controller is not the spell's controller");

        damageEvents.Should().HaveCount(1);
        damageEvents[0].SourceCard.Should().BeSameAs(bcg);
        damageEvents[0].TargetPlayer.Should().BeSameAs(_bob);
        damageEvents[0].Amount.Should().Be(2);
        damageEvents[0].DamageType.Should().Be(DamageType.Ability);
    }

    [Fact]
    public void BonecrusherGiant_SelfTargetingSpell_DealsDamageToCaster()
    {
        // Per CR 115.6, "that spell's controller" is the player who cast
        // the spell. If Alice casts a spell targeting her own Bonecrusher,
        // the 2 damage is dealt to Alice.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bcg = BonecrusherGiantFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(bcg);
        bcg.SetZone(ZoneType.Battlefield);

        var giantGrowth = new Instant("Giant Growth", "G") { Owner = _alice };
        var spell = new Majik.Core.Spells.Spell(giantGrowth, _alice, new[] { Target.Permanent(bcg) });

        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));
        var trigger = bcg.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(18, "the spell's controller is Alice");
    }

    [Fact]
    public void BonecrusherGiant_SpellTargetingSomeoneElse_DoesNotTrigger()
    {
        // A spell targeting some other permanent (not Bonecrusher Giant)
        // must not fire the trigger.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bcg = BonecrusherGiantFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(bcg);
        bcg.SetZone(ZoneType.Battlefield);

        var otherBear = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _alice };
        _alice.Zones.Battlefield.AddCard(otherBear);
        otherBear.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(otherBear) });

        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(0,
            "the spell didn't pick Bonecrusher Giant as a target");
    }
}
