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
/// Unit tests for <see cref="SphereOfTheSunsFactory"/> (New Phyrexia, {2}).
///
/// Sphere of the Suns — Artifact.
///   "This artifact enters tapped and with three charge counters on it.
///    {T}, Remove a charge counter from this artifact: Add one mana of any
///    color."
///
/// Covers:
/// - Identity (Artifact, {2}) + <see cref="NamedCardFactory"/> dispatch.
/// - ETB trigger places three charge counters (CR 122 / CR 614.1d).
/// - Five mana abilities (one per WUBRG) — "Add one mana of any color".
/// - Activating a colour ability removes one charge counter, produces the
///   chosen colour, AND taps the sphere (CR 605 — the cost includes {T}).
/// - Once tapped, no further colour ability can be activated (the printed
///   {T} cost can't be paid by a tapped permanent).
/// - Mana abilities are un-activatable when no charge counters remain.
/// </summary>
public class SphereOfTheSunsTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SphereOfTheSuns_IsArtifact_TwoCost()
    {
        var sphere = SphereOfTheSunsFactory.Create(_alice);

        sphere.Name.Should().Be("Sphere of the Suns");
        sphere.HasType(CardType.Artifact).Should().BeTrue();
        sphere.HasType(CardType.Creature).Should().BeFalse();
        sphere.ManaCost.Should().Be("{2}");
        sphere.Owner.Should().BeSameAs(_alice);
        sphere.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SphereOfTheSuns_IsNotLegendary()
    {
        var sphere = SphereOfTheSunsFactory.Create(_alice);

        sphere.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SphereOfTheSuns()
    {
        var card = NamedCardFactory.Create("Sphere of the Suns", _alice);

        card.Should().BeOfType<Artifact>();
        card!.Name.Should().Be("Sphere of the Suns");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{2}");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB \"enters with three charge counters\" trigger surfaced for shape");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(5,
            "one mana ability per WUBRG colour");
    }

    // -----------------------------------------------------------------------
    // ETB — "enters ... with three charge counters on it"
    // -----------------------------------------------------------------------

    [Fact]
    public void SphereOfTheSuns_HasExactlyOneEtbTrigger()
    {
        var sphere = SphereOfTheSunsFactory.Create(_alice);

        sphere.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB \"enters with three charge counters\" trigger");
    }

    [Fact]
    public void SphereOfTheSuns_Etb_PlacesThreeChargeCounters()
    {
        var sphere = SphereOfTheSunsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sphere);
        sphere.SetZone(ZoneType.Battlefield);

        sphere.Counters.Count(CounterType.Charge).Should().Be(0,
            "no charge counters before the ETB resolves");

        var etb = sphere.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        sphere.Counters.Count(CounterType.Charge).Should().Be(3,
            "enters with three charge counters on it (CR 122 / CR 614.1d)");
    }

    // -----------------------------------------------------------------------
    // Mana abilities — "{T}, Remove a charge counter: Add one mana of any color"
    // -----------------------------------------------------------------------

    [Fact]
    public void SphereOfTheSuns_HasFiveManaAbilities_OnePerColor()
    {
        var sphere = SphereOfTheSunsFactory.Create(_alice);
        var mas = sphere.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(5, "one ManaAbility per WUBRG colour");

        mas.Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.TotalValue == 1);
    }

    [Fact]
    public void SphereOfTheSuns_Activate_RemovesChargeCounter_ProducesColor_AndTaps()
    {
        var sphere = SphereOfTheSunsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sphere);
        sphere.SetZone(ZoneType.Battlefield);
        sphere.Counters.Add(CounterType.Charge, 3);

        var green = sphere.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Green == 1);

        green.CanActivate().Should().BeTrue(
            "untapped sphere with charge counters can pay {T} + remove a charge counter");

        var produced = green.Activate();

        produced.Green.Should().Be(1);
        produced.TotalValue.Should().Be(1);

        sphere.Counters.Count(CounterType.Charge).Should().Be(2,
            "activating the mana ability removes one charge counter");
        sphere.IsTapped.Should().BeTrue(
            "CR 605 — the activation cost includes {T}; the sphere taps");
    }

    [Fact]
    public void SphereOfTheSuns_CannotActivate_WhenTapped()
    {
        var sphere = SphereOfTheSunsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sphere);
        sphere.SetZone(ZoneType.Battlefield);
        sphere.Counters.Add(CounterType.Charge, 3);

        var red = sphere.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Red == 1);
        red.Activate();
        sphere.IsTapped.Should().BeTrue();

        // CR 605.3a — a tapped permanent can't pay the printed {T} cost, so
        // no colour slot is activatable until it untaps, even though two
        // charge counters remain.
        sphere.Counters.Count(CounterType.Charge).Should().Be(2);
        foreach (var ma in sphere.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse(
                "a tapped sphere can't pay {T} regardless of remaining charge counters");
        }
    }

    [Fact]
    public void SphereOfTheSuns_NoChargeCounters_CannotActivate()
    {
        var sphere = SphereOfTheSunsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sphere);
        sphere.SetZone(ZoneType.Battlefield);
        // Untapped but no charge counters → the "remove a charge counter"
        // half of the cost can't be paid (CR 605.3a).

        sphere.IsTapped.Should().BeFalse();
        foreach (var ma in sphere.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse(
                "no charge counter to remove → cost cannot be paid");
        }
    }
}
