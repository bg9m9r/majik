using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
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
/// Unit tests for <see cref="FearOfSurveillanceFactory"/>.
///
/// Oracle (Scryfall, verified 2026-06-24): {1}{W} Enchantment Creature —
/// Nightmare 2/2,
///   "Vigilance
///    Whenever this creature attacks, surveil 1."
///
/// Covers card identity, the evergreen Vigilance keyword (CR 702.20), the
/// declarative attacks-trigger surveil ability shape (CR 508.1f + CR 701.42),
/// and that the trigger fires only on this creature's own attack and surveils
/// the peeked card to the graveyard at resolution under the no-agent default.
/// </summary>
[Trait("Color", "W")]
public class FearOfSurveillanceFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void FearOfSurveillance_HasExpectedIdentity()
    {
        var creature = (Creature)NamedCardFactory.Create("Fear of Surveillance", _alice);

        creature.Name.Should().Be("Fear of Surveillance");
        creature.HasType(CardType.Creature).Should().BeTrue();
        creature.HasType(CardType.Enchantment).Should().BeTrue();
        creature.Subtypes.Should().Contain(CardSubtype.Nightmare);
        creature.ManaCost.Should().Be("{1}{W}");
        creature.Power.Should().Be(2);
        creature.Toughness.Should().Be(2);
        // CR 702.20 — Vigilance is carried declaratively via the keywords array.
        creature.HasEffectiveKeyword("Vigilance").Should().BeTrue("CR 702.20");
    }

    [Fact]
    public void FearOfSurveillance_HasSingleAttackTriggeredAbility()
    {
        var creature = (Creature)NamedCardFactory.Create("Fear of Surveillance", _alice);

        creature.Abilities.OfType<TriggeredAbility>()
            .Should().ContainSingle("the only triggered ability is the attacks-surveil trigger");
    }

    [Fact]
    public void FearOfSurveillance_Attacks_FiresSurveilTrigger_AndPeekedCardGoesToGraveyard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var creature = (Creature)NamedCardFactory.Create("Fear of Surveillance", _alice);
        _alice.Zones.Battlefield.AddCard(creature);
        creature.SetZone(ZoneType.Battlefield);
        var ability = creature.Abilities.OfType<TriggeredAbility>().Single();
        triggers.RegisterTriggeredAbility(ability);

        var top = new Card("Top", ""); top.SetOwner(_alice);
        var second = new Card("Second", ""); second.SetOwner(_alice);
        foreach (var c in new[] { top, second })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        // CR 508.1f — this creature attacking fires its own attack trigger.
        bus.Publish(new CreatureAttacksEvent(creature, _bob));
        triggers.PendingCount.Should().Be(1,
            "this creature's attack fires its own attacks trigger (CR 508.1f)");

        triggers.PutPendingTriggersOnStack(_alice);
        while (true)
        {
            var obj = stack.Pop();
            if (obj == null) break;
            obj.Resolve();
        }

        // CR 701.42 — with no agent registered the surveil default sends the
        // peeked card to the graveyard; the second card stays on the library.
        _alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { second });
    }

    [Fact]
    public void FearOfSurveillance_DoesNotFire_WhenAnotherCreatureAttacks()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var creature = (Creature)NamedCardFactory.Create("Fear of Surveillance", _alice);
        _alice.Zones.Battlefield.AddCard(creature);
        creature.SetZone(ZoneType.Battlefield);
        triggers.RegisterTriggeredAbility(creature.Abilities.OfType<TriggeredAbility>().Single());

        var other = new Creature("Other Attacker", "1R", 2, 2) { Owner = _alice };
        bus.Publish(new CreatureAttacksEvent(other, _bob));

        triggers.PendingCount.Should().Be(0,
            "the attacks_self trigger is a per-attacker self trigger (CR 508.1f)");
    }
}
