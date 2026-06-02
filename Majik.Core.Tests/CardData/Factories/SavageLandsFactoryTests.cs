using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SavageLandsFactory"/> — the B/R/G "tapland"
/// (Shards of Alara / reprints). Oracle text:
///   "This land enters tapped.
///    {T}: Add {B}, {R}, or {G}."
///
/// Unlike the Triome cycle (Ziatora's Proving Ground), Savage Lands carries
/// <b>no</b> basic-land subtypes and has <b>no</b> Cycling — it is a plain
/// three-colour tapland. Covers:
/// - Identity (Land, with NO basic-land subtypes and no Cycling ability).
/// - Three single-colour mana abilities ({B}, {R}, {G}) — CR 605.1a.
/// - Owner / controller wiring.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is supplied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the
/// printed oracle text; the dispatcher factory builds the shape without it,
/// same posture as <see cref="ZiatorasProvingGroundFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class SavageLandsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SavageLands_IsALand()
    {
        var land = (Land)NamedCardFactory.Create("Savage Lands", _alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Savage Lands");
    }

    [Fact]
    public void SavageLands_OwnerAndControllerAreSet()
    {
        var land = SavageLandsFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SavageLands_IsNotLegendaryOrBasic()
    {
        var land = SavageLandsFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
    }

    [Theory]
    [InlineData(CardSubtype.Swamp)]
    [InlineData(CardSubtype.Mountain)]
    [InlineData(CardSubtype.Forest)]
    public void SavageLands_CarriesNoBasicLandSubtype(CardSubtype subtype)
    {
        var land = SavageLandsFactory.Create(_alice);

        land.HasSubtype(subtype).Should().BeFalse(
            "Savage Lands' printed type line is just 'Land' — it has no basic-land subtypes");
    }

    [Fact]
    public void SavageLands_HasNoCyclingOrOtherActivatedAbilities()
    {
        var land = SavageLandsFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Savage Lands has no Cycling or other activated (non-mana) abilities");
        land.Abilities.OfType<KeywordAbility>()
            .Should().NotContain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // Mana abilities — CR 605.1a, one per produced colour
    // -----------------------------------------------------------------------

    [Fact]
    public void SavageLands_HasExactlyThreeManaAbilities()
    {
        var land = SavageLandsFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(3,
            "one each for {B}, {R}, {G}");
    }

    [Theory]
    [InlineData("B")]
    [InlineData("R")]
    [InlineData("G")]
    public void SavageLands_HasManaAbilityProducingColor(string color)
    {
        var land = SavageLandsFactory.Create(_alice);

        var match = land.Abilities.OfType<ManaAbility>().Where(m =>
            ManaForColor(m.ManaGenerated, color) == 1
            && TotalColored(m.ManaGenerated) == 1
            && m.ManaGenerated.Generic == 0);

        match.Should().ContainSingle(
            $"exactly one mana ability produces a single {{{color}}}");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static int ManaForColor(ManaCost cost, string color) => color switch
    {
        "W" => cost.White,
        "U" => cost.Blue,
        "B" => cost.Black,
        "R" => cost.Red,
        "G" => cost.Green,
        _ => 0,
    };

    private static int TotalColored(ManaCost cost) =>
        cost.White + cost.Blue + cost.Black + cost.Red + cost.Green;
}
