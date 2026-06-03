using FluentAssertions;
using Majik.Bot.Heuristic;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Bot.Tests;

/// <summary>
/// Exercises <see cref="MultikickerPolicy"/> — the bot's "how many times do I
/// pay this multikicker?" heuristic (CR 702.32a — the number of times is
/// chosen at announcement, bounded by available mana). Pays down the
/// bot-multikick-times-heuristic deferral: the Multikicker engine
/// (<see cref="MultikickerAdditionalCost"/> + <see cref="Card.TimesKicked"/>)
/// was already built; the gap was the EV-search policy having no rule for the
/// count. The policy implements the "pay once more while mana remains and EV
/// improves" loop the deferral sketch called for.
/// </summary>
public class MultikickerPolicyTests
{
    private readonly Player _alice = new("Alice", 20);

    // ---- Core "pay while mana remains" loop (default monotone-positive EV) ----

    [Fact]
    public void ChooseTimes_PaysAllAffordableKicks_WhenEachKickIsPositiveEv()
    {
        // {2} per kick, 6 mana available beyond the base cost → 3 kicks.
        var perKick = ManaCost.Parse("{2}");
        var times = MultikickerPolicy.ChooseTimes(perKick, manaAvailable: 6);
        times.Should().Be(3);
    }

    [Fact]
    public void ChooseTimes_FloorsToAffordableCount_WhenManaDoesNotDivideEvenly()
    {
        // {2} per kick, 7 mana → only 3 full kicks fit (6 mana), 1 left over.
        var perKick = ManaCost.Parse("{2}");
        var times = MultikickerPolicy.ChooseTimes(perKick, manaAvailable: 7);
        times.Should().Be(3);
    }

    [Fact]
    public void ChooseTimes_ReturnsZero_WhenNoManaForEvenOneKick()
    {
        // {2} per kick, 1 mana → can't afford a single kick → decline (legal,
        // CR 702.32a — zero times is allowed).
        var perKick = ManaCost.Parse("{2}");
        var times = MultikickerPolicy.ChooseTimes(perKick, manaAvailable: 1);
        times.Should().Be(0);
    }

    [Fact]
    public void ChooseTimes_ReturnsZero_WhenPerKickCostIsZeroOrFree()
    {
        // Guard against a divide-by-zero / infinite multikick on a {0} kick.
        var times = MultikickerPolicy.ChooseTimes(ManaCost.Zero, manaAvailable: 10);
        times.Should().Be(0);
    }

    // ---- EV gate stops the loop even when mana remains ----

    [Fact]
    public void ChooseTimes_StopsWhenEvGateDeclines_EvenWithManaRemaining()
    {
        // 10 mana, {1} per kick → 10 kicks affordable, but the EV gate only
        // values the first 2 kicks (e.g. a spell whose payoff caps out). The
        // loop must stop at the EV ceiling, not spend to the mana floor.
        var perKick = ManaCost.Parse("{1}");
        var times = MultikickerPolicy.ChooseTimes(
            perKick, manaAvailable: 10,
            kickIsWorthIt: nextTimes => nextTimes <= 2);
        times.Should().Be(2);
    }

    [Fact]
    public void ChooseTimes_RespectsBoth_EvCeilingAndManaFloor()
    {
        // EV gate would allow up to 5 kicks, but only 3 are affordable.
        // Result = min(EV ceiling, mana floor) = 3.
        var perKick = ManaCost.Parse("{2}");
        var times = MultikickerPolicy.ChooseTimes(
            perKick, manaAvailable: 6,
            kickIsWorthIt: nextTimes => nextTimes <= 5);
        times.Should().Be(3);
    }

    // ---- End-to-end with the real card + cost surface ----

    [Fact]
    public void BuildMultikicker_ForEverflowingChalice_StampsChosenTimes()
    {
        // 6 mana available, {2} multikicker → 3 kicks. Building the additional
        // cost through the chalice's own factory builder and paying it stamps
        // TimesKicked = 3 so the ETB scales to 3 charge counters (CR 702.32c).
        var chalice = EverflowingChaliceFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(chalice);

        var times = MultikickerPolicy.ChooseTimes(
            EverflowingChaliceFactory.MultikickerCost, manaAvailable: 6);
        times.Should().Be(3);

        var cost = EverflowingChaliceFactory.BuildAdditionalCost(chalice, times);
        cost.Should().BeOfType<MultikickerAdditionalCost>()
            .Which.Times.Should().Be(3);

        // Fund the pool with 2·3 = 6 and pay — the chalice stamps its kick count.
        _alice.AddManaToPool(ManaCost.Parse("{6}"));
        cost.Pay(_alice).Should().BeTrue();
        chalice.TimesKicked.Should().Be(3);
    }

    [Fact]
    public void BuildMultikicker_DeclinesCleanly_WhenZeroTimesChosen()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(chalice);

        // No mana for a {2} kick → 0 times → still a legal, payable additional
        // cost that stamps TimesKicked = 0 (the chalice enters with 0 charge
        // counters).
        var times = MultikickerPolicy.ChooseTimes(
            EverflowingChaliceFactory.MultikickerCost, manaAvailable: 1);
        times.Should().Be(0);

        var cost = EverflowingChaliceFactory.BuildAdditionalCost(chalice, times);
        cost.Pay(_alice).Should().BeTrue();
        chalice.TimesKicked.Should().Be(0);
    }
}
