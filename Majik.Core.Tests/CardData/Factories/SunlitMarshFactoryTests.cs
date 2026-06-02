using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for Sunlit Marsh — the W/B "slow land" dual. Type line:
/// <c>Land — Plains Swamp</c>. Oracle text (verified against Scryfall
/// 2026-06-02):
///   "({T}: Add {W} or {B}.)
///    This land enters tapped."
///
/// Same plain enters-tapped two-colour dual shape as the Kaldheim snow dual
/// <see cref="SnowfieldSinkholeFactoryTests"/>, minus the Snow supertype — the
/// {W}/{B} mana comes from the printed Plains and Swamp land subtypes
/// (CR 305.6 / CR 205.3i). It is a fileless JSON card: the
/// <see cref="NamedCardFactory"/> dispatch arm is emitted by the source
/// generator from <c>Majik.Core/CardData/Cards/sunlit-marsh.json</c>, so there
/// is no hand-written factory class.
///
/// Covers:
/// - Identity (Land + the printed Plains and Swamp subtypes, CR 205.3i;
///   nonbasic, CR 205.4d).
/// - Two mana abilities producing {W} and {B} respectively (CR 605.1 — mana
///   abilities don't use the stack).
/// - No non-mana activated abilities (a plain dual has none).
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by the
/// JSON shell (same posture as Snowfield Sinkhole and the Guildgate cards).
/// </summary>
[Trait("Color", "C")]
public class SunlitMarshFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SunlitMarsh_IsLand_WithPlainsAndSwampSubtypes()
    {
        var land = (Land)NamedCardFactory.Create("Sunlit Marsh", _alice);

        land.Name.Should().Be("Sunlit Marsh");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Plains).Should().BeTrue("the type line is Land — Plains Swamp");
        land.HasSubtype(CardSubtype.Swamp).Should().BeTrue("the type line is Land — Plains Swamp");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Sunlit Marsh is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SunlitMarsh_HasTwoManaAbilities_ProducingWhiteAndBlack()
    {
        var land = (Land)NamedCardFactory.Create("Sunlit Marsh", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Sunlit Marsh taps for {W} or {B}");
        mana.Should().Contain(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Black == 0);
        mana.Should().Contain(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void SunlitMarsh_HasNoNonManaActivatedAbility()
    {
        var land = (Land)NamedCardFactory.Create("Sunlit Marsh", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("a plain enters-tapped dual has no activated abilities");
    }
}
