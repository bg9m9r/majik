using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ServantOfTheScaleFactory"/> (Aether Revolt, {G}).
///
/// Card: Servant of the Scale — Creature — Human Soldier {G} 0/0.
///   "This creature enters with a +1/+1 counter on it.
///    When this creature dies, put X +1/+1 counters on target creature you
///    control, where X is the number of +1/+1 counters on this creature."
///
/// Covers:
///   - Identity / dispatch (printed 0/0, {G}, green Human Soldier).
///   - Single dies trigger structure (mandatory single target creature you
///     control).
///   - Enters-with-counter: entering via ZoneService + ReplacementBus places
///     one +1/+1 counter (CR 614.1d).
///   - Dies: puts X +1/+1 counters on the chosen target creature where X is the
///     counter count on the dying Servant (CR 608.2g last-known-information).
///   - Dies with zero counters → no-op.
/// </summary>
[Trait("Color", "G")]
public class ServantOfTheScaleFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void EnterBattlefield(Creature card, Player owner, ReplacementBus bus)
    {
        owner.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, owner);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ServantOfTheScale_Identity()
    {
        var c = ServantOfTheScaleFactory.Create(_alice);

        c.Name.Should().Be("Servant of the Scale");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.Power.Should().Be(0, "printed 0/0 — the +1/+1 ETB counter makes it a 1/1 on the battlefield");
        c.Toughness.Should().Be(0);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ServantOfTheScale()
    {
        var card = NamedCardFactory.Create("Servant of the Scale", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Servant of the Scale");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{G}");
    }

    [Fact]
    public void ServantOfTheScale_HasSingleMandatoryTargetDiesTrigger()
    {
        var c = ServantOfTheScaleFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "single dies trigger");

        var dies = triggers[0];
        dies.TargetRequests.Should().HaveCount(1);
        dies.TargetRequests[0].MinTargets.Should().Be(1,
            "printed 'target creature you control' is a mandatory single target (CR 601.2c)");
        dies.TargetRequests[0].MaxTargets.Should().Be(1);
        dies.TargetRequests[0].Description.Should().Contain("creature");
    }

    // -----------------------------------------------------------------------
    // Enters-with-counter (CR 614.1d / CR 122.1g)
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersWithOnePlusOnePlusOneCounter()
    {
        var bus = new ReplacementBus();
        var card = ServantOfTheScaleFactory.Create(_alice, triggers: null, replacements: bus);

        EnterBattlefield(card, _alice, bus);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Servant of the Scale enters with one +1/+1 counter on it (CR 614.1d)");
    }

    [Fact]
    public void NoReplacementBus_EntersVanilla()
    {
        var bus = new ReplacementBus();
        var card = ServantOfTheScaleFactory.Create(_alice); // no replacement bus wired

        EnterBattlefield(card, _alice, bus);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no enters-with-counter replacement registered on the shape path → no counter");
    }

    // -----------------------------------------------------------------------
    // Dies trigger (CR 603.6c / CR 700.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void Dies_PutsXCountersOnTargetCreatureYouControl()
    {
        var servant = ServantOfTheScaleFactory.Create(_alice);
        // Simulate Servant having accumulated 3 +1/+1 counters (ETB counter +
        // external pump such as Hardened Scales / adapt).
        servant.Counters.Add(CounterType.PlusOnePlusOne, 3);

        // Servant has died — already in the graveyard (CR 608.2g LKI; counters
        // persist on the card object until the next cleanup step, CR 514.2).
        servant.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(servant);

        // Target creature the controller controls.
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_alice);
        grizzly.SetController(_alice);
        grizzly.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(grizzly);

        var dies = servant.Abilities.OfType<TriggeredAbility>().Single();
        dies.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });
        foreach (var e in dies.Effects) e.Execute();

        grizzly.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "X = number of +1/+1 counters on the dying Servant (3) placed on the target");
    }

    [Fact]
    public void Dies_ZeroCounters_NoOp()
    {
        var servant = ServantOfTheScaleFactory.Create(_alice);
        // No counters on Servant (never landed via ETB-counter path in this
        // shape test) → X = 0 → no-op even with a valid target.
        servant.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(servant);

        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_alice);
        grizzly.SetController(_alice);
        grizzly.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(grizzly);

        var dies = servant.Abilities.OfType<TriggeredAbility>().Single();
        dies.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });
        foreach (var e in dies.Effects) e.Execute();

        grizzly.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "X = 0 +1/+1 counters on Servant → no counters placed");
    }
}
