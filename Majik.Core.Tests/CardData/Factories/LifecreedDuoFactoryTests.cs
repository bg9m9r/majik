using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Lifecreed Duo (Bloomburrow, {1}{W}).
///
/// Creature — Bat Bird 1/2 with Flying and
///   "Whenever another creature you control enters, you gain 1 life."
///
/// The unique surface vs the unscoped Soul Warden / Soul's Attendant lifegain
/// shape is: (a) the trigger is gated to creatures the CONTROLLER controls
/// (<c>youControlOnly</c>), so an opponent's creature does NOT fire it, and
/// (b) it carries Flying (CR 702.9). Dispatch + well-formedness are asserted
/// for every implemented card by CardFactoryContractTests.
/// </summary>
[Trait("Color", "W")]
public class LifecreedDuoFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void LifecreedDuo_Identity()
    {
        var c = LifecreedDuoFactory.Create(_alice);

        c.Name.Should().Be("Lifecreed Duo");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Bat).Should().BeTrue();
        c.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // Flying — CR 702.9
    [Fact]
    public void LifecreedDuo_HasFlyingKeyword()
    {
        var c = LifecreedDuoFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "CR 702.9 — Lifecreed Duo has Flying");
        CombatAbilities.HasFlying(c).Should().BeTrue(
            "CR 702.9 — the combat validator reads the Flying keyword for evasion");
    }

    [Fact]
    public void LifecreedDuo_AnotherCreatureYouControlEnters_TriggerMatches()
    {
        var duo = LifecreedDuoFactory.Create(_alice);
        duo.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var trigger = duo.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeTrue(
            "another creature the controller controls entering fires the trigger");
    }

    [Fact]
    public void LifecreedDuo_OpponentCreatureEnters_DoesNotTrigger()
    {
        var duo = LifecreedDuoFactory.Create(_alice);
        duo.SetZone(ZoneType.Battlefield);

        var oppCreature = new Creature("Bob's Bear", "{1}{G}", 2, 2);
        oppCreature.SetOwner(_bob);
        oppCreature.SetController(_bob);

        var trigger = duo.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(oppCreature, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeFalse(
            "'another creature you control' excludes creatures the opponent controls");
    }

    [Fact]
    public void LifecreedDuo_SelfEnters_DoesNotTrigger()
    {
        var duo = LifecreedDuoFactory.Create(_alice);
        duo.SetZone(ZoneType.Hand);

        var trigger = duo.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(duo, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeFalse(
            "'another creature' excludes Lifecreed Duo itself");
    }

    [Fact]
    public void LifecreedDuo_OnResolve_ControllerGainsOneLife()
    {
        var duo = LifecreedDuoFactory.Create(_alice);
        duo.SetZone(ZoneType.Battlefield);

        var trigger = duo.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(21);
    }
}
