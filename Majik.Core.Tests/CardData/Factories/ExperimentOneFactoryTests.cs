using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Experiment One (Gatecrash, {G}, Creature — Human Ooze 1/1).
///
/// Oracle text (Scryfall, verified):
///   "Evolve (Whenever a creature you control enters, if that creature
///    has greater power or toughness than this creature, put a +1/+1
///    counter on this creature.)
///    Remove two +1/+1 counters from this creature: Regenerate it."
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Evolve trigger condition (CR 702.100b):
///       * matches a larger creature you control (greater power OR toughness);
///       * does NOT match a same-or-smaller creature you control;
///       * does NOT match an opponent's creature;
///       * does NOT match Experiment One's own entry.
///   - Evolve effect: places a +1/+1 counter on Experiment One (CR 702.100c).
///   - Regenerate activated ability shape: the only cost is removing two
///     +1/+1 counters; resolution adds a regeneration shield
///     (CR 701.18 / CR 701.15a).
///   - Regenerate cost gate: cannot pay with fewer than two counters.
/// </summary>
[Trait("Color", "G")]
public class ExperimentOneFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static CardMovedEvent EntersBattlefield(ICard card) =>
        new(card, ZoneType.Hand, ZoneType.Battlefield);

    private Creature Creature(Player controller, int power, int toughness, string name = "Test")
    {
        var c = new Creature(name, "{G}", power, toughness);
        c.SetOwner(controller);
        c.SetController(controller);
        return c;
    }

    // ------------------------------------------------------------------
    // Identity
    // ------------------------------------------------------------------

    [Fact]
    public void ExperimentOne_Identity()
    {
        var c = ExperimentOneFactory.Create(_alice);

        c.Name.Should().Be("Experiment One");
        c.ManaCost.Should().Be("{G}");
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Ooze).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void ExperimentOne_HasExactlyOneEvolveTriggerAndOneRegenerateAbility()
    {
        var c = ExperimentOneFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().ContainSingle("Evolve is one triggered ability");
        c.Abilities.OfType<ActivatedAbility>().Should().ContainSingle("Regenerate is one activated ability");
    }

    // ------------------------------------------------------------------
    // Evolve — trigger condition
    // ------------------------------------------------------------------

    [Fact]
    public void Evolve_Matches_WhenLargerCreatureYouControlEnters()
    {
        var exp = ExperimentOneFactory.Create(_alice);
        exp.SetZone(ZoneType.Battlefield);

        var bigger = Creature(_alice, 2, 2, "Bigger"); // greater power AND toughness
        var trigger = exp.Abilities.OfType<TriggeredAbility>().Single();

        trigger.Condition.Matches(EntersBattlefield(bigger), trigger).Should().BeTrue(
            "evolve triggers when a larger creature you control enters (CR 702.100b)");
    }

    [Fact]
    public void Evolve_Matches_WhenOnlyGreaterToughness()
    {
        var exp = ExperimentOneFactory.Create(_alice);
        exp.SetZone(ZoneType.Battlefield);

        var tall = Creature(_alice, 1, 3, "Tall"); // equal power, greater toughness
        var trigger = exp.Abilities.OfType<TriggeredAbility>().Single();

        trigger.Condition.Matches(EntersBattlefield(tall), trigger).Should().BeTrue(
            "evolve triggers on greater toughness alone (CR 702.100b — 'power or toughness')");
    }

    [Fact]
    public void Evolve_DoesNotMatch_SameOrSmallerCreature()
    {
        var exp = ExperimentOneFactory.Create(_alice);
        exp.SetZone(ZoneType.Battlefield);

        var same = Creature(_alice, 1, 1, "Same");
        var trigger = exp.Abilities.OfType<TriggeredAbility>().Single();

        trigger.Condition.Matches(EntersBattlefield(same), trigger).Should().BeFalse(
            "evolve does not trigger when the entering creature is not larger (CR 702.100b)");
    }

    [Fact]
    public void Evolve_DoesNotMatch_OpponentCreature()
    {
        var exp = ExperimentOneFactory.Create(_alice);
        exp.SetZone(ZoneType.Battlefield);

        var enemy = Creature(_bob, 5, 5, "Enemy");
        var trigger = exp.Abilities.OfType<TriggeredAbility>().Single();

        trigger.Condition.Matches(EntersBattlefield(enemy), trigger).Should().BeFalse(
            "evolve only watches 'a creature you control' (CR 702.100b)");
    }

    [Fact]
    public void Evolve_DoesNotMatch_OwnEntry()
    {
        var exp = ExperimentOneFactory.Create(_alice);
        exp.SetZone(ZoneType.Battlefield);

        var trigger = exp.Abilities.OfType<TriggeredAbility>().Single();

        trigger.Condition.Matches(EntersBattlefield(exp), trigger).Should().BeFalse(
            "Experiment One's own entry never has 'greater power or toughness than this creature'");
    }

    [Fact]
    public void Evolve_DoesNotMatch_NonCreature()
    {
        var exp = ExperimentOneFactory.Create(_alice);
        exp.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Mox", "{0}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);

        var trigger = exp.Abilities.OfType<TriggeredAbility>().Single();

        trigger.Condition.Matches(EntersBattlefield(artifact), trigger).Should().BeFalse(
            "evolve watches creatures, not artifacts");
    }

    // ------------------------------------------------------------------
    // Evolve — effect resolution
    // ------------------------------------------------------------------

    [Fact]
    public void Evolve_OnResolve_PlacesPlusOnePlusOneCounter()
    {
        var exp = ExperimentOneFactory.Create(_alice);
        exp.SetZone(ZoneType.Battlefield);

        var trigger = exp.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        exp.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "evolve places a +1/+1 counter on Experiment One (CR 702.100c)");
    }

    // ------------------------------------------------------------------
    // Regenerate — activated ability
    // ------------------------------------------------------------------

    [Fact]
    public void Regenerate_Cost_IsRemoveTwoCountersNoMana()
    {
        var c = ExperimentOneFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "Regenerate's only cost is removing counters — no mana");
        var counterCost = ability.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();
        counterCost.Amount.Should().Be(2,
            "the cost is 'remove two +1/+1 counters from this creature'");
    }

    [Fact]
    public void Regenerate_CannotPay_WithFewerThanTwoCounters()
    {
        var c = ExperimentOneFactory.Create(_alice);
        c.Counters.Add(CounterType.PlusOnePlusOne, 1);

        var counterCost = c.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        counterCost.CanPay(_alice).Should().BeFalse(
            "fewer than two +1/+1 counters cannot pay the regenerate cost");
    }

    [Fact]
    public void Regenerate_AddsShield_AndConsumesTwoCounters()
    {
        var c = ExperimentOneFactory.Create(_alice);
        c.Counters.Add(CounterType.PlusOnePlusOne, 2);
        c.SetZone(ZoneType.Battlefield);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var counterCost = ability.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        counterCost.CanPay(_alice).Should().BeTrue();
        counterCost.Pay(_alice);
        foreach (var effect in ability.Effects) effect.Execute();

        c.HasRegenerationShield.Should().BeTrue(
            "regenerate creates a regeneration shield (CR 701.18 / CR 701.15a)");
        c.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "two +1/+1 counters are removed as the activation cost");
    }
}
