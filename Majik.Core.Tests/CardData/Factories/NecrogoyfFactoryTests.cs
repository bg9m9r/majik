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
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="NecrogoyfFactory"/> (Modern Horizons 2,
/// {3}{B}{B}).
///
/// Creature — Lhurgoyf */4. Oracle text (verified against Scryfall 2026-06-14):
///   "Necrogoyf's power is equal to the number of creature cards in all
///    graveyards.
///    At the beginning of each player's upkeep, that player discards a card.
///    Madness {1}{B}{B}"
///
/// Covers the card's UNIQUE non-madness behaviour:
/// - Identity (name, type, mana cost, fixed 0/4 placeholder, Lhurgoyf).
/// - CDA power = creature cards in all graveyards (toughness fixed at 4).
/// - "At the beginning of each player's upkeep, that player discards a card"
///   fires on EVERY player's upkeep and makes THAT player discard.
/// - NamedCardFactory dispatch.
///
/// Madness {1}{B}{B} is intrinsic (CR 702.35 — MadnessCatalog + the discard
/// funnel cover it) so it is intentionally not tested here.
/// </summary>
[Trait("Color", "B")]
public class NecrogoyfFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects;
    private readonly ZoneService _zones;

    public NecrogoyfFactoryTests()
    {
        _effects = new ContinuousEffectsService(_bus);
        _zones = new ZoneService(_bus);
    }

    private System.Func<System.Collections.Generic.IEnumerable<ICard>> AllGraveyards => () =>
        _alice.Zones.Graveyard.GetCards().Concat(_bob.Zones.Graveyard.GetCards());

    private Creature WireNecrogoyf(Player owner)
    {
        var goyf = NecrogoyfFactory.Create(owner, _effects, _bus, AllGraveyards);
        goyf.ActiveEffects = _effects;
        return goyf;
    }

    [Fact]
    public void Necrogoyf_Identity()
    {
        var goyf = NecrogoyfFactory.Create(_alice);

        goyf.Name.Should().Be("Necrogoyf");
        goyf.ManaCost.Should().Be("{3}{B}{B}");
        goyf.HasType(CardType.Creature).Should().BeTrue();
        goyf.HasSubtype(CardSubtype.Lhurgoyf).Should().BeTrue("Necrogoyf is a Lhurgoyf");
        goyf.BaseToughness.Should().Be(4, "the printed (fixed) toughness is 4");
        goyf.Owner.Should().BeSameAs(_alice);
        goyf.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Necrogoyf()
    {
        var goyf = NamedCardFactory.Create("Necrogoyf", _alice);

        goyf.Should().BeOfType<Creature>();
        goyf.Name.Should().Be("Necrogoyf");
        goyf.HasSubtype(CardSubtype.Lhurgoyf).Should().BeTrue();
    }

    [Fact]
    public void Necrogoyf_EmptyGraveyards_Is0Power4Toughness()
    {
        var goyf = WireNecrogoyf(_alice);
        _zones.MoveCard(goyf, ZoneType.Library, ZoneType.Battlefield, _alice);

        goyf.Power.Should().Be(0, "no creature cards in any graveyard");
        goyf.Toughness.Should().Be(4, "toughness is the fixed 4");
    }

    [Fact]
    public void Necrogoyf_PowerCountsCreatureCardsAcrossAllGraveyards()
    {
        var goyf = WireNecrogoyf(_alice);
        _zones.MoveCard(goyf, ZoneType.Library, ZoneType.Battlefield, _alice);

        var bearA = new Card("Grizzly Bears", "1G", new[] { CardType.Creature });
        bearA.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(bearA);

        var bearB = new Card("Runeclaw Bear", "1G", new[] { CardType.Creature });
        var bolt = new Card("Lightning Bolt", "R", new[] { CardType.Instant });
        foreach (var c in new[] { bearB, bolt })
        {
            c.SetOwner(_bob);
            _bob.Zones.Graveyard.AddCard(c);
        }

        // 2 creature cards across both graveyards (the instant doesn't count).
        goyf.Power.Should().Be(2);
        goyf.Toughness.Should().Be(4, "toughness stays the fixed 4 regardless of graveyards");
    }

    [Fact]
    public void UpkeepTrigger_FiresOnEachPlayersUpkeep_AndThatPlayerDiscards()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var goyf = NecrogoyfFactory.Create(_alice, _effects, _bus, AllGraveyards);
        goyf.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(goyf);
        var trigger = goyf.Abilities.OfType<TriggeredAbility>().Single();
        triggers.RegisterTriggeredAbility(trigger);

        // Give Bob a card so we can observe his discard.
        var grip = new Card("Forest", "", new[] { CardType.Land });
        grip.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(grip);

        // Bob's upkeep — the trigger fires (each player's upkeep), and BOB
        // (the upkeep player) discards a card.
        _bus.Publish(new StepStartedEvent(StepStateType.Upkeep, _bob));
        triggers.PendingCount.Should().Be(1, "each player's upkeep includes Bob's");

        foreach (var e in trigger.Effects) e.Execute();

        _bob.Zones.Hand.GetCards().Should().BeEmpty("Bob discarded his only card");
        _bob.Zones.Graveyard.GetCards().Should().Contain(grip);
    }

    [Fact]
    public void UpkeepTrigger_DoesNotFireOnNonUpkeepStep()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var goyf = NecrogoyfFactory.Create(_alice, _effects, _bus, AllGraveyards);
        goyf.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(goyf);
        triggers.RegisterTriggeredAbility(goyf.Abilities.OfType<TriggeredAbility>().Single());

        _bus.Publish(new StepStartedEvent(StepStateType.Draw, _alice));
        triggers.PendingCount.Should().Be(0, "the trigger is upkeep-only");
    }
}
