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
/// Unit tests for <see cref="BloodArtistFactory"/> (Avacyn Restored,
/// {1}{B}).
///
/// Covers:
/// - Identity (Creature, Vampire subtype, 0/1, {1}{B}, owner/controller).
/// - NamedCardFactory dispatch.
/// - Death trigger fires for any creature dying — controlled or
///   opponent-controlled (CR 603.1 + CR 700.4).
/// - Death trigger does NOT fire for non-creatures dying.
/// - Death trigger does NOT fire on Battlefield → Exile (not "dies").
/// - Drain side: target player loses 1 + controller gains 1 (with
///   resolver).
/// - Drain side: lifegain still fires without resolver (Blood Artist's
///   lifegain is unconditional on the target).
/// </summary>
public class BloodArtistTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BloodArtist_Identity()
    {
        var c = BloodArtistFactory.Create(_alice);

        c.Name.Should().Be("Blood Artist");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Subtypes.Should().Contain(CardSubtype.Vampire);
        c.BasePower.Should().Be(0);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Blood Artist has a single 'any creature dies' trigger");
    }

    [Fact]
    public void BloodArtist_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Blood Artist", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Blood Artist");
    }

    [Fact]
    public void BloodArtist_OwnCreatureDies_DrainsTargetAndGainsLife()
    {
        var artist = BloodArtistFactory.Create(
            _alice,
            targetResolver: () => _bob,
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(artist);
        artist.SetZone(ZoneType.Battlefield);

        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceBear.SetOwner(_alice);
        aliceBear.SetController(_alice);

        var diesEvent = new CardMovedEvent(aliceBear, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = artist.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(diesEvent));

        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(19, "target player loses 1 life");
        _alice.LifeTotal.Should().Be(21, "controller gains 1 life");
    }

    [Fact]
    public void BloodArtist_OpponentCreatureDies_StillFires()
    {
        // Blood Artist's printed text says "another creature dies" with
        // no "you control" qualifier — opponent-controlled deaths must
        // also trigger. This is the key shape difference vs Zulaport
        // Cutthroat / Cruel Celebrant.
        var artist = BloodArtistFactory.Create(
            _alice,
            targetResolver: () => _bob,
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(artist);
        artist.SetZone(ZoneType.Battlefield);

        var bobsBear = new Creature("Runeclaw Bear", "{1}{G}", 2, 2);
        bobsBear.SetOwner(_bob);
        bobsBear.SetController(_bob);

        var diesEvent = new CardMovedEvent(bobsBear, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = artist.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(diesEvent).Should().BeTrue(
            "Blood Artist triggers on any creature dying — controller-agnostic");

        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(19);
        _alice.LifeTotal.Should().Be(21);
    }

    [Fact]
    public void BloodArtist_NonCreatureDies_DoesNotFire()
    {
        var artist = BloodArtistFactory.Create(
            _alice,
            targetResolver: () => _bob,
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(artist);
        artist.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Trinket", "{0}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);

        var moveEvent = new CardMovedEvent(artifact, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = artist.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(moveEvent).Should().BeFalse(
            "Blood Artist's trigger reads 'creature' — an artifact dying is irrelevant");
    }

    [Fact]
    public void BloodArtist_NonGraveyardDestination_DoesNotFire()
    {
        var artist = BloodArtistFactory.Create(
            _alice,
            targetResolver: () => _bob,
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(artist);
        artist.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        // Battlefield → Exile is NOT death (CR 700.4 — dying requires
        // graveyard as destination).
        var exileEvent = new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Exile);

        var trigger = artist.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(exileEvent).Should().BeFalse(
            "CR 700.4 — 'dies' requires Battlefield → Graveyard; exile bypasses the trigger");
    }

    [Fact]
    public void BloodArtist_NoResolver_GainsLifeOnly()
    {
        // Single-arg dispatcher path — no targetResolver wired. The
        // drain side silently no-ops but the controller's lifegain still
        // fires (Blood Artist's lifegain is unconditional). Same shape
        // as Cruel Celebrant's resolver convention.
        var artist = BloodArtistFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(artist);
        artist.SetZone(ZoneType.Battlefield);

        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceBear.SetOwner(_alice);
        aliceBear.SetController(_alice);

        var diesEvent = new CardMovedEvent(aliceBear, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = artist.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(diesEvent));

        foreach (var e in trigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(21, "lifegain side fires unconditionally on resolution");
        _bob.LifeTotal.Should().Be(20, "no targetResolver ⇒ drain silently no-ops");
    }
}
