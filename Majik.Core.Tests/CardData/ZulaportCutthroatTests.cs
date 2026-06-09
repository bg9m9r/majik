using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ZulaportCutthroatFactory"/> (Battle for
/// Zendikar, {1}{B}).
///
/// Covers:
/// - Identity (Creature, Human Rogue subtypes, 1/1, {1}{B},
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Death trigger fires Battlefield → Graveyard for a creature you
///   control (CR 603.1 + CR 700.4).
/// - Death trigger does NOT fire for an opponent's creature.
/// - Death trigger does NOT fire on non-creature permanents dying.
/// - Drain side reads each opponent from the LIVE resolution context
///   (resolver-null bug class): each opponent loses 1 + controller gains 1,
///   verified on the PROD build (NamedCardFactory.Create) too.
/// </summary>
public class ZulaportCutthroatTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ZulaportCutthroat_Identity()
    {
        var c = ZulaportCutthroatFactory.Create(_alice);

        c.Name.Should().Be("Zulaport Cutthroat");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Subtypes.Should().Contain(CardSubtype.Human);
        c.Subtypes.Should().Contain(CardSubtype.Rogue);
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Cutthroat has a single 'creature you control dies' trigger");
    }

    [Fact]
    public void ZulaportCutthroat_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Zulaport Cutthroat", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Zulaport Cutthroat");
    }

    [Fact]
    public void ZulaportCutthroat_OwnCreatureDies_DrainsEachOpponentAndGains()
    {
        var cutthroat = ZulaportCutthroatFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(cutthroat);
        cutthroat.SetZone(ZoneType.Battlefield);

        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceBear.SetOwner(_alice);
        aliceBear.SetController(_alice);

        var diesEvent = new CardMovedEvent(aliceBear, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = cutthroat.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(diesEvent));

        ResolveWithGame(trigger, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(19, "each opponent loses 1 life");
        _alice.LifeTotal.Should().Be(21, "controller gains 1 life");
    }

    /// <summary>
    /// PROD-PATH guard (the resolver-null bug class). The production
    /// <c>GameFacade</c> routed build dispatches the single-arg
    /// <see cref="NamedCardFactory.Create(string, Player)"/>; the drain must
    /// read each opponent off the live <see cref="GameContext"/> the trigger
    /// threads through <see cref="TriggeredAbility.ResolveAsync"/> — not a
    /// captured (null) resolver.
    /// </summary>
    [Fact]
    public void ZulaportCutthroat_DrainsEachOpponent_OnProdBuild()
    {
        var built = NamedCardFactory.Create("Zulaport Cutthroat", _alice);
        built.Should().BeOfType<Creature>();
        var cutthroat = (Creature)built;

        _alice.Zones.Battlefield.AddCard(cutthroat);
        cutthroat.SetZone(ZoneType.Battlefield);

        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceBear.SetOwner(_alice);
        aliceBear.SetController(_alice);

        var diesEvent = new CardMovedEvent(aliceBear, ZoneType.Battlefield, ZoneType.Graveyard);
        var trigger = cutthroat.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(diesEvent));

        ResolveWithGame(trigger, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(19,
            "the prod-built dies trigger reads opponents from the live context (not inert)");
        _alice.LifeTotal.Should().Be(21, "controller gains 1 life");
    }

    [Fact]
    public void ZulaportCutthroat_OpponentCreatureDies_DoesNotFire()
    {
        var cutthroat = ZulaportCutthroatFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(cutthroat);
        cutthroat.SetZone(ZoneType.Battlefield);

        var bobsBear = new Creature("Runeclaw Bear", "{1}{G}", 2, 2);
        bobsBear.SetOwner(_bob);
        bobsBear.SetController(_bob);

        var diesEvent = new CardMovedEvent(bobsBear, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = cutthroat.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(diesEvent).Should().BeFalse(
            "CR 603.1 — 'creature you control dies' filters opponent-controlled deaths");
    }

    [Fact]
    public void ZulaportCutthroat_NonCreatureDies_DoesNotFire()
    {
        var cutthroat = ZulaportCutthroatFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(cutthroat);
        cutthroat.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Trinket", "{0}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);

        var moveEvent = new CardMovedEvent(artifact, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = cutthroat.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(moveEvent).Should().BeFalse(
            "Cutthroat's trigger reads 'creature' — non-creature deaths skip");
    }

    [Fact]
    public void ZulaportCutthroat_NonGraveyardDestination_DoesNotFire()
    {
        var cutthroat = ZulaportCutthroatFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(cutthroat);
        cutthroat.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var exileEvent = new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Exile);

        var trigger = cutthroat.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(exileEvent).Should().BeFalse(
            "CR 700.4 — exile is not death");
    }

    [Fact]
    public void ZulaportCutthroat_OwnCreatureDies_WithoutLiveGame_GainsLifeOnly()
    {
        var cutthroat = ZulaportCutthroatFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(cutthroat);
        cutthroat.SetZone(ZoneType.Battlefield);

        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceBear.SetOwner(_alice);
        aliceBear.SetController(_alice);

        var diesEvent = new CardMovedEvent(aliceBear, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = cutthroat.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(diesEvent));

        // No live GameContext → no opponents to read → drain is a safe no-op,
        // but the lifegain side still fires unconditionally.
        foreach (var e in trigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(21, "lifegain side fires unconditionally");
        _bob.LifeTotal.Should().Be(20, "no live game ⇒ opponent-drain silently no-ops");
    }

    private static void ResolveWithGame(
        TriggeredAbility trigger, Player controller, params Player[] players)
    {
        var game = new GameContext(
            self: controller,
            allPlayers: players,
            activePlayer: controller,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new EventBus()));

        trigger.ResolveAsync(agent: null, game: game).AsTask().GetAwaiter().GetResult();
    }
}
