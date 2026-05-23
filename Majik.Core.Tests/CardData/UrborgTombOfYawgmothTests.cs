using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Urborg, Tomb of Yawgmoth — Legendary Land,
/// "Each land is a Swamp in addition to its other types." (CR 305.7).
///
/// Validates the additive-grant stack:
///   PR #150 — permanent-level layer system.
///   PR #155 — <see cref="EffectiveManaAbilities"/> additive-vs-replacement
///             detection (extended in this PR).
///   PR (this one) — <see cref="AddSubtypeToPermanentsEffect"/>,
///             <see cref="GrantLandSubtypeStaticEffect"/>, and the
///             <see cref="UrborgTombOfYawgmothFactory"/> wiring.
/// </summary>
public class UrborgTombOfYawgmothTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public UrborgTombOfYawgmothTests()
    {
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Urborg_IsLegendaryLand_NoPrintedMana()
    {
        var urborg = UrborgTombOfYawgmothFactory.Create(_alice);

        urborg.Name.Should().Be("Urborg, Tomb of Yawgmoth");
        urborg.HasType(CardType.Land).Should().BeTrue();
        urborg.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        urborg.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        urborg.Abilities.OfType<IManaAbility>().Should().BeEmpty(
            "Urborg has no printed mana ability — tap-for-B comes from the Layer 4 grant");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Urborg()
    {
        var urborg = NamedCardFactory.Create("Urborg, Tomb of Yawgmoth", _alice);

        urborg.Should().BeOfType<Land>();
        urborg.Name.Should().Be("Urborg, Tomb of Yawgmoth");
    }

    // -----------------------------------------------------------------------
    // End-to-end: Urborg grants Swamp; each land taps for printed mana + {B}
    // -----------------------------------------------------------------------

    /// <summary>
    /// With Urborg on the battlefield, a basic Mountain should have BOTH
    /// Mountain and Swamp in its effective subtype set (CR 305.7).
    /// </summary>
    [Fact]
    public void Urborg_OnBattlefield_GrantsSwampToEveryLand()
    {
        var mountain = (Land)NamedCardFactory.Create("Mountain", _alice);
        _zones.MoveCard(mountain, ZoneType.Library, ZoneType.Battlefield, _alice);

        var urborg = UrborgTombOfYawgmothFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(urborg, ZoneType.Library, ZoneType.Battlefield, _alice);

        var effective = _effects.Compute(mountain).Subtypes;

        effective.Should().Contain(CardSubtype.Mountain, "printed subtype is preserved (additive)");
        effective.Should().Contain(CardSubtype.Swamp, "Urborg grants Swamp to every land");
    }

    /// <summary>
    /// Additive-vs-replacement fix in action: a basic Mountain with
    /// Urborg out should expose BOTH the printed {T}: Add {R} AND a
    /// synthesized {T}: Add {B} via the granted Swamp subtype.
    /// </summary>
    [Fact]
    public void Urborg_PreservesPrintedManaAbility_AddsBlackMana()
    {
        var mountain = (Land)NamedCardFactory.Create("Mountain", _alice);
        _zones.MoveCard(mountain, ZoneType.Library, ZoneType.Battlefield, _alice);

        var urborg = UrborgTombOfYawgmothFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(urborg, ZoneType.Library, ZoneType.Battlefield, _alice);

        var abilities = EffectiveManaAbilities.For(mountain, _effects, _alice);

        abilities.Should().HaveCount(2, "CR 305.7 — printed {R} preserved, {B} added for granted Swamp");
        abilities.Should().Contain(a => a.ManaGenerated.Red == 1, "printed Mountain mana ability");
        abilities.Should().Contain(a => a.ManaGenerated.Black == 1, "granted Swamp mana ability");
    }

    /// <summary>
    /// Self-application: Urborg has no printed mana, but its own Layer 4
    /// effect grants itself the Swamp subtype, so
    /// <see cref="EffectiveManaAbilities"/> sees a newly-acquired basic
    /// land subtype and returns a {T}: Add {B}.
    /// </summary>
    [Fact]
    public void Urborg_SelfTapsForBlack()
    {
        var urborg = UrborgTombOfYawgmothFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(urborg, ZoneType.Library, ZoneType.Battlefield, _alice);

        var abilities = EffectiveManaAbilities.For(urborg, _effects, _alice);

        abilities.Should().ContainSingle("Urborg self-applies Swamp → exactly one synthesized {B} ability");
        abilities[0].ManaGenerated.Black.Should().Be(1);
    }
}
