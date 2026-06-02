using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// CR 700.2d — modal "Choose one —" spell. Heartless Act, Ikoria, {1}{B}:
///   Mode 0: Destroy target creature with no counters on it.
///   Mode 1: Remove up to three counters from target creature.
///
/// Tests exercise the EffectFactory directly with crafted
/// <see cref="ChosenSpellParams"/>, mirroring BantCharmTests / IzzetCharmTests.
/// </summary>
public class HeartlessActTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static ChosenSpellParams Chosen(
        int mode, IReadOnlyList<object>[] targets, Player a, Player b) =>
        new(
            ModeIndex: mode,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { a, b });

    // -----------------------------------------------------------------------
    // Identity + dispatcher
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_MonoBlack()
    {
        var card = HeartlessActFactory.Create(_alice);

        card.Name.Should().Be("Heartless Act");
        card.HasType(CardType.Instant).Should().BeTrue();
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Black);
        card.ManaCostValue.TotalValue.Should().Be(2, because: "{1}{B} = mana value 2");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsHeartlessActShape()
    {
        var dispatched = NamedCardFactory.Create("Heartless Act", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Heartless Act");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void BuildDefinition_ExposesTwoModes_WithPerModeIntents()
    {
        var def = HeartlessActFactory.BuildDefinition(o => o);

        def.Modes.Should().HaveCount(2);
        def.Modes[HeartlessActFactory.ModeDestroyNoCounters].Should().Contain("Destroy");
        def.Modes[HeartlessActFactory.ModeRemoveCounters].Should().Contain("Remove up to three");

        def.ModeIntentsOrEmpty.Should().HaveCount(2);
        def.ModeIntentsOrEmpty[HeartlessActFactory.ModeDestroyNoCounters].Should().Be(BotIntent.Removal);
        def.ModeIntentsOrEmpty[HeartlessActFactory.ModeRemoveCounters].Should().Be(BotIntent.Removal);

        def.TargetRequests.Should().HaveCount(2);
        def.TargetRequests[HeartlessActFactory.ModeDestroyNoCounters].MinTargets.Should().Be(0);
        def.TargetRequests[HeartlessActFactory.ModeRemoveCounters].MinTargets.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Mode 0 — destroy target creature with no counters on it
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_Destroy_CounterFreeCreature_MovesToGraveyard()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = HeartlessActFactory.BuildDefinition(o => o);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bear },
            Array.Empty<object>(),
        };

        var effects = def.EffectFactory(
            Chosen(HeartlessActFactory.ModeDestroyNoCounters, targets, _alice, _bob));
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard,
            because: "a counter-free creature is destroyed by mode 0");
    }

    [Fact]
    public void Mode0_Destroy_NoOpsOnCreatureWithCounters()
    {
        var hydra = new Creature("Hangarback Walker", "{X}{X}", 0, 0) { Owner = _bob, Controller = _bob };
        hydra.SetZone(ZoneType.Battlefield);
        hydra.Counters.Add(CounterType.PlusOnePlusOne, 2);
        _bob.Zones.Battlefield.AddCard(hydra);

        var def = HeartlessActFactory.BuildDefinition(o => o);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { hydra },
            Array.Empty<object>(),
        };

        foreach (var e in def.EffectFactory(
            Chosen(HeartlessActFactory.ModeDestroyNoCounters, targets, _alice, _bob)))
            e.Execute();

        hydra.Zone.Should().Be(ZoneType.Battlefield,
            because: "mode 0 only destroys a creature with NO counters (CR 608.2b)");
    }

    [Fact]
    public void Mode0_Destroy_NoOpsOnNonCreatureTarget()
    {
        var artifact = new Artifact("Mishra's Bauble", "{0}") { Owner = _bob, Controller = _bob };
        artifact.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(artifact);

        var def = HeartlessActFactory.BuildDefinition(o => o);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { artifact },
            Array.Empty<object>(),
        };

        foreach (var e in def.EffectFactory(
            Chosen(HeartlessActFactory.ModeDestroyNoCounters, targets, _alice, _bob)))
            e.Execute();

        artifact.Zone.Should().Be(ZoneType.Battlefield,
            because: "mode 0 targets a creature, not an artifact");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — remove up to three counters from target creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_RemoveCounters_RemovesUpToThree_OfOneType()
    {
        var creature = new Creature("Walking Ballista", "{X}{X}", 0, 0) { Owner = _bob, Controller = _bob };
        creature.SetZone(ZoneType.Battlefield);
        creature.Counters.Add(CounterType.PlusOnePlusOne, 5);
        _bob.Zones.Battlefield.AddCard(creature);

        var def = HeartlessActFactory.BuildDefinition(o => o);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { creature },
        };

        var effects = def.EffectFactory(
            Chosen(HeartlessActFactory.ModeRemoveCounters, targets, _alice, _bob));
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        creature.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            because: "5 counters minus 3 removed = 2 (CR 122.5 'up to three')");
    }

    [Fact]
    public void Mode1_RemoveCounters_RemovesAllWhenFewerThanThree()
    {
        var creature = new Creature("Scavenging Ooze", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        creature.SetZone(ZoneType.Battlefield);
        creature.Counters.Add(CounterType.PlusOnePlusOne, 1);
        creature.Counters.Add(CounterType.Charge, 1);
        _bob.Zones.Battlefield.AddCard(creature);

        var def = HeartlessActFactory.BuildDefinition(o => o);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { creature },
        };

        foreach (var e in def.EffectFactory(
            Chosen(HeartlessActFactory.ModeRemoveCounters, targets, _alice, _bob)))
            e.Execute();

        creature.Counters.HasAny.Should().BeFalse(
            because: "with only 2 counters present, 'up to three' removes them all");
    }

    [Fact]
    public void Mode1_RemoveCounters_CapsTotalAtThreeAcrossTypes()
    {
        var creature = new Creature("Test Beast", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        creature.SetZone(ZoneType.Battlefield);
        creature.Counters.Add(CounterType.PlusOnePlusOne, 2);
        creature.Counters.Add(CounterType.Charge, 2);
        _bob.Zones.Battlefield.AddCard(creature);

        var def = HeartlessActFactory.BuildDefinition(o => o);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { creature },
        };

        foreach (var e in def.EffectFactory(
            Chosen(HeartlessActFactory.ModeRemoveCounters, targets, _alice, _bob)))
            e.Execute();

        var totalLeft = creature.Counters.Count(CounterType.PlusOnePlusOne)
                      + creature.Counters.Count(CounterType.Charge);
        totalLeft.Should().Be(1,
            because: "4 counters across two types minus a cap of 3 removed = 1 remaining");
    }

    // -----------------------------------------------------------------------
    // Choose-one pick-count cap
    // -----------------------------------------------------------------------

    [Fact]
    public void ChooseOne_RespectsPickCount_ExtraModesIgnored()
    {
        var def = HeartlessActFactory.BuildDefinition(o => o);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: HeartlessActFactory.ModeDestroyNoCounters,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                HeartlessActFactory.ModeDestroyNoCounters,
                HeartlessActFactory.ModeRemoveCounters, // overflow — should be dropped
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(HeartlessActFactory.PickCount,
            because: "Choose-one caps at 1 effect regardless of how many indices the caller submits");
    }
}
