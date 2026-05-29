using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FirebrandArcherFactory"/> (Hour of Devastation,
/// {1}{R}, Creature — Human Archer 2/1).
///
/// Oracle (verified against Scryfall): "Whenever you cast a noncreature spell,
/// this creature deals 1 damage to each opponent." (Functional reprint of
/// Kessig Flamebreather.)
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatch hands back the correct shape.
///   - One triggered ability present on the card.
///   - Casting an instant → 1 damage to each opponent.
///   - Casting a sorcery → 1 damage to each opponent.
///   - Casting an artifact (noncreature) spell → 1 damage to each opponent.
///   - Casting a creature spell → no trigger.
///   - Opponent casting a noncreature spell → no trigger for Alice.
///   - No opponent resolver → trigger fires but burn half no-ops.
/// </summary>
public class FirebrandArcherFactoryTests
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

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name = "Bear")
    {
        var creature = new Creature(name, "1G", 2, 2) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void FirebrandArcher_Identity_HumanArcher_2_1_AtCost1R()
    {
        var fa = FirebrandArcherFactory.Create(_alice);

        fa.Name.Should().Be("Firebrand Archer");
        fa.ManaCost.Should().Be("{1}{R}");
        fa.HasType(CardType.Creature).Should().BeTrue();
        fa.HasSubtype(CardSubtype.Human).Should().BeTrue();
        fa.HasSubtype(CardSubtype.Archer).Should().BeTrue();
        fa.BasePower.Should().Be(2);
        fa.BaseToughness.Should().Be(1);
        fa.Owner.Should().BeSameAs(_alice);
        fa.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FirebrandArcher()
    {
        var card = NamedCardFactory.Create("Firebrand Archer", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Firebrand Archer");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Archer).Should().BeTrue();
    }

    [Fact]
    public void FirebrandArcher_HasOneTriggeredAbility()
    {
        var fa = FirebrandArcherFactory.Create(_alice);
        fa.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Burn trigger — instant spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingInstant_DealsOneDamageToEachOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fa = FirebrandArcherFactory.Create(
            _alice, bus, triggers, opponentResolver: () => new[] { _bob });
        fa.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // CR 119.3 — damage to a player is life loss.
        _bob.LifeTotal.Should().Be(19);
    }

    // -----------------------------------------------------------------------
    // Burn trigger — sorcery spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingSorcery_DealsOneDamageToEachOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fa = FirebrandArcherFactory.Create(
            _alice, bus, triggers, opponentResolver: () => new[] { _bob });
        fa.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice, "Lava Spike")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(19);
    }

    // -----------------------------------------------------------------------
    // Burn trigger — noncreature artifact spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingNoncreatureArtifactSpell_DealsOneDamageToEachOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fa = FirebrandArcherFactory.Create(
            _alice, bus, triggers, opponentResolver: () => new[] { _bob });
        fa.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewArtifactSpell(_alice, "Mishra's Bauble")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(19);
    }

    // -----------------------------------------------------------------------
    // No trigger on creature spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingCreatureSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fa = FirebrandArcherFactory.Create(
            _alice, bus, triggers, opponentResolver: () => new[] { _bob });
        fa.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0);
        _bob.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Opponent's cast does not trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void OpponentCastingNoncreatureSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fa = FirebrandArcherFactory.Create(
            _alice, bus, triggers, opponentResolver: () => new[] { _bob });
        fa.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "Bob's Bolt")));

        triggers.PendingCount.Should().Be(0);
        _bob.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Burn half no-ops without a resolver
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingNoncreatureSpell_WithoutResolver_TriggersButNoDamage()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fa = FirebrandArcherFactory.Create(
            _alice, bus, triggers, opponentResolver: null);
        fa.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(20);
    }
}
