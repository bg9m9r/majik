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
/// Tests for Soul Warden (Exodus, {W}).
///
/// Covers:
///   - Card shape: name, type, Human + Cleric subtypes, P/T 1/1, mana cost,
///     owner / controller wiring.
///   - NamedCardFactory dispatch.
///   - Trigger condition: ANY other creature entering battlefield (any
///     controller) → matches; Soul Warden itself entering → does not match.
///   - Trigger effect resolution: controller gains 1 life.
/// </summary>
public class SoulWardenFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SoulWarden_Identity()
    {
        var c = SoulWardenFactory.Create(_alice);

        c.Name.Should().Be("Soul Warden");
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
    public void SoulWarden_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Soul Warden", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Soul Warden");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one ETB-other-creature trigger is attached");
    }

    [Fact]
    public void SoulWarden_AnotherCreatureEnters_TriggerMatches()
    {
        var warden = SoulWardenFactory.Create(_alice);
        warden.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var trigger = warden.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeTrue(
            "Soul Warden fires when another creature enters");
    }

    [Fact]
    public void SoulWarden_OpponentCreatureEnters_StillTriggers()
    {
        // Printed Soul Warden cares about ANY creature entering, not just yours.
        var warden = SoulWardenFactory.Create(_alice);
        warden.SetZone(ZoneType.Battlefield);

        var oppCreature = new Creature("Bob's Bear", "{1}{G}", 2, 2);
        oppCreature.SetOwner(_bob);
        oppCreature.SetController(_bob);

        var trigger = warden.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(oppCreature, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeTrue(
            "Soul Warden's printed trigger has no controller restriction");
    }

    [Fact]
    public void SoulWarden_NonCreatureEnters_DoesNotMatch()
    {
        var warden = SoulWardenFactory.Create(_alice);
        warden.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Mox Pearl", "{0}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);

        var trigger = warden.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(artifact, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeFalse(
            "Soul Warden does not trigger on non-creature ETB");
    }

    [Fact]
    public void SoulWarden_SelfEnters_DoesNotTrigger()
    {
        var warden = SoulWardenFactory.Create(_alice);
        warden.SetZone(ZoneType.Hand);

        var trigger = warden.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(warden, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeFalse(
            "Soul Warden's trigger is 'another creature' — itself is excluded");
    }

    [Fact]
    public void SoulWarden_OnResolve_ControllerGainsOneLife()
    {
        var warden = SoulWardenFactory.Create(_alice);
        warden.SetZone(ZoneType.Battlefield);

        var trigger = warden.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(21, "Soul Warden gains its controller 1 life");
    }
}
