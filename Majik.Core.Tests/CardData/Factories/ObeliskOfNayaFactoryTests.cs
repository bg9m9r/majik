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
/// Unit tests for <see cref="ObeliskOfNayaFactory"/>.
///
/// Obelisk of Naya — Artifact {3} (verified against Scryfall).
///   "{T}: Add {R}, {G}, or {W}."
///
/// Covers:
/// - Card identity (Artifact, mana cost {3}) — non-vanilla shell, single assert.
/// - Three Naya mana-ability slots, each producing exactly one of {R}/{G}/{W}
///   (CR 605.1a — a "choose one of these colours" mana ability is modelled as
///   one distinct ManaAbility slot per producible colour; the implicit {T}
///   self-tap is baked into ManaAbility's simple constructor).
///
/// (Dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests, so no dispatch test is duplicated here.)
/// </summary>
[Trait("Color", "C")]
public class ObeliskOfNayaFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity
    // --------------------------------------------------------------

    [Fact]
    public void ObeliskOfNaya_IsArtifact_ThreeCost()
    {
        var rock = ObeliskOfNayaFactory.Create(_alice);

        rock.Name.Should().Be("Obelisk of Naya");
        rock.HasType(CardType.Artifact).Should().BeTrue();
        rock.ManaCost.Should().Be("{3}");
        rock.Owner.Should().BeSameAs(_alice);
        rock.Controller.Should().BeSameAs(_alice);
    }

    // --------------------------------------------------------------
    // Ability shape — three Naya mana abilities (R/G/W)
    // --------------------------------------------------------------

    [Fact]
    public void ObeliskOfNaya_HasThreeManaAbilities_OnePerNayaColor()
    {
        var rock = (Artifact)NamedCardFactory.Create("Obelisk of Naya", _alice);

        var manaAbilities = rock.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(3, "Obelisk of Naya taps for {R}, {G}, or {W}");

        // Each slot produces exactly one mana (one of R/G/W).
        manaAbilities.Should().OnlyContain(ma => ma.ManaGenerated.TotalValue == 1);
    }

    [Fact]
    public void ObeliskOfNaya_ProducesRedGreenWhite_OnlyNayaColors()
    {
        var rock = (Artifact)NamedCardFactory.Create("Obelisk of Naya", _alice);

        var produced = rock.Abilities.OfType<ManaAbility>()
            .Select(ma => ma.ManaGenerated)
            .ToList();

        // Exactly one slot for each Naya colour (CR 605.1a).
        produced.Should().ContainSingle(m => m.Red == 1);
        produced.Should().ContainSingle(m => m.Green == 1);
        produced.Should().ContainSingle(m => m.White == 1);

        // ...and no off-colour mana (no blue, no black).
        produced.Should().NotContain(m => m.Blue == 1);
        produced.Should().NotContain(m => m.Black == 1);
    }
}
