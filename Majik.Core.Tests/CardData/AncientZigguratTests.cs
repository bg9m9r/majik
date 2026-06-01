using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="AncientZigguratFactory"/> — Ancient Ziggurat
/// (Conflux). Oracle text (Scryfall, verified):
///   "{T}: Add one mana of any color. Spend this mana only to cast a creature
///    spell."
///
/// Mirrors <see cref="DelightedHalflingFactory"/>: the "any color" mana
/// ability (CR 106.1b) is modeled as five <see cref="ManaAbility"/> instances
/// (one per WUBRG); the mana picker can satisfy any single colour pip via this
/// land. The "spend only to cast a creature spell" usage restriction is a v1
/// deferral (same posture as Delighted Halfling's "legendary spell"
/// restriction) — enforcement needs per-mana-pool entry tagging + a
/// spend-restriction check in the cast-payment flow.
/// </summary>
public class AncientZigguratTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void AncientZiggurat_Dispatch_ReturnsLand()
    {
        var card = NamedCardFactory.Create("Ancient Ziggurat", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Ancient Ziggurat");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasSupertype(CardSupertype.Basic).Should()
            .BeFalse("Ancient Ziggurat is a nonbasic land");
    }

    // -----------------------------------------------------------------------
    // No mana cost — it's a land (CR 305.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void AncientZiggurat_HasNoManaCost()
    {
        var land = (Land)NamedCardFactory.Create("Ancient Ziggurat", _alice);

        land.ManaCost.Should().Be("");
    }

    // -----------------------------------------------------------------------
    // "Add one mana of any color" — CR 106.1b, modeled as five ManaAbilities
    // -----------------------------------------------------------------------

    [Fact]
    public void AncientZiggurat_HasFiveManaAbilities_OnePerColor()
    {
        var land = (Land)NamedCardFactory.Create("Ancient Ziggurat", _alice);
        var mas = land.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(5, "one ManaAbility per WUBRG colour ('any color')");
    }

    [Fact]
    public void AncientZiggurat_ManaAbilities_CoverAllFiveColors()
    {
        var land = (Land)NamedCardFactory.Create("Ancient Ziggurat", _alice);
        var mas = land.Abilities.OfType<ManaAbility>().ToList();

        // Each branch produces exactly one coloured pip; together they cover
        // WUBRG (CR 106.1b — "any color" is one of the five colours).
        mas.Sum(m => m.ManaGenerated.White).Should().Be(1);
        mas.Sum(m => m.ManaGenerated.Blue).Should().Be(1);
        mas.Sum(m => m.ManaGenerated.Black).Should().Be(1);
        mas.Sum(m => m.ManaGenerated.Red).Should().Be(1);
        mas.Sum(m => m.ManaGenerated.Green).Should().Be(1);
        mas.Sum(m => m.ManaGenerated.Generic).Should()
            .Be(0, "'any color' never produces colorless/generic mana");
    }

    // -----------------------------------------------------------------------
    // Owner / controller
    // -----------------------------------------------------------------------

    [Fact]
    public void AncientZiggurat_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Ancient Ziggurat", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
}
