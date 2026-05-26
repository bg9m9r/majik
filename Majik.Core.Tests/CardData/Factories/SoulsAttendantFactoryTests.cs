using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Soul's Attendant (Magic 2011, {W}).
///
/// Functional reprint of Soul Warden. The test surface mirrors
/// <see cref="SoulWardenFactoryTests"/> so a regression on either card
/// surfaces independently.
/// </summary>
public class SoulsAttendantFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SoulsAttendant_Identity()
    {
        var c = SoulsAttendantFactory.Create(_alice);

        c.Name.Should().Be("Soul's Attendant");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SoulsAttendant_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Soul's Attendant", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Soul's Attendant");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void SoulsAttendant_AnotherCreatureEnters_TriggerMatches()
    {
        var attendant = SoulsAttendantFactory.Create(_alice);
        attendant.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var trigger = attendant.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeTrue();
    }

    [Fact]
    public void SoulsAttendant_OpponentCreatureEnters_StillTriggers()
    {
        var attendant = SoulsAttendantFactory.Create(_alice);
        attendant.SetZone(ZoneType.Battlefield);

        var oppCreature = new Creature("Bob's Bear", "{1}{G}", 2, 2);
        oppCreature.SetOwner(_bob);
        oppCreature.SetController(_bob);

        var trigger = attendant.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(oppCreature, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeTrue(
            "Soul's Attendant fires for any creature entering, regardless of controller");
    }

    [Fact]
    public void SoulsAttendant_SelfEnters_DoesNotTrigger()
    {
        var attendant = SoulsAttendantFactory.Create(_alice);
        attendant.SetZone(ZoneType.Hand);

        var trigger = attendant.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(attendant, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeFalse(
            "'another creature' excludes self");
    }

    [Fact]
    public void SoulsAttendant_OnResolve_ControllerGainsOneLife()
    {
        var attendant = SoulsAttendantFactory.Create(_alice);
        attendant.SetZone(ZoneType.Battlefield);

        var trigger = attendant.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(21);
    }
}
