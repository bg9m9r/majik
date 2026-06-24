using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Coruscation Mage (Bloomburrow, {1}{R}, Creature — Otter Wizard
/// 2/2).
///
/// Oracle (verified against Scryfall 2026-06-24):
///   "Offspring {2} (You may pay an additional {2} as you cast this spell. If
///    you do, when this creature enters, create a 1/1 token copy of it.)
///    Whenever you cast a noncreature spell, this creature deals 1 damage to
///    each opponent."
///
/// Covers ONLY the card's unique behaviour + an identity assert:
///   - Card identity (name, type, Otter + Wizard subtypes, 2/2, {1}{R}).
///   - Offspring keyword marker present (CR 702.169) + Offspring ETB trigger.
///   - Casting a noncreature spell (instant / sorcery / artifact / enchantment)
///     -> 1 damage to each opponent (CR 603.1 / 800.4).
///   - Casting a creature spell -> no trigger (noncreature predicate).
///   - Opponent casting a noncreature spell -> no trigger ("you cast").
///   - Two noncreature casts in a turn -> two pings.
///
/// (NamedCardFactory dispatch + well-formedness are asserted automatically for
/// every implemented card by CardFactoryContractTests — not re-tested here.)
/// </summary>
[Trait("Color", "R")]
public class CoruscationMageFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Bolt")
    {
        var instant = new Instant(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static Majik.Core.Spells.Spell NewSorcerySpell(Player controller, string name = "Lava")
    {
        var sorcery = new Sorcery(name, "1R") { Owner = controller };
        return new Majik.Core.Spells.Spell(sorcery, controller);
    }

    private static Majik.Core.Spells.Spell NewArtifactSpell(Player controller, string name = "Trinket")
    {
        var artifact = new Artifact(name, "1") { Owner = controller };
        return new Majik.Core.Spells.Spell(artifact, controller);
    }

    private static Majik.Core.Spells.Spell NewEnchantmentSpell(Player controller, string name = "Aura")
    {
        var enchantment = new Enchantment(name, "1W") { Owner = controller };
        return new Majik.Core.Spells.Spell(enchantment, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name = "Bear")
    {
        var creature = new Creature(name, "1G", 2, 2) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void CoruscationMage_Identity_OtterWizard_2_2_AtCost1R()
    {
        var card = CoruscationMageFactory.Create(_alice);

        card.Name.Should().Be("Coruscation Mage");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Otter).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CoruscationMage_HasOffspringKeywordMarker()
    {
        var card = CoruscationMageFactory.Create(_alice);

        // CR 702.169 — Offspring keyword marker surfaced for the keyword scan.
        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Offspring");
    }

    // -----------------------------------------------------------------------
    // Noncreature-cast ping
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingInstant_Deals1DamageToEachOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = CoruscationMageFactory.Create(_alice, triggers);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        Majik.Core.Tests.Helpers.ContextResolve.ResolveStackTop(stack, _alice, _alice, _bob);

        // CR 603.1 / CR 800.4 — 1 damage to each opponent; the controller is
        // never their own opponent.
        _bob.LifeTotal.Should().Be(19);
        _alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void CastingSorcery_Deals1DamageToEachOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = CoruscationMageFactory.Create(_alice, triggers);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice, "Lava Spike")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        Majik.Core.Tests.Helpers.ContextResolve.ResolveStackTop(stack, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(19);
    }

    [Fact]
    public void CastingArtifactSpell_Deals1DamageToEachOpponent()
    {
        // Unlike Electrostatic Field (instant/sorcery only), Coruscation Mage
        // fires on ANY noncreature spell — an artifact spell triggers it.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = CoruscationMageFactory.Create(_alice, triggers);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewArtifactSpell(_alice, "Mishra's Bauble")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        Majik.Core.Tests.Helpers.ContextResolve.ResolveStackTop(stack, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(19);
    }

    [Fact]
    public void CastingEnchantmentSpell_Deals1DamageToEachOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = CoruscationMageFactory.Create(_alice, triggers);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewEnchantmentSpell(_alice, "Pacifism")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        Majik.Core.Tests.Helpers.ContextResolve.ResolveStackTop(stack, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(19);
    }

    // -----------------------------------------------------------------------
    // Creature spell does not trigger (noncreature predicate, CR 110.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingCreatureSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = CoruscationMageFactory.Create(_alice, triggers);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0);
        _bob.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Opponent's cast does not trigger ("you cast")
    // -----------------------------------------------------------------------

    [Fact]
    public void OpponentCastingNoncreatureSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = CoruscationMageFactory.Create(_alice, triggers);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "Bob's Bolt")));

        triggers.PendingCount.Should().Be(0);
        _alice.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Two noncreature casts in a turn -> two independent pings
    // -----------------------------------------------------------------------

    [Fact]
    public void TwoNoncreatureCasts_PingTwice()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = CoruscationMageFactory.Create(_alice, triggers);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Bolt #1")));
        triggers.PutPendingTriggersOnStack(_alice);
        Majik.Core.Tests.Helpers.ContextResolve.ResolveStackTop(stack, _alice, _alice, _bob);
        _bob.LifeTotal.Should().Be(19);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Bolt #2")));
        triggers.PutPendingTriggersOnStack(_alice);
        Majik.Core.Tests.Helpers.ContextResolve.ResolveStackTop(stack, _alice, _alice, _bob);
        _bob.LifeTotal.Should().Be(18);
    }
}
