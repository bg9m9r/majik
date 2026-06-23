using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ObeliskOfGrixisFactory"/> — the Alara Reborn
/// Grixis "Obelisk" tri-colour mana rock. Oracle text (verified against
/// Scryfall):
///   "{T}: Add {U}, {B}, or {R}."
///
/// Pure mana rock (no cycling). Covers:
/// - Identity (Artifact, mana cost {3}).
/// - Three "{T}: Add" mana abilities producing {U}, {B}, {R} (CR 605.1),
///   gated on the artifact being untapped.
/// (Dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests, so no dispatch test is needed here.)
/// </summary>
[Trait("Color", "M")]
public class ObeliskOfGrixisFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ObeliskOfGrixis_Identity_ArtifactCostThree()
    {
        var artifact = (Artifact)NamedCardFactory.Create("Obelisk of Grixis", _alice);

        artifact.Name.Should().Be("Obelisk of Grixis");
        artifact.ManaCostValue.Generic.Should().Be(3, "Obelisk of Grixis costs {3}");
        artifact.ManaCostValue.Blue.Should().Be(0, "{3} has no coloured pips");
        artifact.ManaCostValue.Black.Should().Be(0);
        artifact.ManaCostValue.Red.Should().Be(0);
    }

    [Fact]
    public void ObeliskOfGrixis_HasThreeManaAbilities_ProducingUBR()
    {
        var artifact = (Artifact)NamedCardFactory.Create("Obelisk of Grixis", _alice);
        var mana = artifact.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(3, "Obelisk taps for {U}, {B}, or {R}");
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
    }

    [Fact]
    public void ObeliskOfGrixis_ManaAbilities_GatedOnUntapped()
    {
        var artifact = (Artifact)NamedCardFactory.Create("Obelisk of Grixis", _alice);
        var blue = artifact.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Blue == 1);

        // CR 605.1 — the tap ({T}) is the mana ability's cost; while the
        // artifact is untapped the slot is active, and tapping it disables it.
        blue.CanActivate().Should().BeTrue("untapped artifact can tap for mana");
        artifact.Tap();
        blue.CanActivate().Should().BeFalse("a tapped artifact cannot tap again");
    }
}
