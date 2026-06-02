using System;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MirrodinsCoreFactory"/> (Fifth Dawn).
///
/// Mirrodin's Core — Land.
///   "{T}: Add {C}.
///    {T}: Put a charge counter on this land.
///    {T}, Remove a charge counter from this land: Add one mana of any
///    color."
///
/// Covers:
/// - Identity (Land, nonbasic) + <see cref="NamedCardFactory"/> dispatch.
/// - Enters untapped with no counters (no ETB trigger — unlike Vivid Crag).
/// - One {C} mana ability (no counter cost).
/// - One non-mana <see cref="ActivatedAbility"/> that puts a charge counter.
/// - Five WUBRG any-colour mana abilities, each removing a charge counter.
/// - The {C} ability activates with no charge counters and removes none.
/// - Activating an any-colour ability removes one charge counter, produces
///   the chosen colour, AND taps the land (CR 605 — cost includes {T}).
/// - The any-colour abilities are un-activatable with no charge counters; the
///   {C} ability still is.
/// - Once tapped, no mana ability can be activated.
/// </summary>
public class MirrodinsCoreTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MirrodinsCore_Identity()
    {
        var land = MirrodinsCoreFactory.Create(_alice);

        land.Name.Should().Be("Mirrodin's Core");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Mirrodin's Core is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MirrodinsCore()
    {
        var card = NamedCardFactory.Create("Mirrodin's Core", _alice);

        card.Should().BeOfType<Land>();
        card!.Name.Should().Be("Mirrodin's Core");
        card.HasType(CardType.Land).Should().BeTrue();

        // No ETB trigger — enters untapped with no counters.
        card.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Mirrodin's Core has no enters-the-battlefield trigger");

        // Six mana abilities: {C} plus five WUBRG any-colour.
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(6,
            "one {C} ability plus one any-colour ability per WUBRG");

        // One non-mana activated ability: {T}: Put a charge counter.
        // ManaAbility is a separate type (not an ActivatedAbility subclass), so
        // OfType<ActivatedAbility> already excludes the six mana abilities.
        card.Abilities.OfType<ActivatedAbility>()
            .Should().HaveCount(1, "the {T}: Put a charge counter activated ability");
    }

    [Fact]
    public void MirrodinsCore_EntersUntapped_NoCounters()
    {
        var land = MirrodinsCoreFactory.Create(_alice);

        land.IsTapped.Should().BeFalse("Mirrodin's Core enters untapped");
        land.Counters.Count(CounterType.Charge).Should().Be(0,
            "Mirrodin's Core enters with no charge counters");
    }

    // -----------------------------------------------------------------------
    // Mana ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void MirrodinsCore_HasSixManaAbilities_ColorlessPlusFiveAnyColor()
    {
        var land = MirrodinsCoreFactory.Create(_alice);
        var mas = land.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(6, "one {C} ability + five WUBRG any-colour abilities");

        mas.Count(m => m.ManaGenerated.White == 1 && m.ManaGenerated.TotalValue == 1).Should().Be(1, "{W}");
        mas.Count(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.TotalValue == 1).Should().Be(1, "{U}");
        mas.Count(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.TotalValue == 1).Should().Be(1, "{B}");
        mas.Count(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.TotalValue == 1).Should().Be(1, "{R}");
        mas.Count(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.TotalValue == 1).Should().Be(1, "{G}");

        // {C} is colorless: ManaCost.Parse("C") folds colorless into Generic
        // (same as Crumbling Vestige). No coloured pips, total value 1.
        mas.Count(m => m.ManaGenerated.Generic == 1
                       && m.ManaGenerated.TotalValue == 1
                       && m.ManaGenerated.White == 0
                       && m.ManaGenerated.Blue == 0
                       && m.ManaGenerated.Black == 0
                       && m.ManaGenerated.Red == 0
                       && m.ManaGenerated.Green == 0)
            .Should().Be(1, "the base {C} ability");

        // Behavioural split: on the battlefield with no charge counters, exactly
        // one mana ability (the {C}) is activatable; the five any-colour
        // abilities are not (no counter to remove).
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        mas.Count(m => m.CanActivate()).Should().Be(1,
            "with no charge counters only the {C} ability (no counter cost) is activatable");
        mas.Count(m => !m.CanActivate()).Should().Be(5,
            "the five charge-gated any-colour abilities cannot pay their remove-a-charge-counter cost");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void MirrodinsCore_Colorless_ActivatesWithoutCounters_RemovesNone()
    {
        var land = MirrodinsCoreFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var colorless = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Generic == 1 && m.ManaGenerated.TotalValue == 1
                         && m.CanActivate());

        colorless.CanActivate().Should().BeTrue(
            "the base {T}: Add {C} ability needs no charge counter");

        var produced = colorless.Activate();
        produced.Generic.Should().Be(1);
        produced.TotalValue.Should().Be(1);

        land.Counters.Count(CounterType.Charge).Should().Be(0,
            "the {C} ability removes no charge counter");
        land.IsTapped.Should().BeTrue("CR 605 — the activation cost includes {T}");
    }

    // -----------------------------------------------------------------------
    // {T}: Put a charge counter on this land
    // -----------------------------------------------------------------------

    [Fact]
    public void MirrodinsCore_ChargeAbility_PutsCounter_AndTaps()
    {
        var land = MirrodinsCoreFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // ManaAbility is not an ActivatedAbility subclass, so OfType already
        // isolates the single non-mana "{T}: Put a charge counter" ability.
        var charge = land.Abilities.OfType<ActivatedAbility>().Single();

        // The effect adds one charge counter (CR 122.1).
        foreach (var e in charge.Effects) e.Execute();

        land.Counters.Count(CounterType.Charge).Should().Be(1,
            "{T}: Put a charge counter on this land (CR 122.1)");
    }

    // -----------------------------------------------------------------------
    // {T}, Remove a charge counter: Add one mana of any color
    // -----------------------------------------------------------------------

    [Fact]
    public void MirrodinsCore_AnyColor_Activate_RemovesChargeCounter_ProducesColor_AndTaps()
    {
        var land = MirrodinsCoreFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.Counters.Add(CounterType.Charge, 2);

        var anyGreen = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Green == 1);

        anyGreen.CanActivate().Should().BeTrue(
            "untapped land with charge counters can pay {T} + remove a charge counter");

        var produced = anyGreen.Activate();
        produced.Green.Should().Be(1);
        produced.TotalValue.Should().Be(1);

        land.Counters.Count(CounterType.Charge).Should().Be(1,
            "activating the any-colour ability removes one charge counter");
        land.IsTapped.Should().BeTrue(
            "CR 605 — the activation cost includes {T}; the land taps");
    }

    [Fact]
    public void MirrodinsCore_NoChargeCounters_OnlyColorlessActivatable()
    {
        var land = MirrodinsCoreFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        // Untapped, no charge counters.

        land.IsTapped.Should().BeFalse();

        // The five any-colour abilities (coloured pips, Generic == 0) cannot
        // pay their remove-a-charge-counter cost.
        land.Abilities.OfType<ManaAbility>()
            .Count(m => m.ManaGenerated.TotalValue == 1 && m.ManaGenerated.Generic == 0 && m.CanActivate())
            .Should().Be(0, "no charge counter to remove → no any-colour ability is activatable");

        // Exactly one mana ability — the {C} — is activatable.
        land.Abilities.OfType<ManaAbility>().Count(m => m.CanActivate()).Should().Be(1,
            "only the base {T}: Add {C} ability (no charge-counter cost) is activatable");
    }

    [Fact]
    public void MirrodinsCore_CannotActivateManaAbilities_WhenTapped()
    {
        var land = MirrodinsCoreFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.Counters.Add(CounterType.Charge, 2);

        // Tap the land by activating the {C} ability.
        var colorless = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Generic == 1 && m.ManaGenerated.TotalValue == 1);
        colorless.Activate();
        land.IsTapped.Should().BeTrue();

        // CR 605.3a — a tapped permanent can't pay the printed {T} cost.
        foreach (var ma in land.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse(
                "a tapped land can't pay {T} regardless of remaining charge counters");
        }
    }
}
