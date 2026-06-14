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
/// Unit tests for <see cref="VividGroveFactory"/> (Lorwyn "Vivid" land cycle).
///
/// Vivid Grove — Land.
///   "This land enters tapped with two charge counters on it.
///    {T}: Add {G}.
///    {T}, Remove a charge counter from this land: Add one mana of any
///    color."
///
/// Mechanically identical to <see cref="VividCragFactory"/> except the base
/// colour is {G} rather than {R}. Covers:
/// - Identity (Land, nonbasic).
/// - ETB trigger places two charge counters (CR 122 / CR 614.1d).
/// - Six mana abilities: one base {G} (no counter cost) + five WUBRG
///   any-colour (each removes a charge counter).
/// - The base {G} ability activates with NO charge counters and removes none.
/// - Activating an any-colour ability removes one charge counter, produces
///   the chosen colour, AND taps the land (CR 605 — the cost includes {T}).
/// - Once tapped, no further mana ability can be activated.
/// - The any-colour abilities are un-activatable when no charge counters
///   remain; the base {G} ability still is (it has no counter cost).
/// </summary>
[Trait("Color", "G")]
public class VividGroveTests
{
    private readonly Player _alice = new("Alice", 20);

    /// <summary>
    /// The base "{T}: Add {G}" ability — the {G} producer with NO charge-counter
    /// cost. Identified behaviourally: with zero charge counters on an untapped
    /// land it is still activatable, whereas the any-colour {G} ability is not.
    /// </summary>
    private static ManaAbility BaseGreen(Land land)
    {
        EnsureZeroCountersOnBattlefield(land);
        return land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Green == 1 && m.CanActivate());
    }

    /// <summary>
    /// The any-colour {G} ability — the {G} producer that removes a charge
    /// counter. Identified behaviourally: with zero charge counters it is the
    /// green ability that CANNOT activate (its remove-a-charge-counter cost is
    /// unpayable), whereas the base {G} ability can.
    /// </summary>
    private static ManaAbility AnyColorGreen(Land land)
    {
        EnsureZeroCountersOnBattlefield(land);
        return land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Green == 1 && !m.CanActivate());
    }

    /// <summary>
    /// Guard the behavioural identification helpers: they only discriminate the
    /// two {G} abilities when the land is untapped, on the battlefield, with no
    /// charge counters. Throws otherwise so a mis-set-up test fails loudly.
    /// </summary>
    private static void EnsureZeroCountersOnBattlefield(Land land)
    {
        if (land.Zone != ZoneType.Battlefield)
            throw new InvalidOperationException("helper requires the land on the battlefield");
        if (land.IsTapped)
            throw new InvalidOperationException("helper requires an untapped land");
        if (land.Counters.Count(CounterType.Charge) != 0)
            throw new InvalidOperationException("helper requires zero charge counters");
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void VividGrove_Identity()
    {
        var land = VividGroveFactory.Create(_alice);

        land.Name.Should().Be("Vivid Grove");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Vivid Grove is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // ETB — "enters ... with two charge counters on it"
    // -----------------------------------------------------------------------

    [Fact]
    public void VividGrove_HasExactlyOneEtbTrigger()
    {
        var land = VividGroveFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB \"enters with two charge counters\" trigger");
    }

    [Fact]
    public void VividGrove_Etb_PlacesTwoChargeCounters()
    {
        var land = VividGroveFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        land.Counters.Count(CounterType.Charge).Should().Be(0,
            "no charge counters before the ETB resolves");

        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        land.Counters.Count(CounterType.Charge).Should().Be(2,
            "enters with two charge counters on it (CR 122 / CR 614.1d)");
    }

    // -----------------------------------------------------------------------
    // Mana abilities — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void VividGrove_HasSixManaAbilities_BasePlusFiveAnyColor()
    {
        var land = VividGroveFactory.Create(_alice);
        var mas = land.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(6, "one base {G} ability + five WUBRG any-colour abilities");

        // All five colours are produced (the any-colour suite), and {G} appears
        // twice (the base ability plus the any-colour green slot).
        mas.Count(m => m.ManaGenerated.White == 1 && m.ManaGenerated.TotalValue == 1).Should().Be(1, "{W}");
        mas.Count(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.TotalValue == 1).Should().Be(1, "{U}");
        mas.Count(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.TotalValue == 1).Should().Be(1, "{B}");
        mas.Count(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.TotalValue == 1).Should().Be(1, "{R}");
        mas.Count(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.TotalValue == 1).Should().Be(2,
            "{G} is produced by both the base ability and the any-colour green slot");

        // Behavioural split: on the battlefield with no charge counters, exactly
        // one ability (the base {G}) is activatable; the other five (the
        // charge-gated any-colour suite) are not.
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        mas.Count(m => m.CanActivate()).Should().Be(1,
            "with no charge counters only the base {G} ability (no counter cost) is activatable");
        mas.Count(m => !m.CanActivate()).Should().Be(5,
            "the five charge-gated any-colour abilities cannot pay their remove-a-charge-counter cost");
    }

    // -----------------------------------------------------------------------
    // Base {T}: Add {G}
    // -----------------------------------------------------------------------

    [Fact]
    public void VividGrove_BaseGreen_ActivatesWithoutCounters_RemovesNone()
    {
        var land = VividGroveFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        // No charge counters present.

        var baseGreen = BaseGreen(land);
        baseGreen.CanActivate().Should().BeTrue(
            "the base {T}: Add {G} ability needs no charge counter");

        var produced = baseGreen.Activate();
        produced.Green.Should().Be(1);
        produced.TotalValue.Should().Be(1);

        land.Counters.Count(CounterType.Charge).Should().Be(0,
            "the base {G} ability removes no charge counter");
        land.IsTapped.Should().BeTrue("CR 605 — the activation cost includes {T}");
    }

    // -----------------------------------------------------------------------
    // {T}, Remove a charge counter: Add one mana of any color
    // -----------------------------------------------------------------------

    [Fact]
    public void VividGrove_AnyColor_Activate_RemovesChargeCounter_ProducesColor_AndTaps()
    {
        var land = VividGroveFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.Counters.Add(CounterType.Charge, 2);

        var anyBlue = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Blue == 1);

        anyBlue.CanActivate().Should().BeTrue(
            "untapped land with charge counters can pay {T} + remove a charge counter");

        var produced = anyBlue.Activate();
        produced.Blue.Should().Be(1);
        produced.TotalValue.Should().Be(1);

        land.Counters.Count(CounterType.Charge).Should().Be(1,
            "activating the any-colour ability removes one charge counter");
        land.IsTapped.Should().BeTrue(
            "CR 605 — the activation cost includes {T}; the land taps");
    }

    [Fact]
    public void VividGrove_CannotActivate_WhenTapped()
    {
        var land = VividGroveFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Resolve the base {G} ability before adding counters (the helper
        // discriminates only at zero charge counters), then add counters and
        // tap by activating it.
        var baseGreen = BaseGreen(land);
        land.Counters.Add(CounterType.Charge, 2);

        baseGreen.Activate();
        land.IsTapped.Should().BeTrue();

        // CR 605.3a — a tapped permanent can't pay the printed {T} cost, so
        // no mana ability is activatable until it untaps.
        foreach (var ma in land.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse(
                "a tapped land can't pay {T} regardless of remaining charge counters");
        }
    }

    [Fact]
    public void VividGrove_NoChargeCounters_OnlyBaseGreenActivatable()
    {
        var land = VividGroveFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        // Untapped, no charge counters.

        land.IsTapped.Should().BeFalse();

        AnyColorGreen(land).CanActivate().Should().BeFalse(
            "no charge counter to remove → the any-colour green cost cannot be paid");

        // Exactly one mana ability — the base {G} — is activatable; the five
        // any-colour abilities all need a charge counter to remove.
        land.Abilities.OfType<ManaAbility>().Count(m => m.CanActivate()).Should().Be(1,
            "only the base {T}: Add {G} ability (no charge-counter cost) is activatable");

        BaseGreen(land).CanActivate().Should().BeTrue(
            "the base {T}: Add {G} ability has no charge-counter cost");
    }
}
