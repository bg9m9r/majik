using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="QuirionBeastcallerFactory"/> (Dominaria United,
/// {G}).
///
/// Covers shape, single dies trigger structure, dies-distribute behaviour
/// with a single deterministic target. The "enters with N +1/+1 counters
/// for each other creature spell you've cast this turn" ETB-counter half
/// is a documented v1 gap (no per-turn creature-spell-cast tally on
/// <see cref="Majik.Core.Game.TurnState"/> yet); tests exercise the
/// dies-distribute half by pre-stamping counters on Quirion directly so
/// the live counter-read on the dying card has something to dump.
/// </summary>
public class QuirionBeastcallerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Quirion_Identity()
    {
        var c = QuirionBeastcallerFactory.Create(_alice);

        c.Name.Should().Be("Quirion Beastcaller");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Quirion_HasSingleDiesTrigger()
    {
        var c = QuirionBeastcallerFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "single dies trigger");

        var dies = triggers[0];
        dies.TargetRequests.Should().HaveCount(1);
        dies.TargetRequests[0].MinTargets.Should().Be(0,
            "printed 'any number of target creatures' allows the zero-target path (CR 601.2c)");
        dies.TargetRequests[0].Description.Should().Contain("creature");
    }

    [Fact]
    public void Quirion_Dies_DumpsCountersOnChosenTarget()
    {
        var quirion = QuirionBeastcallerFactory.Create(_alice);
        // Pre-stamp 3 +1/+1 counters on Quirion (simulating either the
        // ETB-counter half once tracking lands, or external sources like
        // Hardened Scales / adapt counters).
        quirion.Counters.Add(CounterType.PlusOnePlusOne, 3);

        // Quirion is in the graveyard (just died — CR 608.2g
        // last-known-information).
        quirion.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(quirion);

        // Target creature on the battlefield.
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_alice);
        grizzly.SetController(_alice);
        grizzly.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(grizzly);

        var dies = quirion.Abilities.OfType<TriggeredAbility>().Single();
        dies.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });
        foreach (var e in dies.Effects) e.Execute();

        grizzly.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "all 3 +1/+1 counters on the dying Quirion are placed on the chosen target (v1 single-target collapse)");
    }

    [Fact]
    public void Quirion_Dies_ZeroCounters_NoOp()
    {
        // Default ETB-counter half is currently 0 (creature-spell-cast
        // tracking deferred). With 0 counters on Quirion, the dies trigger
        // is a no-op even with a valid target.
        var quirion = QuirionBeastcallerFactory.Create(_alice);
        quirion.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(quirion);

        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_alice);
        grizzly.SetController(_alice);
        grizzly.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(grizzly);

        var dies = quirion.Abilities.OfType<TriggeredAbility>().Single();
        dies.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });
        foreach (var e in dies.Effects) e.Execute();

        grizzly.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no counters on Quirion → distribute 0 → no counters placed");
    }

    [Fact]
    public void Quirion_Dies_NoTarget_NoOp()
    {
        // Printed "any number of target creatures" — choosing zero is
        // legal (CR 601.2c). With no target the counters simply go nowhere.
        var quirion = QuirionBeastcallerFactory.Create(_alice);
        quirion.Counters.Add(CounterType.PlusOnePlusOne, 5);
        quirion.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(quirion);

        var dies = quirion.Abilities.OfType<TriggeredAbility>().Single();
        dies.SetChosenTargets(new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
        });
        foreach (var e in dies.Effects) e.Execute();

        // Nothing crashes; counters remain on the dying Quirion (until
        // cleanup) and don't leak onto anything else.
        quirion.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(5);
    }

    [Fact]
    public void Quirion_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Quirion Beastcaller", _alice);

        c.Should().NotBeNull();
        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Quirion Beastcaller");
        ((Creature)c).Power.Should().Be(1);
        ((Creature)c).Toughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue();
    }
}
