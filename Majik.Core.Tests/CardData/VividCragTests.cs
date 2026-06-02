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
/// Unit tests for <see cref="VividCragFactory"/> (Lorwyn "Vivid" land cycle).
///
/// Vivid Crag — Land.
///   "This land enters tapped with two charge counters on it.
///    {T}: Add {R}.
///    {T}, Remove a charge counter from this land: Add one mana of any
///    color."
///
/// Covers:
/// - Identity (Land, nonbasic) + <see cref="NamedCardFactory"/> dispatch.
/// - ETB trigger places two charge counters (CR 122 / CR 614.1d).
/// - Six mana abilities: one base {R} (no counter cost) + five WUBRG
///   any-colour (each removes a charge counter).
/// - The base {R} ability activates with NO charge counters and removes none.
/// - Activating an any-colour ability removes one charge counter, produces
///   the chosen colour, AND taps the land (CR 605 — the cost includes {T}).
/// - Once tapped, no further mana ability can be activated.
/// - The any-colour abilities are un-activatable when no charge counters
///   remain; the base {R} ability still is (it has no counter cost).
/// </summary>
public class VividCragTests
{
    private readonly Player _alice = new("Alice", 20);

    /// <summary>
    /// The base "{T}: Add {R}" ability — the {R} producer with NO charge-counter
    /// cost. Identified behaviourally: with zero charge counters on an untapped
    /// land it is still activatable, whereas the any-colour {R} ability is not.
    /// </summary>
    private static ManaAbility BaseRed(Land land)
    {
        EnsureZeroCountersOnBattlefield(land);
        return land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Red == 1 && m.CanActivate());
    }

    /// <summary>
    /// The any-colour {R} ability — the {R} producer that removes a charge
    /// counter. Identified behaviourally: with zero charge counters it is the
    /// red ability that CANNOT activate (its remove-a-charge-counter cost is
    /// unpayable), whereas the base {R} ability can.
    /// </summary>
    private static ManaAbility AnyColorRed(Land land)
    {
        EnsureZeroCountersOnBattlefield(land);
        return land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Red == 1 && !m.CanActivate());
    }

    /// <summary>
    /// Guard the behavioural identification helpers: they only discriminate the
    /// two {R} abilities when the land is untapped, on the battlefield, with no
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
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void VividCrag_Identity()
    {
        var land = VividCragFactory.Create(_alice);

        land.Name.Should().Be("Vivid Crag");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Vivid Crag is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_VividCrag()
    {
        var card = NamedCardFactory.Create("Vivid Crag", _alice);

        card.Should().BeOfType<Land>();
        card!.Name.Should().Be("Vivid Crag");
        card.HasType(CardType.Land).Should().BeTrue();

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB \"enters with two charge counters\" trigger surfaced for shape");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(6,
            "one base {R} ability plus one any-colour ability per WUBRG");
    }

    // -----------------------------------------------------------------------
    // ETB — "enters ... with two charge counters on it"
    // -----------------------------------------------------------------------

    [Fact]
    public void VividCrag_HasExactlyOneEtbTrigger()
    {
        var land = VividCragFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB \"enters with two charge counters\" trigger");
    }

    [Fact]
    public void VividCrag_Etb_PlacesTwoChargeCounters()
    {
        var land = VividCragFactory.Create(_alice);
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
    public void VividCrag_HasSixManaAbilities_BasePlusFiveAnyColor()
    {
        var land = VividCragFactory.Create(_alice);
        var mas = land.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(6, "one base {R} ability + five WUBRG any-colour abilities");

        // All five colours are produced (the any-colour suite), and {R} appears
        // twice (the base ability plus the any-colour red slot).
        mas.Count(m => m.ManaGenerated.White == 1 && m.ManaGenerated.TotalValue == 1).Should().Be(1, "{W}");
        mas.Count(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.TotalValue == 1).Should().Be(1, "{U}");
        mas.Count(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.TotalValue == 1).Should().Be(1, "{B}");
        mas.Count(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.TotalValue == 1).Should().Be(1, "{G}");
        mas.Count(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.TotalValue == 1).Should().Be(2,
            "{R} is produced by both the base ability and the any-colour red slot");

        // Behavioural split: on the battlefield with no charge counters, exactly
        // one ability (the base {R}) is activatable; the other five (the
        // charge-gated any-colour suite) are not.
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        mas.Count(m => m.CanActivate()).Should().Be(1,
            "with no charge counters only the base {R} ability (no counter cost) is activatable");
        mas.Count(m => !m.CanActivate()).Should().Be(5,
            "the five charge-gated any-colour abilities cannot pay their remove-a-charge-counter cost");
    }

    // -----------------------------------------------------------------------
    // Base {T}: Add {R}
    // -----------------------------------------------------------------------

    [Fact]
    public void VividCrag_BaseRed_ActivatesWithoutCounters_RemovesNone()
    {
        var land = VividCragFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        // No charge counters present.

        var baseRed = BaseRed(land);
        baseRed.CanActivate().Should().BeTrue(
            "the base {T}: Add {R} ability needs no charge counter");

        var produced = baseRed.Activate();
        produced.Red.Should().Be(1);
        produced.TotalValue.Should().Be(1);

        land.Counters.Count(CounterType.Charge).Should().Be(0,
            "the base {R} ability removes no charge counter");
        land.IsTapped.Should().BeTrue("CR 605 — the activation cost includes {T}");
    }

    // -----------------------------------------------------------------------
    // {T}, Remove a charge counter: Add one mana of any color
    // -----------------------------------------------------------------------

    [Fact]
    public void VividCrag_AnyColor_Activate_RemovesChargeCounter_ProducesColor_AndTaps()
    {
        var land = VividCragFactory.Create(_alice);
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
    public void VividCrag_CannotActivate_WhenTapped()
    {
        var land = VividCragFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Resolve the base {R} ability before adding counters (the helper
        // discriminates only at zero charge counters), then add counters and
        // tap by activating it.
        var baseRed = BaseRed(land);
        land.Counters.Add(CounterType.Charge, 2);

        baseRed.Activate();
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
    public void VividCrag_NoChargeCounters_OnlyBaseRedActivatable()
    {
        var land = VividCragFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        // Untapped, no charge counters.

        land.IsTapped.Should().BeFalse();

        AnyColorRed(land).CanActivate().Should().BeFalse(
            "no charge counter to remove → the any-colour red cost cannot be paid");

        // Exactly one mana ability — the base {R} — is activatable; the five
        // any-colour abilities all need a charge counter to remove.
        land.Abilities.OfType<ManaAbility>().Count(m => m.CanActivate()).Should().Be(1,
            "only the base {T}: Add {R} ability (no charge-counter cost) is activatable");

        BaseRed(land).CanActivate().Should().BeTrue(
            "the base {T}: Add {R} ability has no charge-counter cost");
    }
}
