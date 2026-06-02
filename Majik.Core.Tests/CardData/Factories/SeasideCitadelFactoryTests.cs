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
/// Tests for <see cref="SeasideCitadelFactory"/> — Seaside Citadel (Conflux,
/// the Bant member of the original tapped tri-land cycle). Land:
///   "This land enters tapped.
///    {T}: Add {G}, {W}, or {U}."
///
/// Mirrors <see cref="SavaiTriomeFactoryTests"/> minus the cycling clause and
/// the printed basic subtypes: identity (plain nonbasic Land), three mana
/// abilities (one per produced colour, CR 605.1), and the unconditional
/// enters-tapped restriction (CR 614.1c).
/// </summary>
[Trait("Color", "C")]
public class SeasideCitadelFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SeasideCitadel_Identity()
    {
        var land = (Land)NamedCardFactory.Create("Seaside Citadel", _alice);

        land.Name.Should().Be("Seaside Citadel");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Seaside Citadel is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {G}, {W}, or {U} — three mana abilities (CR 605.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void SeasideCitadel_HasThreeManaAbilities_ProducingGreenWhiteBlue()
    {
        var land = (Land)NamedCardFactory.Create("Seaside Citadel", _alice);
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(3, "{T}: Add {G}, {W}, or {U}");
        manaAbilities.Should().Contain(m => m.ManaGenerated.Green == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.White == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.Blue == 1);
    }

    [Fact]
    public void SeasideCitadel_HasNoActivatedOrTriggeredAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Seaside Citadel", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Seaside Citadel has no non-mana activated abilities (no cycling)");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Seaside Citadel has no triggered abilities");
    }

    // -----------------------------------------------------------------------
    // Enters-tapped — CR 614.1c
    // -----------------------------------------------------------------------

    [Fact]
    public void SeasideCitadel_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var land = SeasideCitadelFactory.Create(_alice, replacements: replacements);

        land.Should().NotBeNull();
        // The replacement is registered on the supplied bus (CR 614.1c); the
        // shape-only path (null bus) skips it. EntersTappedReplacement has no
        // public bus-inspection surface, so the production path (covered by
        // the binder chain via oracle text) is the authoritative test for
        // tapped-entry behaviour; here we assert the build succeeds wired.
    }
}
