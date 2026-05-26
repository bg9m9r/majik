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
/// Unit tests for <see cref="CruelCelebrantFactory"/> (War of the Spark,
/// {W}{B}).
///
/// Covers:
/// - Identity (Creature, Vampire subtype, 1/2, {W}{B}, owner/controller).
/// - NamedCardFactory dispatch.
/// - Death trigger fires Battlefield → Graveyard for a creature you
///   control (CR 603.1 + CR 700.4).
/// - Death trigger fires for a planeswalker you control (per the printed
///   "creature or planeswalker" union).
/// - Death trigger does NOT fire for an opponent's creature.
/// - Death trigger does NOT fire on non-creature / non-planeswalker
///   permanents (artifact, enchantment, land).
/// - Death trigger does NOT fire on other zone transitions.
/// - Drain side: each opponent loses 1 + controller gains 1 (with
///   resolver).
/// - Drain side: lifegain still fires without resolver (Cruel Celebrant
///   ALWAYS gives the controller 1 life on death; the opponent-loss is
///   conditional on the resolver).
/// </summary>
public class CruelCelebrantTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void CruelCelebrant_Identity()
    {
        var c = CruelCelebrantFactory.Create(_alice);

        c.Name.Should().Be("Cruel Celebrant");
        c.ManaCost.Should().Be("{W}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Subtypes.Should().Contain(CardSubtype.Vampire);
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Celebrant has a single union 'creature or planeswalker you control dies' trigger");
    }

    [Fact]
    public void CruelCelebrant_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Cruel Celebrant", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Cruel Celebrant");
    }

    [Fact]
    public void CruelCelebrant_OwnCreatureDies_DrainsAndGains()
    {
        var celebrant = CruelCelebrantFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(celebrant);
        celebrant.SetZone(ZoneType.Battlefield);

        // Alice's own creature dies (CR 700.4 — Battlefield → Graveyard).
        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceBear.SetOwner(_alice);
        aliceBear.SetController(_alice);

        var diesEvent = new CardMovedEvent(aliceBear, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = celebrant.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(diesEvent));

        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(19, "each opponent loses 1 life");
        _alice.LifeTotal.Should().Be(21, "controller gains 1 life");
    }

    [Fact]
    public void CruelCelebrant_OwnPlaneswalkerDies_DrainsAndGains()
    {
        // CR 700.4 — "die" technically reads "creature dies / planeswalker
        // is put into a graveyard" but the printed text bundles them
        // together for trigger purposes. The condition fires on a
        // controlled planeswalker moving Battlefield → Graveyard.
        var celebrant = CruelCelebrantFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(celebrant);
        celebrant.SetZone(ZoneType.Battlefield);

        var pw = new Planeswalker("Liliana of the Veil", "{1}{B}{B}",
            startingLoyalty: 3, subtypes: new[] { CardSubtype.Liliana });
        pw.SetOwner(_alice);
        pw.SetController(_alice);

        var diesEvent = new CardMovedEvent(pw, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = celebrant.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(diesEvent).Should().BeTrue(
            "the printed 'creature or planeswalker you control dies' union covers planeswalkers");

        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(19);
        _alice.LifeTotal.Should().Be(21);
    }

    [Fact]
    public void CruelCelebrant_OpponentCreatureDies_DoesNotFire()
    {
        var celebrant = CruelCelebrantFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(celebrant);
        celebrant.SetZone(ZoneType.Battlefield);

        // Bob's creature dies — NOT controlled by Alice.
        var bobsBear = new Creature("Runeclaw Bear", "{1}{G}", 2, 2);
        bobsBear.SetOwner(_bob);
        bobsBear.SetController(_bob);

        var diesEvent = new CardMovedEvent(bobsBear, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = celebrant.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(diesEvent).Should().BeFalse(
            "CR 603.1 — 'creature you control dies' filters out opponent-controlled deaths");
    }

    [Fact]
    public void CruelCelebrant_NonCreatureNonPlaneswalkerDies_DoesNotFire()
    {
        var celebrant = CruelCelebrantFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(celebrant);
        celebrant.SetZone(ZoneType.Battlefield);

        // An artifact moving Battlefield → Graveyard — not creature, not
        // planeswalker, so the trigger predicate must reject it.
        var artifact = new Artifact("Trinket", "{0}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);

        var moveEvent = new CardMovedEvent(artifact, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = celebrant.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(moveEvent).Should().BeFalse(
            "Celebrant's trigger reads 'creature or planeswalker' — an artifact dying is irrelevant");
    }

    [Fact]
    public void CruelCelebrant_NonGraveyardDestination_DoesNotFire()
    {
        var celebrant = CruelCelebrantFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(celebrant);
        celebrant.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        // Battlefield → Exile is NOT death (CR 700.4 — dying requires
        // graveyard as destination).
        var exileEvent = new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Exile);

        var trigger = celebrant.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(exileEvent).Should().BeFalse(
            "CR 700.4 — 'dies' requires Battlefield → Graveyard; exile bypasses the trigger");
    }

    [Fact]
    public void CruelCelebrant_OwnCreatureDies_WithoutResolver_GainsLifeOnly()
    {
        // Single-arg dispatcher path — no opponentResolver wired. The
        // opponent-drain side silently no-ops but the controller's
        // lifegain still fires (Celebrant's lifegain side is
        // unconditional on the resolver). Mirrors Sheoldred / Meathook's
        // resolver convention but with the lifegain split out.
        var celebrant = CruelCelebrantFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(celebrant);
        celebrant.SetZone(ZoneType.Battlefield);

        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceBear.SetOwner(_alice);
        aliceBear.SetController(_alice);

        var diesEvent = new CardMovedEvent(aliceBear, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = celebrant.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(diesEvent));

        foreach (var e in trigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(21, "lifegain side fires unconditionally on resolution");
        _bob.LifeTotal.Should().Be(20, "no opponentResolver ⇒ opponent-drain silently no-ops");
    }
}
