using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FavoredHopliteFactory"/>.
///
/// Covers:
/// - Identity (name, type Creature, P/T 1/2, Human + Soldier subtypes,
///   mana cost {W}, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Heroic trigger:
///   - Targeted spell from controller → trigger fires, +1/+1 counter
///     placed, damage-prevention shield registered.
///   - Untargeted spell from controller → no trigger.
///   - Spell targeting a different permanent → no trigger.
///   - Targeted spell from opponent → no trigger (controller scope).
///   - Damage to Hoplite is prevented for the turn while the shield is up.
///   - Damage to another creature is unaffected.
///   - Shield drops at end of turn.
/// </summary>
[Trait("Color", "W")]
public class FavoredHopliteFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Spark",
        params ITarget[] targets)
    {
        var instant = new Instant(name, "W") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller, targets);
    }

    [Fact]
    public void FavoredHoplite_Identity()
    {
        var fh = FavoredHopliteFactory.Create(_alice);

        fh.Name.Should().Be("Favored Hoplite");
        fh.ManaCost.Should().Be("{W}");
        fh.HasType(CardType.Creature).Should().BeTrue();
        fh.HasSubtype(CardSubtype.Human).Should().BeTrue();
        fh.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        fh.BasePower.Should().Be(1);
        fh.BaseToughness.Should().Be(2);
        fh.Owner.Should().BeSameAs(_alice);
        fh.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void HeroicTrigger_TargetedSpell_AddsCounterAndPlacesShield()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var replacements = new ReplacementBus();

        var fh = FavoredHopliteFactory.Create(_alice, triggers, replacements);
        fh.SetZone(ZoneType.Battlefield);
        fh.ActiveEffects = new ContinuousEffectsService();

        fh.Power.Should().Be(1);
        fh.Toughness.Should().Be(2);

        // Cast a spell that targets Hoplite.
        bus.Publish(new SpellCastEvent(
            NewInstantSpell(_alice, "Boon", Target.Permanent(fh))));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // +1/+1 counter placed.
        fh.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        fh.Power.Should().Be(2);
        fh.Toughness.Should().Be(3);

        // Shield is on the bus — damage to Hoplite cancels.
        var src = new Creature("src", "", 5, 5);
        replacements.Apply(new DamageIntent(src, 5, TargetCreature: fh))
            .Should().BeNull("Heroic prevention shield is up for the turn");
    }

    [Fact]
    public void HeroicTrigger_UntargetedSpell_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var replacements = new ReplacementBus();

        var fh = FavoredHopliteFactory.Create(_alice, triggers, replacements);
        fh.SetZone(ZoneType.Battlefield);
        fh.ActiveEffects = new ContinuousEffectsService();

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Tormenting Voice")));
        triggers.PendingCount.Should().Be(0);
        fh.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void HeroicTrigger_SpellTargetingDifferentPermanent_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fh = FavoredHopliteFactory.Create(_alice, triggers);
        fh.SetZone(ZoneType.Battlefield);

        var other = new Creature("Other", "", 2, 2) { Owner = _alice, Controller = _alice };
        bus.Publish(new SpellCastEvent(
            NewInstantSpell(_alice, "Bolt", Target.Permanent(other))));

        triggers.PendingCount.Should().Be(0,
            "Heroic checks targets reference Hoplite specifically");
    }

    [Fact]
    public void HeroicTrigger_OpponentCastTargetingHoplite_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fh = FavoredHopliteFactory.Create(_alice, triggers);
        fh.SetZone(ZoneType.Battlefield);

        // Bob (opponent) casts a spell targeting Hoplite (e.g. Shock).
        // Heroic is controller-scoped — Alice cast nothing, so no trigger.
        var bobsSpell = new Instant("Shock", "R") { Owner = _bob };
        bus.Publish(new SpellCastEvent(
            new Majik.Core.Spells.Spell(bobsSpell, _bob, new[] { Target.Permanent(fh) })));

        triggers.PendingCount.Should().Be(0,
            "Heroic is 'whenever YOU cast a spell that targets …'");
    }

    [Fact]
    public void HeroicShield_DoesNotPreventDamageToOtherCreatures()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var replacements = new ReplacementBus();

        var fh = FavoredHopliteFactory.Create(_alice, triggers, replacements);
        fh.SetZone(ZoneType.Battlefield);
        fh.ActiveEffects = new ContinuousEffectsService();

        bus.Publish(new SpellCastEvent(
            NewInstantSpell(_alice, "Boon", Target.Permanent(fh))));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Shield is creature-scoped; damage to another creature still applies.
        var other = new Creature("Other", "", 2, 2) { Owner = _alice, Controller = _alice };
        var src = new Creature("src", "", 3, 3);
        var result = replacements.Apply(new DamageIntent(src, 3, TargetCreature: other));
        result.Should().NotBeNull();
        result!.Amount.Should().Be(3);
    }

    [Fact]
    public void HeroicShield_ExpiresAtEndOfTurn()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var replacements = new ReplacementBus();

        var fh = FavoredHopliteFactory.Create(_alice, triggers, replacements);
        fh.SetZone(ZoneType.Battlefield);
        fh.ActiveEffects = new ContinuousEffectsService();

        bus.Publish(new SpellCastEvent(
            NewInstantSpell(_alice, "Boon", Target.Permanent(fh))));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        replacements.ExpireEndOfTurn();

        var src = new Creature("src", "", 5, 5);
        var result = replacements.Apply(new DamageIntent(src, 5, TargetCreature: fh));
        result.Should().NotBeNull("EOT cleanup drops the Heroic shield");
        result!.Amount.Should().Be(5);
    }

    [Fact]
    public void SpellTargetsCreature_HelperPredicate()
    {
        var fh = FavoredHopliteFactory.Create(_alice);
        var other = new Creature("Other", "", 2, 2) { Owner = _alice, Controller = _alice };

        FavoredHopliteFactory.SpellTargetsCreature(
            new ITarget[] { Target.Permanent(fh) }, fh).Should().BeTrue();
        FavoredHopliteFactory.SpellTargetsCreature(
            new ITarget[] { Target.Permanent(other) }, fh).Should().BeFalse();
        FavoredHopliteFactory.SpellTargetsCreature(
            new ITarget[] { Target.Player(_alice) }, fh).Should().BeFalse();
        FavoredHopliteFactory.SpellTargetsCreature(
            Array.Empty<ITarget>(), fh).Should().BeFalse();
    }
}
