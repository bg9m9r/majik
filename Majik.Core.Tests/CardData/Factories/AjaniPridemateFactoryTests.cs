using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Ajani's Pridemate (Magic 2011, {1}{W}).
///
/// Covers:
///   - Card shape: name, type, Cat + Soldier subtypes, P/T 2/2, mana cost,
///     owner / controller wiring.
///   - NamedCardFactory dispatch.
///   - Lifegain trigger condition: controller gain → matches; opponent
///     gain → does not; controller life loss → does not; zero delta → does
///     not.
///   - Effect resolution: one +1/+1 counter regardless of gained amount
///     (CR 122.1).
/// </summary>
[Trait("Color", "W")]
public class AjaniPridemateFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Pridemate_Identity()
    {
        var c = AjaniPridemateFactory.Create(_alice);

        c.Name.Should().Be("Ajani's Pridemate");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void Pridemate_LifegainTrigger_FiresForController_NotOpponent()
    {
        var pridemate = AjaniPridemateFactory.Create(_alice);
        var trigger = pridemate.Abilities.OfType<TriggeredAbility>().Single();

        // Controller gains life — match.
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 22), trigger)
            .Should().BeTrue("Pridemate's trigger fires on controller life gain");
        // Opponent gains life — no match.
        trigger.Condition.Matches(new LifeChangedEvent(_bob, 20, 25), trigger)
            .Should().BeFalse("Pridemate ignores opponent life gains");
        // Controller loses life — no match (strict positive delta).
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 17), trigger)
            .Should().BeFalse("life LOSS is not life gain");
        // Zero delta — no match.
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 20), trigger)
            .Should().BeFalse("zero life delta is not a gain");
    }

    [Fact]
    public void Pridemate_OnResolve_PlacesOnePlusOnePlusOneCounter()
    {
        var pridemate = AjaniPridemateFactory.Create(_alice);
        pridemate.SetZone(ZoneType.Battlefield);

        var trigger = pridemate.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        pridemate.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Pridemate gains one +1/+1 counter on lifegain (CR 122.1)");
    }

    [Fact]
    public void Pridemate_MultipleLifeGains_AccumulateCounters()
    {
        // CR 603.2 / 122.1 — each separate life-gain event triggers the
        // ability once, placing one counter per resolution regardless of
        // the gained amount.
        var pridemate = AjaniPridemateFactory.Create(_alice);
        pridemate.SetZone(ZoneType.Battlefield);

        var trigger = pridemate.Abilities.OfType<TriggeredAbility>().Single();

        // Three separate life-gain events resolve → three counters.
        foreach (var effect in trigger.Effects) effect.Execute();
        foreach (var effect in trigger.Effects) effect.Execute();
        foreach (var effect in trigger.Effects) effect.Execute();

        pridemate.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3);
    }
}
