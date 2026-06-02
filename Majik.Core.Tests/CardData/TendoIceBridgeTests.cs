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
/// Unit tests for <see cref="TendoIceBridgeFactory"/> (Champions of
/// Kamigawa).
///
/// Tendo Ice Bridge — Land.
///   "This land enters with a charge counter on it.
///    {T}: Add {C}.
///    {T}, Remove a charge counter from this land: Add one mana of any
///    color."
///
/// Covers:
/// - Identity (Land, no mana cost) + <see cref="NamedCardFactory"/> dispatch.
/// - ETB trigger places ONE charge counter (CR 122 / CR 614.1d).
/// - The unconditional {T}: Add {C} mana ability (always available untapped,
///   no charge-counter cost).
/// - Five colour mana abilities (one per WUBRG) — "Add one mana of any
///   color" — each gated on a charge counter being present.
/// - Activating a colour ability removes one charge counter, produces the
///   chosen colour, AND taps the land (CR 605 — the cost includes {T}).
/// - With no charge counters, only the {T}: Add {C} ability remains
///   activatable; the colour abilities cannot be paid.
/// </summary>
public class TendoIceBridgeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void TendoIceBridge_IsLand_NoManaCost()
    {
        var land = TendoIceBridgeFactory.Create(_alice);

        land.Name.Should().Be("Tendo Ice Bridge");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TendoIceBridge_IsNotLegendary()
    {
        var land = TendoIceBridgeFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TendoIceBridge()
    {
        var card = NamedCardFactory.Create("Tendo Ice Bridge", _alice);

        card.Should().BeOfType<Land>();
        card!.Name.Should().Be("Tendo Ice Bridge");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB \"enters with a charge counter\" trigger surfaced for shape");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(6,
            "one colourless {C} ability plus one colour ability per WUBRG");
    }

    // -----------------------------------------------------------------------
    // ETB — "enters with a charge counter on it"
    // -----------------------------------------------------------------------

    [Fact]
    public void TendoIceBridge_HasExactlyOneEtbTrigger()
    {
        var land = TendoIceBridgeFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB \"enters with a charge counter\" trigger");
    }

    [Fact]
    public void TendoIceBridge_Etb_PlacesOneChargeCounter()
    {
        var land = TendoIceBridgeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        land.Counters.Count(CounterType.Charge).Should().Be(0,
            "no charge counters before the ETB resolves");

        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        land.Counters.Count(CounterType.Charge).Should().Be(1,
            "enters with a charge counter on it (CR 122 / CR 614.1d)");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void TendoIceBridge_HasColorlessManaAbility()
    {
        var land = TendoIceBridgeFactory.Create(_alice);

        var colorless = land.Abilities.OfType<ManaAbility>()
            .Where(m => m.ManaGenerated.TotalValue == 1
                        && m.ManaGenerated.White == 0
                        && m.ManaGenerated.Blue == 0
                        && m.ManaGenerated.Black == 0
                        && m.ManaGenerated.Red == 0
                        && m.ManaGenerated.Green == 0)
            .ToList();

        colorless.Should().HaveCount(1, "exactly one {T}: Add {C} ability");
    }

    [Fact]
    public void TendoIceBridge_Colorless_ActivatableWithNoCounters_DoesNotRemoveCounters()
    {
        var land = TendoIceBridgeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        // No charge counters — the {C} ability does not need one.

        var colorless = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.TotalValue == 1
                         && m.ManaGenerated.White == 0
                         && m.ManaGenerated.Blue == 0
                         && m.ManaGenerated.Black == 0
                         && m.ManaGenerated.Red == 0
                         && m.ManaGenerated.Green == 0);

        colorless.CanActivate().Should().BeTrue(
            "{T}: Add {C} has no charge-counter cost");

        var produced = colorless.Activate();
        produced.TotalValue.Should().Be(1);
        produced.Generic.Should().Be(1,
            "{C} folds into the generic bucket (no dedicated colourless channel)");

        land.Counters.Count(CounterType.Charge).Should().Be(0,
            "the colourless ability does not remove a charge counter");
        land.IsTapped.Should().BeTrue("CR 605 — the {T} cost taps the land");
    }

    // -----------------------------------------------------------------------
    // {T}, Remove a charge counter: Add one mana of any color
    // -----------------------------------------------------------------------

    [Fact]
    public void TendoIceBridge_HasFiveColorManaAbilities_OnePerColor()
    {
        var land = TendoIceBridgeFactory.Create(_alice);
        var mas = land.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.TotalValue == 1);
    }

    [Fact]
    public void TendoIceBridge_ColorAbility_RemovesChargeCounter_ProducesColor_AndTaps()
    {
        var land = TendoIceBridgeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.Counters.Add(CounterType.Charge, 1);

        var green = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Green == 1);

        green.CanActivate().Should().BeTrue(
            "untapped land with a charge counter can pay {T} + remove a charge counter");

        var produced = green.Activate();

        produced.Green.Should().Be(1);
        produced.TotalValue.Should().Be(1);

        land.Counters.Count(CounterType.Charge).Should().Be(0,
            "activating the colour ability removes the charge counter");
        land.IsTapped.Should().BeTrue(
            "CR 605 — the activation cost includes {T}; the land taps");
    }

    [Fact]
    public void TendoIceBridge_ColorAbility_NoChargeCounters_CannotActivate()
    {
        var land = TendoIceBridgeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        // Untapped but no charge counters → the "remove a charge counter"
        // half of the cost can't be paid (CR 605.3a).

        land.IsTapped.Should().BeFalse();
        foreach (var ma in land.Abilities.OfType<ManaAbility>()
                     .Where(m => m.ManaGenerated.TotalValue == 1
                                 && (m.ManaGenerated.White == 1
                                     || m.ManaGenerated.Blue == 1
                                     || m.ManaGenerated.Black == 1
                                     || m.ManaGenerated.Red == 1
                                     || m.ManaGenerated.Green == 1)))
        {
            ma.CanActivate().Should().BeFalse(
                "no charge counter to remove → the colour ability's cost cannot be paid");
        }
    }

    [Fact]
    public void TendoIceBridge_ColorAbility_CannotActivate_WhenTapped()
    {
        var land = TendoIceBridgeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.Counters.Add(CounterType.Charge, 1);

        land.Tap();
        land.IsTapped.Should().BeTrue();

        // CR 605.3a — a tapped land can't pay the printed {T} cost.
        foreach (var ma in land.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse(
                "a tapped land can't pay {T} for any of its mana abilities");
        }
    }
}
