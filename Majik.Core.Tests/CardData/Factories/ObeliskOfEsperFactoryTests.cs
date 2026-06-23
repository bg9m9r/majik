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
/// Unit tests for <see cref="ObeliskOfEsperFactory"/> — the Esper ({W}{U}{B})
/// three-colour mana rock from Shards of Alara. Oracle text (verified against
/// Scryfall):
///   "{T}: Add {W}, {U}, or {B}."
///
/// Covers:
/// - Identity (Artifact, mana cost {3}).
/// - Three mana abilities producing {W}, {U}, {B} respectively (CR 605.1 —
///   mana abilities don't use the stack; the activator picks a colour by
///   picking the matching mana-ability slot).
///
/// (Dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests, so this file only exercises the unique behaviour.)
/// </summary>
[Trait("Color", "C")]
public class ObeliskOfEsperFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ObeliskOfEsper_IsArtifact_ThreeCost()
    {
        var obelisk = ObeliskOfEsperFactory.Create(_alice);

        obelisk.Name.Should().Be("Obelisk of Esper");
        obelisk.HasType(CardType.Artifact).Should().BeTrue();
        obelisk.ManaCost.Should().Be("{3}");
        obelisk.Owner.Should().BeSameAs(_alice);
        obelisk.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ObeliskOfEsper_HasThreeManaAbilities_ProducingWUB()
    {
        var obelisk = (Artifact)NamedCardFactory.Create("Obelisk of Esper", _alice);
        var mana = obelisk.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(3, "Obelisk of Esper taps for {W}, {U}, or {B}");
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
        mana.Should().OnlyContain(m => m.ManaGenerated.TotalValue == 1);
    }
}
