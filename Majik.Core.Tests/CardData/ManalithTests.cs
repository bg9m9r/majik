using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ManalithFactory"/>.
///
/// Manalith — Artifact {3} (verified against Scryfall).
///   "{T}: Add one mana of any color."
///
/// Covers:
/// - Card identity (Artifact, mana cost {3}).
/// - NamedCardFactory dispatch.
/// - Five WUBRG mana-ability slots, each producing exactly one coloured mana
///   (CR 605.1a — "any color" is modelled as five distinct ManaAbility slots,
///   the same shape used by Springleaf Drum / Crumbling Vestige). The implicit
///   {T} self-tap is baked into ManaAbility's simple constructor.
/// </summary>
public class ManalithTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void Manalith_IsArtifact_ThreeCost()
    {
        var rock = (Artifact)NamedCardFactory.Create("Manalith", _alice);

        rock.Name.Should().Be("Manalith");
        rock.HasType(CardType.Artifact).Should().BeTrue();
        rock.ManaCost.Should().Be("{3}");
        rock.Owner.Should().BeSameAs(_alice);
        rock.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Manalith()
    {
        var card = NamedCardFactory.Create("Manalith", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Manalith");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{3}");
    }

    // --------------------------------------------------------------
    // Ability shape — five single-colour mana abilities (WUBRG)
    // --------------------------------------------------------------

    [Fact]
    public void Manalith_HasFiveManaAbilities_OnePerColor()
    {
        var rock = (Artifact)NamedCardFactory.Create("Manalith", _alice);

        var manaAbilities = rock.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(5);

        // Each slot produces exactly one mana (one of W/U/B/R/G).
        manaAbilities.Should().OnlyContain(ma => ma.ManaGenerated.TotalValue == 1);
    }

    [Fact]
    public void Manalith_ProducesEveryColor()
    {
        var rock = (Artifact)NamedCardFactory.Create("Manalith", _alice);

        var produced = rock.Abilities.OfType<ManaAbility>()
            .Select(ma => ma.ManaGenerated)
            .ToList();

        // Exactly one slot for each WUBRG colour (CR 605.1a).
        produced.Should().ContainSingle(m => m.White == 1);
        produced.Should().ContainSingle(m => m.Blue == 1);
        produced.Should().ContainSingle(m => m.Black == 1);
        produced.Should().ContainSingle(m => m.Red == 1);
        produced.Should().ContainSingle(m => m.Green == 1);
    }
}
