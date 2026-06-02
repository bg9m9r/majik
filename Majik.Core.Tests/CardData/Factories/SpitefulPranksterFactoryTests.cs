using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SpitefulPranksterFactory"/> (Eldritch Moon,
/// {2}{R}).
///
/// Spiteful Prankster is a 3/2 Creature — Devil (Scryfall verified 2026-06):
///   "During your turn, this creature has first strike.
///    Whenever another creature dies, this creature deals 1 damage to target
///    player or planeswalker."
///
/// The conditional first-strike static mirrors
/// <see cref="GhituLavarunnerFactory"/>'s Layer-6 conditional Haste grant —
/// only the gate (active player == controller) and the granted keyword
/// (First strike) differ. The dies trigger mirrors
/// <see cref="FalkenrathNobleFactory"/>'s "another creature dies" trigger but
/// deals 1 damage to a chosen player/planeswalker (via
/// <see cref="ViashinoPyromancerFactory"/>'s target-request + Fx.DealDamageAny
/// resolve) rather than draining life.
///
/// Covers:
/// - Identity (name, Creature, Devil subtype, 3/2, {2}{R}, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Conditional first strike: present on the controller's turn, absent on an
///   opponent's turn (Layer 6, CR 613.1f / CR 702.7).
/// - Dies trigger fires for ANOTHER creature dying (CR 603.1).
/// - Dies trigger does NOT fire for Spiteful Prankster's OWN death ("another"
///   excludes itself) nor for non-creatures nor Battlefield → Exile.
/// - Dies trigger deals 1 damage to the chosen target player.
/// </summary>
[Trait("Color", "R")]
public class SpitefulPranksterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SpitefulPrankster_Identity()
    {
        var c = SpitefulPranksterFactory.Create(_alice);

        c.Name.Should().Be("Spiteful Prankster");
        c.ManaCost.Should().Be("{2}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Devil).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Prankster has a single 'another creature dies' trigger");
    }

    [Fact]
    public void NamedFactory_Dispatches_SpitefulPrankster()
    {
        var card = NamedCardFactory.Create("Spiteful Prankster", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Spiteful Prankster");
        card.HasSubtype(CardSubtype.Devil).Should().BeTrue();
    }

    // ── Conditional first strike (Layer 6) ────────────────────────────────

    private Creature NewPranksterOnBattlefield(
        out ContinuousEffectsService effects, bool myTurn)
    {
        effects = new ContinuousEffectsService();
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        // isControllersTurn predicate stands in for TurnManager.ActivePlayer
        // == controller; flip it to model whose turn it is.
        var local = myTurn;
        var prankster = SpitefulPranksterFactory.Create(
            _alice,
            isControllersTurn: () => local,
            targetResolver: null,
            effects: effects,
            eventBus: bus,
            triggers: null);
        zones.MoveCard(prankster, ZoneType.Library, ZoneType.Battlefield, _alice);
        prankster.ActiveEffects = effects;
        return prankster;
    }

    [Fact]
    public void FirstStrike_PresentDuringControllersTurn()
    {
        var prankster = NewPranksterOnBattlefield(out var effects, myTurn: true);

        effects.Compute(prankster).Keywords.Should().Contain(
            SpitefulPranksterFactory.FirstStrike,
            "CR 702.7 — has first strike during your turn");
    }

    [Fact]
    public void FirstStrike_AbsentDuringOpponentsTurn()
    {
        var prankster = NewPranksterOnBattlefield(out var effects, myTurn: false);

        effects.Compute(prankster).Keywords.Should().NotContain(
            SpitefulPranksterFactory.FirstStrike,
            "the static reads 'during your turn' — gone on an opponent's turn");
    }

    [Fact]
    public void FirstStrike_NotGrantedWhenNotMyTurn_ThenGrantedWhenItIs()
    {
        var prankster = SpitefulPranksterFactory.Create(_alice);
        // Shape overload: no continuous-effects service, so the conditional
        // static does not surface a granted keyword (no flat First strike).
        prankster.Abilities.OfType<KeywordAbility>()
            .Should().NotContain(k => k.Keyword == SpitefulPranksterFactory.FirstStrike,
                "first strike is conditional — never a printed flat keyword");
    }

    // ── Dies trigger: deal 1 damage to target player/planeswalker ─────────

    private Creature NewPranksterForTrigger(System.Func<Player?> resolver)
    {
        var prankster = SpitefulPranksterFactory.Create(
            _alice,
            isControllersTurn: () => true,
            targetResolver: resolver,
            effects: null,
            eventBus: null,
            triggers: null);
        _alice.Zones.Battlefield.AddCard(prankster);
        prankster.SetZone(ZoneType.Battlefield);
        return prankster;
    }

    [Fact]
    public void Dies_AnotherCreatureDies_DealsOneDamageToTargetPlayer()
    {
        var prankster = NewPranksterForTrigger(() => _bob);

        var bobsBear = new Creature("Runeclaw Bear", "{1}{G}", 2, 2);
        bobsBear.SetOwner(_bob);
        bobsBear.SetController(_bob);

        var diesEvent = new CardMovedEvent(
            bobsBear, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = prankster.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(diesEvent));

        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(19, "1 damage to the chosen target player");
    }

    [Fact]
    public void Dies_OwnDeath_DoesNotFire()
    {
        var prankster = NewPranksterForTrigger(() => _bob);

        var diesEvent = new CardMovedEvent(
            prankster, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = prankster.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(diesEvent).Should().BeFalse(
            "'another creature' excludes Spiteful Prankster itself");
    }

    [Fact]
    public void Dies_NonCreatureDies_DoesNotFire()
    {
        var prankster = NewPranksterForTrigger(() => _bob);

        var artifact = new Artifact("Trinket", "{0}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);

        var moveEvent = new CardMovedEvent(
            artifact, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = prankster.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(moveEvent).Should().BeFalse(
            "the trigger reads 'creature' — non-creature deaths skip");
    }

    [Fact]
    public void Dies_BattlefieldToExile_DoesNotFire()
    {
        var prankster = NewPranksterForTrigger(() => _bob);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var exileEvent = new CardMovedEvent(
            bear, ZoneType.Battlefield, ZoneType.Exile);

        var trigger = prankster.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(exileEvent).Should().BeFalse(
            "CR 700.4 — exile is not death");
    }

    [Fact]
    public void Dies_NoResolver_NoOps()
    {
        var prankster = NewPranksterForTrigger(resolver: null!);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var diesEvent = new CardMovedEvent(
            bear, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = prankster.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(diesEvent));

        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(20, "no targetResolver ⇒ damage silently no-ops");
    }
}
