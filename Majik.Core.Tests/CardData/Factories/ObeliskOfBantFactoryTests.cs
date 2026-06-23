using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ObeliskOfBantFactory"/> — the Bant ({G}{W}{U})
/// member of the Alara "Obelisk" tri-colour mana-rock cycle. Oracle text
/// (verified against Scryfall):
///   "{T}: Add {G}, {W}, or {U}."
///
/// Covers:
/// - Identity (Artifact, mana cost {3}, no P/T or subtypes).
/// - Three mana abilities producing {G}, {W}, {U} respectively (CR 605.1 —
///   each "or"-colour is its own single-colour mana ability; mana abilities
///   don't use the stack so no separate colour prompt is needed).
/// </summary>
[Trait("Color", "C")]
public class ObeliskOfBantFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ObeliskOfBant_IsArtifact_ThreeCost()
    {
        var obelisk = ObeliskOfBantFactory.Create(_alice);

        obelisk.Name.Should().Be("Obelisk of Bant");
        obelisk.HasType(CardType.Artifact).Should().BeTrue();
        obelisk.ManaCost.Should().Be("{3}");
        obelisk.Owner.Should().BeSameAs(_alice);
        obelisk.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ObeliskOfBant_HasThreeManaAbilities_ProducingGWU()
    {
        var obelisk = ObeliskOfBantFactory.Create(_alice);
        var mana = obelisk.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(3, "Obelisk of Bant taps for {G}, {W}, or {U}");
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
        mana.Should().OnlyContain(m => m.ManaGenerated.TotalValue == 1);
    }
}
