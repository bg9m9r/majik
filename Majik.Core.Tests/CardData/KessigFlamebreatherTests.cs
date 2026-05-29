using System.Collections.Generic;
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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Kessig Flamebreather (Midnight Hunt, {1}{R},
/// Creature — Human Shaman 1/3).
///
/// Oracle (verified against Scryfall): "Whenever you cast a noncreature
/// spell, this creature deals 1 damage to each opponent."
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatch hands back the correct shape.
///   - One triggered ability present on the card.
///   - Casting an instant → 1 damage to each opponent.
///   - Casting a sorcery → 1 damage to each opponent.
///   - Casting an artifact (noncreature) spell → 1 damage.
///   - Casting a creature spell → no trigger, no damage.
///   - Opponent casting a noncreature spell → no trigger for Alice.
/// </summary>
public class KessigFlamebreatherTests
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
    public void KessigFlamebreather_Identity_HumanShaman_1_3_AtCost1R()
    {
        var card = KessigFlamebreatherFactory.Create(_alice);

        card.Name.Should().Be("Kessig Flamebreather");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_KessigFlamebreather()
    {
        var card = NamedCardFactory.Create("Kessig Flamebreather", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Kessig Flamebreather");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
    }

    [Fact]
    public void KessigFlamebreather_HasOneTriggeredAbility()
    {
        var card = KessigFlamebreatherFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Damage trigger — instant spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingInstant_Deals1DamageToEachOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = KessigFlamebreatherFactory.Create(
            _alice,
            opponentResolver: () => new List<Player> { _alice, _bob },
            eventBus: bus,
            triggers: triggers);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // CR 800.4 — each opponent (Bob) takes 1 damage; Alice (controller) does not.
        _bob.LifeTotal.Should().Be(19);
        _alice.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Damage trigger — sorcery spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingSorcery_Deals1DamageToEachOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = KessigFlamebreatherFactory.Create(
            _alice,
            opponentResolver: () => new List<Player> { _alice, _bob },
            eventBus: bus,
            triggers: triggers);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice, "Lava Spike")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(19);
    }

    // -----------------------------------------------------------------------
    // Damage trigger — noncreature artifact spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingNoncreatureArtifactSpell_Deals1DamageToEachOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = KessigFlamebreatherFactory.Create(
            _alice,
            opponentResolver: () => new List<Player> { _alice, _bob },
            eventBus: bus,
            triggers: triggers);
        card.SetZone(ZoneType.Battlefield);

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
    public void CastingCreatureSpell_DoesNotTriggerOrDealDamage()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = KessigFlamebreatherFactory.Create(
            _alice,
            opponentResolver: () => new List<Player> { _alice, _bob },
            eventBus: bus,
            triggers: triggers);
        card.SetZone(ZoneType.Battlefield);

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

        var card = KessigFlamebreatherFactory.Create(
            _alice,
            opponentResolver: () => new List<Player> { _alice, _bob },
            eventBus: bus,
            triggers: triggers);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "Bob's Bolt")));

        triggers.PendingCount.Should().Be(0);
        _alice.LifeTotal.Should().Be(20);
        _bob.LifeTotal.Should().Be(20);
    }
}
