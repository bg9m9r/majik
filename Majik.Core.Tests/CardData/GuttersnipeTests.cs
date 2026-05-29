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
/// Tests for Guttersnipe (Return to Ravnica, {2}{R},
/// Creature — Goblin Shaman 2/2).
///
/// Oracle (verified against Scryfall): "Whenever you cast an instant or
/// sorcery spell, this creature deals 2 damage to each opponent."
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatch hands back the correct shape.
///   - One triggered ability present on the card.
///   - Casting an instant -> 2 damage to each opponent.
///   - Casting a sorcery -> 2 damage to each opponent.
///   - Casting a creature (noninstant/nonsorcery) spell -> no trigger.
///   - Casting an artifact spell -> no trigger.
///   - Opponent casting an instant -> no trigger for Alice.
///   - Multiplayer: damage hits every opponent, not the controller.
/// </summary>
public class GuttersnipeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Player _carol = new("Carol", 20);

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
    public void Guttersnipe_Identity_GoblinShaman_2_2_AtCost2R()
    {
        var snipe = GuttersnipeFactory.Create(_alice);

        snipe.Name.Should().Be("Guttersnipe");
        snipe.ManaCost.Should().Be("{2}{R}");
        snipe.HasType(CardType.Creature).Should().BeTrue();
        snipe.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        snipe.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        snipe.BasePower.Should().Be(2);
        snipe.BaseToughness.Should().Be(2);
        snipe.Owner.Should().BeSameAs(_alice);
        snipe.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Guttersnipe()
    {
        var card = NamedCardFactory.Create("Guttersnipe", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Guttersnipe");
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        card.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
    }

    [Fact]
    public void Guttersnipe_HasOneTriggeredAbility()
    {
        var snipe = GuttersnipeFactory.Create(_alice);
        snipe.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Damage trigger — instant spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingInstant_Deals2DamageToEachOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var snipe = GuttersnipeFactory.Create(
            _alice, () => new List<Player> { _alice, _bob }, bus, triggers);
        snipe.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(18);
        _alice.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Damage trigger — sorcery spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingSorcery_Deals2DamageToEachOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var snipe = GuttersnipeFactory.Create(
            _alice, () => new List<Player> { _alice, _bob }, bus, triggers);
        snipe.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice, "Lava Spike")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(18);
        _alice.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Multiplayer — every opponent, not the controller
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingInstant_Multiplayer_DamagesEveryOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var snipe = GuttersnipeFactory.Create(
            _alice, () => new List<Player> { _alice, _bob, _carol }, bus, triggers);
        snipe.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Opt")));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(18);
        _carol.LifeTotal.Should().Be(18);
        _alice.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // No trigger on noninstant/nonsorcery spells
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingCreatureSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var snipe = GuttersnipeFactory.Create(
            _alice, () => new List<Player> { _alice, _bob }, bus, triggers);
        snipe.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0);
        _bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void CastingArtifactSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var snipe = GuttersnipeFactory.Create(
            _alice, () => new List<Player> { _alice, _bob }, bus, triggers);
        snipe.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewArtifactSpell(_alice, "Mishra's Bauble")));

        triggers.PendingCount.Should().Be(0);
        _bob.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Opponent's cast does not trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void OpponentCastingInstant_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var snipe = GuttersnipeFactory.Create(
            _alice, () => new List<Player> { _alice, _bob }, bus, triggers);
        snipe.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "Bob's Bolt")));

        triggers.PendingCount.Should().Be(0);
        _alice.LifeTotal.Should().Be(20);
        _bob.LifeTotal.Should().Be(20);
    }
}
