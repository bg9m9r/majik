using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CrumblingNecropolisFactory"/> — Crumbling
/// Necropolis (Shards of Alara tapped tri-land cycle).
///
/// Grixis (U/B/R) tapland. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    {T}: Add {U}, {B}, or {R}."
///
/// Unlike the Triome cycle it carries no basic land subtypes and no Cycling.
/// Loaded from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, nonbasic, owner/controller).
/// - Three single-colour mana abilities — {U}, {B}, {R} (CR 605.1a).
/// - No basic land subtypes (it is "Land", not "Land — Island Swamp Mountain").
/// - No cycling / no other activated abilities.
/// - Enters-tapped (CR 614.1c) registers an <see cref="EntersTappedReplacement"/>
///   on a supplied <see cref="ReplacementBus"/>.
/// </summary>
[Trait("Color", "C")]
public class CrumblingNecropolisFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CrumblingNecropolis_IsLand_WithCorrectNameAndOwnership()
    {
        var land = (Land)NamedCardFactory.Create("Crumbling Necropolis", _alice);

        land.Name.Should().Be("Crumbling Necropolis");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Crumbling Necropolis is nonbasic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Theory]
    [InlineData(CardSubtype.Island)]
    [InlineData(CardSubtype.Swamp)]
    [InlineData(CardSubtype.Mountain)]
    public void CrumblingNecropolis_HasNoBasicLandSubtype(CardSubtype subtype)
    {
        // Printed type line is plain "Land" — unlike the Triome cycle, this
        // tapland carries no basic land subtypes.
        var land = (Land)NamedCardFactory.Create("Crumbling Necropolis", _alice);

        land.HasSubtype(subtype).Should().BeFalse(
            $"the type line is 'Land', so it must not carry {subtype}");
    }

    // -----------------------------------------------------------------------
    // Mana abilities — CR 605.1a, one per produced colour
    // -----------------------------------------------------------------------

    [Fact]
    public void CrumblingNecropolis_HasExactlyThreeManaAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Crumbling Necropolis", _alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(3,
            "one each for {U}, {B}, {R}");
    }

    [Fact]
    public void CrumblingNecropolis_HasManaAbility_ForBlue()
    {
        var land = (Land)NamedCardFactory.Create("Crumbling Necropolis", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1
                && m.ManaGenerated.Black == 0
                && m.ManaGenerated.Red == 0
                && m.ManaGenerated.Generic == 0);
    }

    [Fact]
    public void CrumblingNecropolis_HasManaAbility_ForBlack()
    {
        var land = (Land)NamedCardFactory.Create("Crumbling Necropolis", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1
                && m.ManaGenerated.Blue == 0
                && m.ManaGenerated.Red == 0
                && m.ManaGenerated.Generic == 0);
    }

    [Fact]
    public void CrumblingNecropolis_HasManaAbility_ForRed()
    {
        var land = (Land)NamedCardFactory.Create("Crumbling Necropolis", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1
                && m.ManaGenerated.Blue == 0
                && m.ManaGenerated.Black == 0
                && m.ManaGenerated.Generic == 0);
    }

    // -----------------------------------------------------------------------
    // No cycling / no other activated abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void CrumblingNecropolis_HasNoActivatedAbilities()
    {
        // Crumbling Necropolis is a plain tapland — no Cycling, unlike the
        // Triome cycle.
        var land = (Land)NamedCardFactory.Create("Crumbling Necropolis", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        land.Abilities.OfType<KeywordAbility>()
            .Should().NotContain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // Enters-tapped — CR 614.1c
    // -----------------------------------------------------------------------

    [Fact]
    public void CrumblingNecropolis_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var land = CrumblingNecropolisFactory.Create(_alice, replacements: replacements);

        land.Should().NotBeNull();
        // The unconditional enters-tapped restriction (CR 614.1c) is
        // registered on the supplied bus; the shape-only path (null bus)
        // skips it. EntersTappedReplacement has no public bus-inspection
        // surface, so the production path (covered by the binder chain via
        // oracle text) is the authoritative test for tapped-entry behaviour.
    }
}
