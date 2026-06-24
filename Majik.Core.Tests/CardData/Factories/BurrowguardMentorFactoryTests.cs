using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BurrowguardMentorFactory"/>.
///
/// Burrowguard Mentor — {G}{W} Creature — Rabbit Soldier, printed power "*" /
/// toughness "*". Oracle text (verified against Scryfall 2026-06-23):
///   "Trample
///    Burrowguard Mentor's power and toughness are each equal to the number of
///    creatures you control."
///
/// Covers (only the card's UNIQUE behaviour + a single identity assert):
///   - Identity: {G}{W} green+white Rabbit Soldier, mana value 2, printed P/T
///     seeded 0/0 ("*").
///   - Trample keyword marker (CR 702.19, materialised from the JSON keyword
///     line).
///   - CDA: BOTH power and toughness = number of creatures you control
///     (CR 604.3 / 613.2 Layer 7a). Counts itself; opponents' creatures
///     excluded.
///
/// Dispatch + well-formedness is covered for every implemented card by
/// CardFactoryContractTests; not re-asserted here.
/// </summary>
[Trait("Color", "M")]
public class BurrowguardMentorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature NewCreature(Player controller, string name)
    {
        var creature = new Creature(name, "{G}", 2, 2);
        creature.SetOwner(controller);
        creature.SetController(controller);
        return creature;
    }

    // -----------------------------------------------------------------------
    // Identity (single *_Identity assert — non-vanilla "*" P/T seeded 0/0)
    // -----------------------------------------------------------------------

    [Fact]
    public void BurrowguardMentor_Identity_GreenWhiteRabbitSoldier_AtCostGW()
    {
        var card = BurrowguardMentorFactory.Create(_alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Burrowguard Mentor");
        card.ManaCost.Should().Be("{G}{W}");
        card.ManaCostValue.TotalValue.Should().Be(2, "{G}{W} is mana value 2");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Rabbit).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        card.BasePower.Should().Be(0, "printed power is \"*\", seeded 0 (CR 208.2c)");
        card.BaseToughness.Should().Be(0, "printed toughness is \"*\", seeded 0 (CR 208.2c)");
        CardColors.GetColors(card).Should().Contain(ManaColor.Green);
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Trample keyword marker (CR 702.19), from the JSON keyword line.
    // -----------------------------------------------------------------------

    [Fact]
    public void BurrowguardMentor_HasTrampleKeywordMarker()
    {
        var card = BurrowguardMentorFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Trample", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("the printed line includes Trample");
    }

    // -----------------------------------------------------------------------
    // CDA — BOTH power AND toughness = number of creatures you control
    // (CR 604.3 / 613.2 Layer 7a).
    // -----------------------------------------------------------------------

    [Fact]
    public void BurrowguardMentor_PowerAndToughness_EachEqualCreaturesYouControl()
    {
        var bus = new EventBus();
        // Wire the effects service to the bus so its CR-613 memoization cache
        // invalidates on game events (matches live ContinuousEffectsService).
        var effects = new ContinuousEffectsService(bus);

        Func<IEnumerable<ICard>> mine = () => _alice.Zones.Battlefield.GetCards();

        var card = BurrowguardMentorFactory.Create(_alice, effects, bus, mine);
        card.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        // ETB fires the CDA lifecycle (register the Layer-7a CDA) — same
        // CardMovedEvent path real zone moves take.
        bus.Publish(new CardMovedEvent(card, ZoneType.Hand, ZoneType.Battlefield));

        // Only Burrowguard Mentor itself is a creature you control → 1/1.
        card.Power.Should().Be(1, "it counts itself among creatures you control");
        card.Toughness.Should().Be(1, "toughness also equals creatures you control");

        // Add two more creatures under Alice's control.
        var bear = NewCreature(_alice, "Bear");
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        var wolf = NewCreature(_alice, "Wolf");
        _alice.Zones.Battlefield.AddCard(wolf);
        wolf.SetZone(ZoneType.Battlefield);

        // An opponent's creature does NOT count toward "you control".
        var bobBear = NewCreature(_bob, "BobBear");
        _bob.Zones.Battlefield.AddCard(bobBear);
        bobBear.SetZone(ZoneType.Battlefield);

        // Raw AddCard moves don't fire events; nudge the layer-pipeline
        // memoization cache the way real zone moves would (CardMovedEvent).
        bus.Publish(new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield));

        card.Power.Should().Be(3, "three creatures you control: Mentor, Bear, Wolf");
        card.Toughness.Should().Be(3, "toughness tracks the same count");
    }

    // -----------------------------------------------------------------------
    // Pure helper.
    // -----------------------------------------------------------------------

    [Fact]
    public void BurrowguardMentor_PureHelper_CountsCreatures()
    {
        var bear = new Card("Grizzly Bears", "1G", new[] { CardType.Creature });
        var bolt = new Card("Lightning Bolt", "R", new[] { CardType.Instant });
        var giant = new Card("Hill Giant", "3R", new[] { CardType.Creature });

        BurrowguardMentorFactory.CountCreatures(new ICard[] { bear, bolt, giant })
            .Should().Be(2);
        BurrowguardMentorFactory.CountCreatures(Array.Empty<ICard>())
            .Should().Be(0);
    }
}
