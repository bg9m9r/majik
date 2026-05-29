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
/// Unit tests for <see cref="PentadPrismFactory"/> (Fifth Dawn, {2}).
///
/// Pentad Prism — Artifact.
///   "Sunburst (This artifact enters with a charge counter on it for each
///    color of mana spent to cast it.)"
///   "Remove a charge counter from this artifact: Add one mana of any color."
///
/// Covers:
/// - Identity (Artifact, {2}) + <see cref="NamedCardFactory"/> dispatch.
/// - Sunburst keyword marker + ETB trigger (CR 702.44 — non-creature branch
///   lands charge counters).
/// - Sunburst ETB with N colors paid → N charge counters.
/// - Five mana abilities (one per WUBRG) — "Add one mana of any color".
/// - Activating a colour ability removes one charge counter, produces the
///   chosen colour, and does NOT tap the prism (CR 605 — no {T} in the cost).
/// - Mana abilities are un-activatable when no charge counters remain.
/// </summary>
public class PentadPrismTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PentadPrism_IsArtifact_TwoCost()
    {
        var prism = PentadPrismFactory.Create(_alice);

        prism.Name.Should().Be("Pentad Prism");
        prism.HasType(CardType.Artifact).Should().BeTrue();
        prism.HasType(CardType.Creature).Should().BeFalse();
        prism.ManaCost.Should().Be("{2}");
        prism.Owner.Should().BeSameAs(_alice);
        prism.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PentadPrism()
    {
        var card = NamedCardFactory.Create("Pentad Prism", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Pentad Prism");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{2}");
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Sunburst",
                "Sunburst keyword marker surfaced");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Sunburst ETB trigger surfaced for shape");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(5,
            "one mana ability per WUBRG colour");
    }

    // -----------------------------------------------------------------------
    // Sunburst ETB (CR 702.44a — non-creature → charge counters)
    // -----------------------------------------------------------------------

    [Fact]
    public void PentadPrism_EtbWithTwoColorsPaid_AddsTwoChargeCounters()
    {
        var prism = PentadPrismFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(prism);
        prism.SetZone(ZoneType.Battlefield);

        prism.SetPendingCastColors(new[] { ManaColor.White, ManaColor.Blue });

        var etb = prism.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        prism.Counters.Count(CounterType.Charge).Should().Be(2,
            "two colors of mana spent → two charge counters (CR 702.44a, non-creature branch)");
        prism.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "Pentad Prism is a non-creature artifact — Sunburst uses charge counters, not +1/+1");
    }

    [Fact]
    public void PentadPrism_EtbWithZeroColors_AddsNoCounters()
    {
        var prism = PentadPrismFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(prism);
        prism.SetZone(ZoneType.Battlefield);
        prism.SetPendingCastColors(Array.Empty<ManaColor>());

        var etb = prism.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        prism.Counters.Count(CounterType.Charge).Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Mana abilities — "Remove a charge counter: Add one mana of any color"
    // -----------------------------------------------------------------------

    [Fact]
    public void PentadPrism_HasFiveManaAbilities_OnePerColor()
    {
        var prism = PentadPrismFactory.Create(_alice);
        var mas = prism.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(5, "one ManaAbility per WUBRG colour");

        mas.Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.TotalValue == 1);
    }

    [Fact]
    public void PentadPrism_Activate_RemovesChargeCounter_ProducesColor_DoesNotTap()
    {
        var prism = PentadPrismFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(prism);
        prism.SetZone(ZoneType.Battlefield);
        prism.Counters.Add(CounterType.Charge, 2);

        var mas = prism.Abilities.OfType<ManaAbility>().ToList();
        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeTrue("prism has charge counters to remove");
        }

        // Activate the green option.
        var green = mas.Single(m => m.ManaGenerated.Green == 1);
        var produced = green.Activate();

        produced.Green.Should().Be(1);
        produced.TotalValue.Should().Be(1);

        prism.Counters.Count(CounterType.Charge).Should().Be(1,
            "activating the mana ability removes one charge counter");
        prism.IsTapped.Should().BeFalse(
            "CR 605 — the activation cost is 'remove a charge counter', not {T}; the prism stays untapped");
    }

    [Fact]
    public void PentadPrism_Activate_TwiceFromTwoCounters_ThenLocksOut()
    {
        var prism = PentadPrismFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(prism);
        prism.SetZone(ZoneType.Battlefield);
        prism.Counters.Add(CounterType.Charge, 2);

        var mas = prism.Abilities.OfType<ManaAbility>().ToList();

        mas.Single(m => m.ManaGenerated.Red == 1).Activate();
        mas.Single(m => m.ManaGenerated.Blue == 1).Activate();

        prism.Counters.Count(CounterType.Charge).Should().Be(0,
            "two activations removed both charge counters");

        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeFalse(
                "no charge counters left — the cost can no longer be paid (CR 605.3a)");
        }
    }

    [Fact]
    public void PentadPrism_NoChargeCounters_CannotActivate()
    {
        var prism = PentadPrismFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(prism);
        prism.SetZone(ZoneType.Battlefield);
        // No charge counters (e.g. cast with only colorless mana → Sunburst
        // added zero).

        foreach (var ma in prism.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse(
                "no charge counter to remove → cost cannot be paid");
        }
    }
}
