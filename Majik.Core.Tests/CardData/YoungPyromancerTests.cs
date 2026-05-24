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
using Majik.Core.Tokens;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Young Pyromancer (Magic 2014, {1}{R}, Creature — Human Shaman 2/1).
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatch hands back the correct shape.
///   - One triggered ability present on the card.
///   - Casting an instant → 1/1 Elemental token created on the battlefield.
///   - Casting a sorcery → 1/1 Elemental token created on the battlefield.
///   - Casting a creature spell → no token.
///   - Opponent casting an instant → no token for Alice.
/// </summary>
public class YoungPyromancerTests
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

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name = "Bear")
    {
        var creature = new Creature(name, "1G", 2, 2) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void YoungPyromancer_Identity_HumanShaman_2_1_AtCost1R()
    {
        var yp = YoungPyromancerFactory.Create(_alice);

        yp.Name.Should().Be("Young Pyromancer");
        yp.ManaCost.Should().Be("{1}{R}");
        yp.HasType(CardType.Creature).Should().BeTrue();
        yp.HasSubtype(CardSubtype.Human).Should().BeTrue();
        yp.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        yp.BasePower.Should().Be(2);
        yp.BaseToughness.Should().Be(1);
        yp.Owner.Should().BeSameAs(_alice);
        yp.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_YoungPyromancer()
    {
        var card = NamedCardFactory.Create("Young Pyromancer", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Young Pyromancer");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
    }

    [Fact]
    public void YoungPyromancer_HasOneTriggeredAbility()
    {
        var yp = YoungPyromancerFactory.Create(_alice);
        yp.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Token trigger — instant spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingInstant_CreatesOneElementalToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var yp = YoungPyromancerFactory.Create(_alice, bus, triggers);
        yp.SetZone(ZoneType.Battlefield);

        var battlefieldBefore = _alice.Zones.Battlefield.GetCards().Count();

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var battlefield = _alice.Zones.Battlefield.GetCards();
        battlefield.Should().HaveCount(battlefieldBefore + 1);
        var token = battlefield.OfType<Creature>().Last();
        token.IsToken.Should().BeTrue();
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        token.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Token trigger — sorcery spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingSorcery_CreatesOneElementalToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var yp = YoungPyromancerFactory.Create(_alice, bus, triggers);
        yp.SetZone(ZoneType.Battlefield);

        var battlefieldBefore = _alice.Zones.Battlefield.GetCards().Count();

        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice, "Lava Spike")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var battlefield = _alice.Zones.Battlefield.GetCards();
        battlefield.Should().HaveCount(battlefieldBefore + 1);
        var token = battlefield.OfType<Creature>().Last();
        token.IsToken.Should().BeTrue();
        token.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // No trigger on creature spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingCreatureSpell_DoesNotCreateToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var yp = YoungPyromancerFactory.Create(_alice, bus, triggers);
        yp.SetZone(ZoneType.Battlefield);

        var battlefieldBefore = _alice.Zones.Battlefield.GetCards().Count();

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0);
        _alice.Zones.Battlefield.GetCards().Count().Should().Be(battlefieldBefore);
    }

    // -----------------------------------------------------------------------
    // Opponent's cast does not trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void OpponentCastingInstant_DoesNotCreateToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var yp = YoungPyromancerFactory.Create(_alice, bus, triggers);
        yp.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "Bob's Bolt")));

        triggers.PendingCount.Should().Be(0);
    }
}
