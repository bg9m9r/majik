using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Kiln Fiend (Rise of the Eldrazi, {1}{R}).
///
/// Card: Creature — Elemental Beast 1/2.
///   "Whenever you cast an instant or sorcery spell, this creature gets
///    +3/+0 until end of turn."
///
/// Direct functional sibling of Festival Crasher (+3/+0 vs +2/+0) and the
/// same SpellCastEvent → end-of-turn pump shape as Soul-Scar Mage's prowess.
///
/// Covers:
///   - Identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatcher entry.
///   - Cast trigger wired as a TriggeredAbility when an effects service is
///     supplied; not wired on the single-arg shape-only path.
///   - The +3/+0 pump registers a Layer-7c continuous effect that expires at
///     end of turn (CR 613 Layer 7c, CR 603.1).
///   - The trigger fires on the controller's instant/sorcery, not on a
///     creature spell, and not on an opponent's instant/sorcery.
/// </summary>
[Trait("Color", "R")]
public class KilnFiendFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void KilnFiend_Identity()
    {
        var c = KilnFiendFactory.Create(_alice);

        c.Name.Should().Be("Kiln Fiend");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Cast trigger wiring
    // -----------------------------------------------------------------------

    [Fact]
    public void SingleArg_ShapeOnly_DoesNotWireCastTrigger()
    {
        var c = KilnFiendFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "single-arg dispatcher path is shape-only — no cast trigger");
    }

    [Fact]
    public void WithEffectsService_AttachesCastTrigger()
    {
        var effects = new ContinuousEffectsService();
        var c = KilnFiendFactory.Create(_alice, effects, triggers: null);

        c.Abilities.OfType<TriggeredAbility>()
            .Should().ContainSingle("the cast trigger wires when a ContinuousEffectsService is supplied");
    }

    // -----------------------------------------------------------------------
    // +3/+0 pump
    // -----------------------------------------------------------------------

    [Fact]
    public void Pump_GivesPlusThreeZero_UntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var fiend = KilnFiendFactory.Create(_alice, effects, triggers: null);
        fiend.ActiveEffects = effects;

        fiend.Power.Should().Be(1);
        fiend.Toughness.Should().Be(2);

        // Resolve the cast trigger once (controller casts an instant/sorcery).
        FireCastTrigger(fiend);

        fiend.Power.Should().Be(4, "Kiln Fiend gets +3/+0 on each instant/sorcery cast");
        fiend.Toughness.Should().Be(2, "the pump is +3/+0 — toughness is unchanged");

        // Stacks per cast (CR 603.1 — each instance is a separate trigger).
        FireCastTrigger(fiend);
        fiend.Power.Should().Be(7);
        fiend.Toughness.Should().Be(2);
    }

    [Fact]
    public void Pump_ExpiresAtEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var fiend = KilnFiendFactory.Create(_alice, effects, triggers: null);
        fiend.ActiveEffects = effects;

        FireCastTrigger(fiend);
        fiend.Power.Should().Be(4);

        effects.ExpireEndOfTurn();

        fiend.Power.Should().Be(1, "the +3/+0 pump is 'until end of turn'");
        fiend.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Trigger predicate (via the live TriggeredAbility condition)
    // -----------------------------------------------------------------------

    [Fact]
    public void Trigger_DoesNotFire_OnCreatureSpell()
    {
        var effects = new ContinuousEffectsService();
        var fiend = KilnFiendFactory.Create(_alice, effects, triggers: null);
        var trigger = fiend.Abilities.OfType<TriggeredAbility>().Single();

        var creatureSpell = SpellOf(_alice, "Grizzly Bears", CardType.Creature);
        trigger.Condition.Matches(
            new Majik.Core.Domain.DomainEvents.SpellCastEvent(creatureSpell), trigger)
            .Should().BeFalse("a creature spell is not an instant or sorcery");
    }

    [Fact]
    public void Trigger_DoesNotFire_OnOpponentsInstant()
    {
        var effects = new ContinuousEffectsService();
        var fiend = KilnFiendFactory.Create(_alice, effects, triggers: null);
        var trigger = fiend.Abilities.OfType<TriggeredAbility>().Single();

        var bobInstant = SpellOf(_bob, "Lightning Bolt", CardType.Instant);
        trigger.Condition.Matches(
            new Majik.Core.Domain.DomainEvents.SpellCastEvent(bobInstant), trigger)
            .Should().BeFalse("the trigger is 'whenever you cast' — opponent casts don't count");
    }

    [Fact]
    public void Trigger_Fires_OnControllersInstantAndSorcery()
    {
        var effects = new ContinuousEffectsService();
        var fiend = KilnFiendFactory.Create(_alice, effects, triggers: null);
        var trigger = fiend.Abilities.OfType<TriggeredAbility>().Single();

        var instant = SpellOf(_alice, "Lightning Bolt", CardType.Instant);
        var sorcery = SpellOf(_alice, "Lava Spike", CardType.Sorcery);

        trigger.Condition.Matches(
            new Majik.Core.Domain.DomainEvents.SpellCastEvent(instant), trigger)
            .Should().BeTrue();
        trigger.Condition.Matches(
            new Majik.Core.Domain.DomainEvents.SpellCastEvent(sorcery), trigger)
            .Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void FireCastTrigger(Creature fiend)
    {
        var trigger = fiend.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects)
        {
            effect.Execute();
        }
    }

    private static Majik.Core.Spells.Spell SpellOf(Player controller, string name, CardType type)
    {
        Card card = type switch
        {
            CardType.Instant => new Instant(name, "{R}"),
            CardType.Sorcery => new Sorcery(name, "{R}"),
            _ => new Creature(name, "{1}{G}", 2, 2),
        };
        card.SetOwner(controller);
        card.SetController(controller);
        return new Majik.Core.Spells.Spell(card, controller);
    }
}
