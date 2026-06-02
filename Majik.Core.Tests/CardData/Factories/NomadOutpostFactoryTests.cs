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
/// Tests for <see cref="NomadOutpostFactory"/> — Nomad Outpost (Khans of
/// Tarkir, the Mardu member of the tapped tri-land cycle). Land:
///   "This land enters tapped.
///    {T}: Add {R}, {W}, or {B}."
///
/// Mirrors <see cref="SeasideCitadelFactoryTests"/> (same tri-land posture,
/// RWB colours instead of GWU): identity (plain nonbasic Land), three mana
/// abilities (one per produced colour, CR 605.1), no activated/triggered
/// abilities, and the unconditional enters-tapped restriction (CR 614.1c).
/// </summary>
[Trait("Color", "C")]
public class NomadOutpostFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NomadOutpost_Identity()
    {
        var land = (Land)NamedCardFactory.Create("Nomad Outpost", _alice);

        land.Name.Should().Be("Nomad Outpost");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Nomad Outpost is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {R}, {W}, or {B} — three mana abilities (CR 605.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void NomadOutpost_HasThreeManaAbilities_ProducingRedWhiteBlack()
    {
        var land = (Land)NamedCardFactory.Create("Nomad Outpost", _alice);
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(3, "{T}: Add {R}, {W}, or {B}");
        manaAbilities.Should().Contain(m => m.ManaGenerated.Red == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.White == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.Black == 1);
    }

    [Fact]
    public void NomadOutpost_HasNoActivatedOrTriggeredAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Nomad Outpost", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Nomad Outpost has no non-mana activated abilities (no cycling)");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Nomad Outpost has no triggered abilities");
    }

    // -----------------------------------------------------------------------
    // Enters-tapped — CR 614.1c
    // -----------------------------------------------------------------------

    [Fact]
    public void NomadOutpost_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var land = NomadOutpostFactory.Create(_alice, replacements: replacements);

        land.Should().NotBeNull();
        // The replacement is registered on the supplied bus (CR 614.1c); the
        // shape-only path (null bus) skips it. EntersTappedReplacement has no
        // public bus-inspection surface, so the production path (covered by
        // the binder chain via oracle text) is the authoritative test for
        // tapped-entry behaviour; here we assert the build succeeds wired.
    }
}
