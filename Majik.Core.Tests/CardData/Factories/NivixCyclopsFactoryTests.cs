using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="NivixCyclopsFactory"/>.
///
/// Nivix Cyclops — {1}{U}{R} Creature — Cyclops 1/4:
///   "Defender
///    Whenever you cast an instant or sorcery spell, this creature gets +3/+0
///    until end of turn and can attack this turn as though it didn't have
///    defender."
/// </summary>
[Trait("Color", "UR")]
public class NivixCyclopsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void NivixCyclops_IsCyclops_1_4_ManaValue3_WithDefender()
    {
        var card = NivixCyclopsFactory.Create(_alice);

        card.Should().BeOfType<Creature>();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Name.Should().Be("Nivix Cyclops");
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(4);
        card.ManaCostValue.TotalValue.Should().Be(3, "{1}{U}{R} is mana value 3");
        card.HasSubtype(CardSubtype.Cyclops).Should().BeTrue();
        CombatAbilities.HasDefender(card).Should().BeTrue("Defender keyword");
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
    }

    [Fact]
    public void NivixCyclops_CannotAttackByDefault()
    {
        var card = NivixCyclopsFactory.Create(_alice);
        card.HasSummoningSickness = false;

        // CR 702.3b — defender forbids attacking until the cast rider grants it.
        BlockLegality.CanAttack(card, out var reason).Should().BeFalse();
        reason.Should().Contain("defender");
        card.CanAttackAsThoughItDidntHaveDefenderThisTurn.Should().BeFalse();
    }

    [Fact]
    public void OnInstantCast_GrantsPlus3Plus0AndAttackAsThoughNoDefender()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var cyclops = NivixCyclopsFactory.Create(_alice, triggers, effects);
        cyclops.SetZone(ZoneType.Battlefield);
        cyclops.ClearSummoningSickness();

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        cyclops.Power.Should().Be(4, "+3/+0 from the cast rider (1 base + 3)");
        cyclops.Toughness.Should().Be(4, "+0 toughness");
        cyclops.CanAttackAsThoughItDidntHaveDefenderThisTurn.Should().BeTrue(
            "the rider grants attacking as though no defender (CR 508.1a)");
        BlockLegality.CanAttack(cyclops, out _).Should().BeTrue(
            "with the grant, the defender creature may attack");
    }

    [Fact]
    public void OnSorceryCast_GrantsAttackAsThoughNoDefender()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var cyclops = NivixCyclopsFactory.Create(_alice, triggers, effects);
        cyclops.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice, "Divination")));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        cyclops.CanAttackAsThoughItDidntHaveDefenderThisTurn.Should().BeTrue();
        cyclops.Power.Should().Be(4);
    }

    [Fact]
    public void OnCreatureCast_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var cyclops = NivixCyclopsFactory.Create(_alice, triggers, effects);
        cyclops.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0, "a creature spell is not an instant/sorcery");
        cyclops.CanAttackAsThoughItDidntHaveDefenderThisTurn.Should().BeFalse();
        cyclops.Power.Should().Be(1, "no pump from a creature cast");
    }

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Bolt")
    {
        var instant = new Instant(name, "{R}") { Owner = controller, Controller = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static Majik.Core.Spells.Spell NewSorcerySpell(Player controller, string name = "Divination")
    {
        var sorcery = new Sorcery(name, "{2}{U}") { Owner = controller, Controller = controller };
        return new Majik.Core.Spells.Spell(sorcery, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name = "Bear")
    {
        var bear = new Creature(name, "{1}{G}", 2, 2) { Owner = controller, Controller = controller };
        return new Majik.Core.Spells.Spell(bear, controller);
    }
}
