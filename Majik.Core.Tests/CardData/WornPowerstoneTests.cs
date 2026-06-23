using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="WornPowerstoneFactory"/>.
///
/// Worn Powerstone — Artifact {3}.
///   "This artifact enters tapped.
///    {T}: Add {C}{C}."
///
/// Covers the card's UNIQUE behaviour:
/// - Card identity (Artifact, mana cost {3}) — a single _Identity assert.
/// - The {T}: Add {C}{C} mana ability shape (single ManaAbility, two
///   colourless folded into the generic bucket per CR 107.4c).
///
/// Dispatch / well-formedness is covered for every implemented card by
/// <c>CardFactoryContractTests</c>; enters-tapped (CR 614.1c) is owned by
/// <c>EntersTappedBinder</c> on the production load path (oracle-text driven),
/// not this factory, so it isn't asserted here.
/// </summary>
[Trait("Color", "C")]
public class WornPowerstoneTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity
    // --------------------------------------------------------------

    [Fact]
    public void WornPowerstone_Identity_IsArtifact_ThreeCost()
    {
        var stone = WornPowerstoneFactory.Create(_alice);

        stone.Name.Should().Be("Worn Powerstone");
        stone.HasType(CardType.Artifact).Should().BeTrue();
        stone.ManaCost.Should().Be("{3}");
        stone.Owner.Should().BeSameAs(_alice);
        stone.Controller.Should().BeSameAs(_alice);
    }

    // --------------------------------------------------------------
    // {T}: Add {C}{C}
    // --------------------------------------------------------------

    [Fact]
    public void WornPowerstone_HasSingleManaAbility()
    {
        var stone = WornPowerstoneFactory.Create(_alice);

        stone.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        // No activated/triggered abilities — Worn Powerstone is a pure rock.
        stone.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void TapForColorless_ProducesTwoGeneric()
    {
        var stone = WornPowerstoneFactory.Create(_alice);
        var ma = stone.Abilities.OfType<ManaAbility>().Single();

        // {C}{C} folds into the generic bucket via ManaCost.Parse (CR 107.4c).
        ma.ManaGenerated.TotalValue.Should().Be(2);
    }
}
