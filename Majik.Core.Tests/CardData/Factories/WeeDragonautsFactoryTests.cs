using System.Linq;
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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Wee Dragonauts (Guildpact, {1}{U}{R},
/// Creature — Faerie Wizard 1/3).
///
/// Oracle text (verified against Scryfall):
///   "Flying
///    Whenever you cast an instant or sorcery spell, this creature gets
///    +2/+0 until end of turn."
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - Flying keyword marker attached (CR 702.9).
///   - NamedCardFactory dispatch hands back the correct shape.
///   - Casting an instant → +2/+0 EOT (power = 3, toughness = 3).
///   - Casting a sorcery → +2/+0 EOT.
///   - Casting a creature spell → no pump (instant/sorcery only).
///   - Two instant/sorcery casts in one turn → +4/+0 stacks (power = 5).
/// </summary>
[Trait("Color", "M")]
public class WeeDragonautsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Bolt")
    {
        var instant = new Instant(name, "{R}") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static Majik.Core.Spells.Spell NewSorcerySpell(Player controller, string name = "Divination")
    {
        var sorcery = new Sorcery(name, "{U}") { Owner = controller };
        return new Majik.Core.Spells.Spell(sorcery, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name = "Bear")
    {
        var creature = new Creature(name, "{1}{G}", 2, 2) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WeeDragonauts_Identity_FaerieWizard_1_3_AtCost1UR()
    {
        var card = WeeDragonautsFactory.Create(_alice);

        card.Name.Should().Be("Wee Dragonauts");
        card.ManaCost.Should().Be("{1}{U}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WeeDragonauts_HasFlyingKeywordMarker()
    {
        var card = WeeDragonautsFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Flying",
                "CR 702.9 — Flying is a printed keyword ability on Wee Dragonauts");
    }

    [Fact]
    public void WeeDragonauts_HasOneCastTriggeredAbility()
    {
        var card = WeeDragonautsFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Cast-instant/sorcery pump
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingInstantSpell_PumpsPlus2Plus0EOT()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = WeeDragonautsFactory.Create(_alice, bus, triggers, effects);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // CR 514.2 / Layer 7c — +2/+0 until end of turn.
        card.Power.Should().Be(3);
        card.Toughness.Should().Be(3);
    }

    [Fact]
    public void CastingSorcerySpell_PumpsPlus2Plus0EOT()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = WeeDragonautsFactory.Create(_alice, bus, triggers, effects);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice, "Divination")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        card.Power.Should().Be(3);
        card.Toughness.Should().Be(3);
    }

    [Fact]
    public void CastingCreatureSpell_NoPump()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = WeeDragonautsFactory.Create(_alice, bus, triggers, effects);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0);
        card.Power.Should().Be(1);
        card.Toughness.Should().Be(3);
    }

    [Fact]
    public void CastingMultipleInstantSorcerySpells_PumpStacksAdditively()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var effects = new ContinuousEffectsService();

        var card = WeeDragonautsFactory.Create(_alice, bus, triggers, effects);
        card.SetZone(ZoneType.Battlefield);

        // First instant cast → +2/+0.
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Bolt #1")));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();
        card.Power.Should().Be(3);

        // Second cast (sorcery) → another +2/+0 stacks additively per CR 613.
        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice, "Divination")));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();
        card.Power.Should().Be(5);

        // Toughness unaffected by the +0 toughness portion.
        card.Toughness.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void WeeDragonauts_DispatchesThroughNamedFactory()
    {
        var card = NamedCardFactory.Create("Wee Dragonauts", _alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Wee Dragonauts");
        ((Creature)card).HasSubtype(CardSubtype.Faerie).Should().BeTrue();
    }
}
