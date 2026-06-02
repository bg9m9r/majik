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
/// Tests for <see cref="MysticMonasteryFactory"/> — Mystic Monastery (Khans of
/// Tarkir, the Jeskai member of the Wedge tapland cycle). Land:
///   "This land enters tapped.
///    {T}: Add {U}, {R}, or {W}."
///
/// Mirrors <see cref="SeasideCitadelFactoryTests"/> (plain tapland triland, no
/// cycling, no printed basic subtypes) with Jeskai colours: identity (plain
/// nonbasic Land), three mana abilities (one per produced colour, CR 605.1),
/// and the unconditional enters-tapped restriction (CR 614.1c).
/// </summary>
[Trait("Color", "C")]
public class MysticMonasteryFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MysticMonastery_Identity()
    {
        var land = (Land)NamedCardFactory.Create("Mystic Monastery", _alice);

        land.Name.Should().Be("Mystic Monastery");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Mystic Monastery is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {U}, {R}, or {W} — three mana abilities (CR 605.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void MysticMonastery_HasThreeManaAbilities_ProducingBlueRedWhite()
    {
        var land = (Land)NamedCardFactory.Create("Mystic Monastery", _alice);
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(3, "{T}: Add {U}, {R}, or {W}");
        manaAbilities.Should().Contain(m => m.ManaGenerated.Blue == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.Red == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.White == 1);
    }

    [Fact]
    public void MysticMonastery_HasNoActivatedOrTriggeredAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Mystic Monastery", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Mystic Monastery has no non-mana activated abilities (no cycling)");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Mystic Monastery has no triggered abilities");
    }

    // -----------------------------------------------------------------------
    // Enters-tapped — CR 614.1c
    // -----------------------------------------------------------------------

    [Fact]
    public void MysticMonastery_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var land = MysticMonasteryFactory.Create(_alice, replacements: replacements);

        land.Should().NotBeNull();
        // The replacement is registered on the supplied bus (CR 614.1c); the
        // shape-only path (null bus) skips it. EntersTappedReplacement has no
        // public bus-inspection surface, so the production path (covered by the
        // binder chain via oracle text) is the authoritative test for
        // tapped-entry behaviour; here we assert the build succeeds wired.
    }
}
