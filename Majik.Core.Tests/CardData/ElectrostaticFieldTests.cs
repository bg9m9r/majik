using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Electrostatic Field (Guilds of Ravnica, {1}{R},
/// Creature — Wall 0/4).
///
/// Oracle (verified against Scryfall):
///   "Defender
///    Whenever you cast an instant or sorcery spell, this creature deals
///    1 damage to each opponent."
///
/// Covers:
///   - Card identity (name, type, subtype Wall, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatch hands back the correct shape.
///   - Defender keyword marker present (CR 702.3).
///   - One triggered ability present on the card.
///   - Casting an instant -> 1 damage to each opponent.
///   - Casting a sorcery -> 1 damage to each opponent.
///   - Casting a noncreature non-instant/sorcery (artifact) spell -> no trigger.
///   - Casting a creature spell -> no trigger.
///   - Opponent casting an instant -> no trigger for Alice.
/// </summary>
public class ElectrostaticFieldTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private List<Player> AllPlayers => new() { _alice, _bob };

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
    public void ElectrostaticField_Identity_Wall_0_4_AtCost1R()
    {
        var ef = ElectrostaticFieldFactory.Create(_alice);

        ef.Name.Should().Be("Electrostatic Field");
        ef.ManaCost.Should().Be("{1}{R}");
        ef.HasType(CardType.Creature).Should().BeTrue();
        ef.HasSubtype(CardSubtype.Wall).Should().BeTrue();
        ef.BasePower.Should().Be(0);
        ef.BaseToughness.Should().Be(4);
        ef.Owner.Should().BeSameAs(_alice);
        ef.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ElectrostaticField()
    {
        var card = NamedCardFactory.Create("Electrostatic Field", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Electrostatic Field");
        card.HasSubtype(CardSubtype.Wall).Should().BeTrue();
    }

    [Fact]
    public void ElectrostaticField_HasDefenderKeyword()
    {
        var ef = ElectrostaticFieldFactory.Create(_alice);

        // CR 702.3 — Defender keyword marker; surfaced for block legality.
        CombatAbilities.HasDefender(ef).Should().BeTrue();
    }

    [Fact]
    public void ElectrostaticField_HasOneTriggeredAbility()
    {
        var ef = ElectrostaticFieldFactory.Create(_alice);
        ef.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Token trigger — instant spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingInstant_Deals1DamageToEachOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ef = ElectrostaticFieldFactory.Create(_alice, triggers, () => AllPlayers);
        ef.SetZone(ZoneType.Battlefield);

        _bob.LifeTotal.Should().Be(20);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(19);
        _alice.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Token trigger — sorcery spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingSorcery_Deals1DamageToEachOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ef = ElectrostaticFieldFactory.Create(_alice, triggers, () => AllPlayers);
        ef.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice, "Lava Spike")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(19);
    }

    // -----------------------------------------------------------------------
    // No trigger on noncreature non-instant/sorcery (artifact) spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingArtifactSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ef = ElectrostaticFieldFactory.Create(_alice, triggers, () => AllPlayers);
        ef.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewArtifactSpell(_alice, "Mishra's Bauble")));

        triggers.PendingCount.Should().Be(0);
        _bob.LifeTotal.Should().Be(20);
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

        var ef = ElectrostaticFieldFactory.Create(_alice, triggers, () => AllPlayers);
        ef.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0);
        _bob.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Opponent's cast does not trigger ("you cast")
    // -----------------------------------------------------------------------

    [Fact]
    public void OpponentCastingInstant_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ef = ElectrostaticFieldFactory.Create(_alice, triggers, () => AllPlayers);
        ef.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "Bob's Bolt")));

        triggers.PendingCount.Should().Be(0);
        _alice.LifeTotal.Should().Be(20);
    }
}
