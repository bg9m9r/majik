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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="FalkenrathNobleFactory"/> (Innistrad,
/// {3}{B}).
///
/// Covers:
/// - Identity (Creature, Vampire subtype, 2/2, {3}{B}, owner/controller,
///   Flying keyword).
/// - NamedCardFactory dispatch.
/// - Death trigger fires for any creature dying — controlled or
///   opponent-controlled (CR 603.1 + CR 700.4).
/// - Death trigger does NOT fire for non-creatures.
/// - Death trigger does NOT fire on Battlefield → Exile.
/// - Drain side: target player loses 1 + controller gains 1.
/// - Drain side: lifegain still fires without resolver.
/// </summary>
public class FalkenrathNobleTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void FalkenrathNoble_Identity()
    {
        var c = FalkenrathNobleFactory.Create(_alice);

        c.Name.Should().Be("Falkenrath Noble");
        c.ManaCost.Should().Be("{3}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Subtypes.Should().Contain(CardSubtype.Vampire);
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().Contain(
            k => k.Keyword == "Flying",
            "CR 702.9 — Falkenrath Noble has Flying");

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Noble has a single 'any creature dies' trigger");
    }

    [Fact]
    public void FalkenrathNoble_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Falkenrath Noble", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Falkenrath Noble");
    }

    [Fact]
    public void FalkenrathNoble_OwnCreatureDies_DrainsAndGains()
    {
        var noble = FalkenrathNobleFactory.Create(
            _alice,
            targetResolver: () => _bob,
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(noble);
        noble.SetZone(ZoneType.Battlefield);

        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceBear.SetOwner(_alice);
        aliceBear.SetController(_alice);

        var diesEvent = new CardMovedEvent(aliceBear, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = noble.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(diesEvent));

        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(19, "target player loses 1 life");
        _alice.LifeTotal.Should().Be(21, "controller gains 1 life");
    }

    [Fact]
    public void FalkenrathNoble_OpponentCreatureDies_StillFires()
    {
        // Same shape as Blood Artist — no "you control" qualifier.
        var noble = FalkenrathNobleFactory.Create(
            _alice,
            targetResolver: () => _bob,
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(noble);
        noble.SetZone(ZoneType.Battlefield);

        var bobsBear = new Creature("Runeclaw Bear", "{1}{G}", 2, 2);
        bobsBear.SetOwner(_bob);
        bobsBear.SetController(_bob);

        var diesEvent = new CardMovedEvent(bobsBear, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = noble.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(diesEvent).Should().BeTrue(
            "Noble triggers on any creature dying — controller-agnostic");

        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(19);
        _alice.LifeTotal.Should().Be(21);
    }

    [Fact]
    public void FalkenrathNoble_NonCreatureDies_DoesNotFire()
    {
        var noble = FalkenrathNobleFactory.Create(
            _alice,
            targetResolver: () => _bob,
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(noble);
        noble.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Trinket", "{0}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);

        var moveEvent = new CardMovedEvent(artifact, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = noble.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(moveEvent).Should().BeFalse(
            "Noble's trigger reads 'creature' — non-creature deaths skip");
    }

    [Fact]
    public void FalkenrathNoble_NonGraveyardDestination_DoesNotFire()
    {
        var noble = FalkenrathNobleFactory.Create(
            _alice,
            targetResolver: () => _bob,
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(noble);
        noble.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var exileEvent = new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Exile);

        var trigger = noble.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(exileEvent).Should().BeFalse(
            "CR 700.4 — exile is not death");
    }

    [Fact]
    public void FalkenrathNoble_NoResolver_GainsLifeOnly()
    {
        var noble = FalkenrathNobleFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(noble);
        noble.SetZone(ZoneType.Battlefield);

        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceBear.SetOwner(_alice);
        aliceBear.SetController(_alice);

        var diesEvent = new CardMovedEvent(aliceBear, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = noble.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(diesEvent));

        foreach (var e in trigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(21, "lifegain side fires unconditionally");
        _bob.LifeTotal.Should().Be(20, "no targetResolver ⇒ drain silently no-ops");
    }
}
