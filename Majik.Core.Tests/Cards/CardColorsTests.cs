using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// Coverage for <see cref="CardColors.GetColors"/> — CR 202.2 / CR 105.2.
///
/// Mana-cost-pip derivation is already exercised across many card tests
/// (CardData/WishCycleTests, CardData/TaintedIndulgenceTests, etc.). This
/// suite pins the <b>color indicator</b> path: cards whose color is set by
/// a printed color indicator rather than by mana-cost pips. The canonical
/// example is Dryad Arbor — a Land Creature with empty mana cost printed
/// with a green color indicator (Scryfall <c>colors: ["G"]</c>). Before
/// honoring color indicators, <see cref="CardColors.GetColors"/> returned
/// an empty set for Dryad Arbor, which silently broke Green Sun's Zenith
/// and Summoner's Pact's library-search predicates.
/// </summary>
public class CardColorsTests
{
    [Fact]
    public void GetColors_DryadArbor_ReturnsGreen()
    {
        // Dryad Arbor: "Land Creature — Forest Dryad", no printed mana cost,
        // green color indicator (Scryfall colors: ["G"]). CR 202.2c — an
        // object with no mana symbols / no mana cost takes its color from
        // its color indicator. Dryad Arbor is therefore green and must be
        // findable by "green creature" tutors (Green Sun's Zenith,
        // Summoner's Pact, Chord of Calling).
        var owner = new Player("Alice", 20);
        var arbor = (Creature)NamedCardFactory.Create("Dryad Arbor", owner);

        var colors = CardColors.GetColors(arbor);

        colors.Should().Contain(ManaColor.Green);
    }

    [Fact]
    public void GetColors_DryadArbor_BuiltViaScryfallFactory_ReturnsGreen()
    {
        // Same predicate, exercised through the dispatcher path that the
        // production cast / library-load flow uses (NamedCardFactory →
        // DryadArborFactory). Pins that the color indicator survives the
        // CardDefinition / dispatch boundary.
        var owner = new Player("Alice", 20);
        var arbor = NamedCardFactory.Create("Dryad Arbor", owner);

        var colors = CardColors.GetColors(arbor);

        colors.Should().Contain(ManaColor.Green);
    }

    [Fact]
    public void GetColors_PlainCreatureWithGreenManaCost_StillReturnsGreen()
    {
        // Regression sanity: the mana-cost-pip path remains the default
        // when no color indicator is stamped.
        var owner = new Player("Alice", 20);
        var elf = new Creature("Llanowar Elves", "G", 1, 1);
        elf.SetOwner(owner);

        var colors = CardColors.GetColors(elf);

        colors.Should().Contain(ManaColor.Green);
    }

    [Fact]
    public void GetColors_ColorlessLandWithoutIndicator_ReturnsEmpty()
    {
        // Regression sanity: a plain colorless card (no mana cost, no
        // indicator override, no token override) stays colorless.
        var owner = new Player("Alice", 20);
        var rock = new Card("Wastes", "", new[] { CardType.Land });
        rock.SetOwner(owner);

        var colors = CardColors.GetColors(rock);

        colors.Should().BeEmpty();
    }
}
