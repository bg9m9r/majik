using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MassacreWurmFactory"/> (New Phyrexia,
/// {3}{B}{B}{B}).
///
/// Massacre Wurm — Creature — Phyrexian Wurm 6/5. Oracle text (Scryfall,
/// verified):
///   "When this creature enters, creatures your opponents control get -2/-2
///    until end of turn.
///    Whenever a creature an opponent controls dies, that player loses 2
///    life."
///
/// Covers ONLY the card's unique behaviour:
/// - Identity (name, type Creature, Phyrexian + Wurm subtypes, mana cost,
///   6/5).
/// - ETB sweep: every creature an OPPONENT controls gets -2/-2; the
///   controller's own creatures are untouched (CR 102.1).
/// - Opponent-creature dies → that player (the dying creature's controller)
///   loses 2 life (CR 603.1 + CR 700.4 + CR 603.3 "that player").
/// - Own-creature dies does NOT fire (only an opponent's death matters).
/// - Non-creature graveyard move does not fire the dies-trigger (CR 700.4).
///
/// (CardFactoryContractTests already asserts NamedCardFactory dispatch +
/// well-formedness for every implemented card — no dispatch test here.)
/// </summary>
[Trait("Color", "B")]
public class MassacreWurmTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void MassacreWurm_Identity()
    {
        var c = MassacreWurmFactory.Create(_alice);

        c.Name.Should().Be("Massacre Wurm");
        c.ManaCost.Should().Be("{3}{B}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wurm).Should().BeTrue();
        c.BasePower.Should().Be(6, "Massacre Wurm is a 6/5");
        c.BaseToughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // Two triggered abilities — ETB -2/-2 sweep + opponent-creature-dies
        // drain.
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB -2/-2 sweep + opponent-creature-dies 2-life drain");
    }

    // -----------------------------------------------------------------------
    // ETB sweep (CR 603.6a) — opponents' creatures only
    // -----------------------------------------------------------------------

    [Fact]
    public void MassacreWurm_Etb_OpponentsCreaturesGetMinusTwoMinusTwo_OwnUntouched()
    {
        // Alice's own creature — must be UNTOUCHED ("your opponents control").
        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        aliceBear.SetOwner(_alice);
        aliceBear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(aliceBear);
        aliceBear.SetZone(ZoneType.Battlefield);

        // Bob's creatures — both should get -2/-2.
        var bobBear = new Creature("Runeclaw Bear", "{1}{G}", 2, 2)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        bobBear.SetOwner(_bob);
        bobBear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobBear);
        bobBear.SetZone(ZoneType.Battlefield);

        var bobGiant = new Creature("Hill Giant", "{3}{R}", 3, 3)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        bobGiant.SetOwner(_bob);
        bobGiant.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobGiant);
        bobGiant.SetZone(ZoneType.Battlefield);

        var wurm = MassacreWurmFactory.Create(_alice, eventBus: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(wurm);
        wurm.SetZone(ZoneType.Battlefield);

        var etbTrigger = wurm.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(
                new CardMovedEvent(wurm, ZoneType.Stack, ZoneType.Battlefield)));

        // Resolve through a live game so the sweep reads every opponent's
        // battlefield off the context (resolver-null bug-class fix).
        Majik.Core.Tests.Helpers.ContextResolve.Resolve(etbTrigger, _alice, _alice, _bob);

        // Bob's creatures take -2/-2 — CR 613 Layer 7c.
        bobBear.Power.Should().Be(0, "Runeclaw Bear 2/2 gets -2/-2 ⇒ 0/0");
        bobBear.Toughness.Should().Be(0);
        bobGiant.Power.Should().Be(1, "Hill Giant 3/3 gets -2/-2 ⇒ 1/1");
        bobGiant.Toughness.Should().Be(1);

        // Alice's own creature is untouched — "creatures your OPPONENTS control".
        aliceBear.Power.Should().Be(2, "the controller's own creatures are not swept");
        aliceBear.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Dies trigger (CR 603.1 / CR 700.4 / CR 603.3 "that player")
    // -----------------------------------------------------------------------

    [Fact]
    public void MassacreWurm_OpponentCreatureDies_ThatPlayerLosesTwoLife()
    {
        var wurm = MassacreWurmFactory.Create(_alice, eventBus: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(wurm);
        wurm.SetZone(ZoneType.Battlefield);

        // Bob's creature dies — its controller (LKI) is Bob, an opponent.
        var bobsBear = new Creature("Runeclaw Bear", "{1}{G}", 2, 2);
        bobsBear.SetOwner(_bob);
        bobsBear.SetController(_bob);

        var diesEvent = new CardMovedEvent(
            bobsBear, ZoneType.Battlefield, ZoneType.Graveyard, lkiController: _bob);

        var diesTrigger = wurm.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(diesEvent));

        // Resolve through a live game so "that player" is read off the
        // TriggeringPlayer the condition stamped (CR 603.3).
        Majik.Core.Tests.Helpers.ContextResolve.Resolve(diesTrigger, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(18, "the opponent whose creature died loses 2 life");
        _alice.LifeTotal.Should().Be(20, "the controller is unaffected by the drain");
    }

    [Fact]
    public void MassacreWurm_OwnCreatureDies_DoesNotFireDiesTrigger()
    {
        // "a creature an opponent controls dies" — the controller's OWN
        // creature dying must NOT satisfy the dies predicate (CR 102.1).
        var wurm = MassacreWurmFactory.Create(_alice, eventBus: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(wurm);
        wurm.SetZone(ZoneType.Battlefield);

        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceBear.SetOwner(_alice);
        aliceBear.SetController(_alice);

        var diesEvent = new CardMovedEvent(
            aliceBear, ZoneType.Battlefield, ZoneType.Graveyard, lkiController: _alice);

        wurm.Abilities.OfType<TriggeredAbility>()
            .Where(t => t.IsTriggered(diesEvent))
            .Should().BeEmpty("the controller's own creature dying does not fire the drain");
    }

    [Fact]
    public void MassacreWurm_NonCreatureDies_DoesNotFireDiesTrigger()
    {
        // CR 700.4 — "dies" applies only to creatures. An opponent's artifact
        // moving Battlefield → Graveyard must not satisfy the dies predicate.
        var wurm = MassacreWurmFactory.Create(_alice, eventBus: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(wurm);
        wurm.SetZone(ZoneType.Battlefield);

        var bobTrinket = new Artifact("Trinket", "{0}");
        bobTrinket.SetOwner(_bob);
        bobTrinket.SetController(_bob);

        var moveEvent = new CardMovedEvent(
            bobTrinket, ZoneType.Battlefield, ZoneType.Graveyard, lkiController: _bob);

        wurm.Abilities.OfType<TriggeredAbility>()
            .Where(t => t.IsTriggered(moveEvent))
            .Should().BeEmpty("a non-creature moving to graveyard does not fire the drain — CR 700.4");
    }
}
