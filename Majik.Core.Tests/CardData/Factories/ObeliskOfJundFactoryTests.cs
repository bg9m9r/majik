using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ObeliskOfJundFactory"/> — the Shards-of-Alara
/// "Obelisk" tri-colour mana rock. Oracle text (Scryfall):
///   "{T}: Add {B}, {R}, or {G}."
/// Artifact, mana cost {3}, no other abilities (no cycling).
///
/// Covers:
/// - Identity (Artifact, mana cost {3}).
/// - Exactly three mana abilities producing {B}, {R}, {G} respectively, each a
///   single coloured pip (CR 605.1 — mana abilities don't use the stack).
/// - No non-mana activated abilities (unlike the Ikoria Crystals, no cycling).
///
/// Dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests, so they are not duplicated here.
/// </summary>
[Trait("Color", "M")]
public class ObeliskOfJundFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ObeliskOfJund_IsArtifact_ThreeCost()
    {
        var obelisk = ObeliskOfJundFactory.Create(_alice);

        obelisk.Name.Should().Be("Obelisk of Jund");
        obelisk.HasType(CardType.Artifact).Should().BeTrue();
        obelisk.ManaCost.Should().Be("{3}");
        obelisk.Owner.Should().BeSameAs(_alice);
        obelisk.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ObeliskOfJund_HasThreeManaAbilities_ProducingBRG()
    {
        var obelisk = ObeliskOfJundFactory.Create(_alice);
        var mana = obelisk.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(3, "Obelisk of Jund taps for {B}, {R}, or {G}");
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);

        // Each produces exactly one coloured pip.
        mana.Should().OnlyContain(m => m.ManaGenerated.TotalValue == 1);
    }

    [Fact]
    public void ObeliskOfJund_HasNoNonManaActivatedAbilities()
    {
        var obelisk = ObeliskOfJundFactory.Create(_alice);

        // Plain rock: no cycling. Mana abilities are ManaAbility (not
        // ActivatedAbility), so a plain rock has zero ActivatedAbility entries —
        // unlike the Ikoria Crystals, whose cycling surfaces as one.
        obelisk.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
