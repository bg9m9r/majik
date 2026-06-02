using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Essence Warden (Planar Chaos, {G}).
///
/// Functional reprint of Soul Warden (green-costed). Mirrors
/// <see cref="SoulWardenFactoryTests"/>.
///
/// Covers:
///   - Card shape: name, type, Elf + Shaman subtypes, P/T 1/1, mana cost,
///     owner / controller wiring.
///   - Trigger condition: ANY other creature entering battlefield (any
///     controller) → matches; Essence Warden itself entering → does not match;
///     non-creature ETB → does not match.
///   - Trigger effect resolution: controller gains 1 life.
/// </summary>
[Trait("Color", "G")]
public class EssenceWardenFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void EssenceWarden_Identity()
    {
        var c = EssenceWardenFactory.Create(_alice);

        c.Name.Should().Be("Essence Warden");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EssenceWarden_AnotherCreatureEnters_TriggerMatches()
    {
        var warden = EssenceWardenFactory.Create(_alice);
        warden.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var trigger = warden.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeTrue(
            "Essence Warden fires when another creature enters");
    }

    [Fact]
    public void EssenceWarden_OpponentCreatureEnters_StillTriggers()
    {
        // Printed Essence Warden cares about ANY creature entering, not just yours.
        var warden = EssenceWardenFactory.Create(_alice);
        warden.SetZone(ZoneType.Battlefield);

        var oppCreature = new Creature("Bob's Bear", "{1}{G}", 2, 2);
        oppCreature.SetOwner(_bob);
        oppCreature.SetController(_bob);

        var trigger = warden.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(oppCreature, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeTrue(
            "Essence Warden's printed trigger has no controller restriction");
    }

    [Fact]
    public void EssenceWarden_NonCreatureEnters_DoesNotMatch()
    {
        var warden = EssenceWardenFactory.Create(_alice);
        warden.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Mox Pearl", "{0}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);

        var trigger = warden.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(artifact, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeFalse(
            "Essence Warden does not trigger on non-creature ETB");
    }

    [Fact]
    public void EssenceWarden_SelfEnters_DoesNotTrigger()
    {
        var warden = EssenceWardenFactory.Create(_alice);
        warden.SetZone(ZoneType.Hand);

        var trigger = warden.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(warden, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeFalse(
            "Essence Warden's trigger is 'another creature' — itself is excluded");
    }

    [Fact]
    public void EssenceWarden_OnResolve_ControllerGainsOneLife()
    {
        var warden = EssenceWardenFactory.Create(_alice);
        warden.SetZone(ZoneType.Battlefield);

        var trigger = warden.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(21, "Essence Warden gains its controller 1 life");
    }
}
