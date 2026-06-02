using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Grisly Salvage (Dragon's Maze, {B}{G}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Reveal the top five cards of your library. You may put a creature or
///    land card from among them into your hand. Put the rest into your
///    graveyard."
///
/// Card shape comes from the embedded JSON (<c>grisly-salvage.json</c>) via
/// <see cref="CardDefinitionLoader"/> + <see cref="CardDefinitionFactory"/>;
/// the resolve-time reveal-and-choose body is built by the factory and routes
/// through the shared <see cref="RevealAndChoose"/> primitive (same posture as
/// <see cref="MalevolentRumbleFactory"/>, but the eligible predicate is
/// "creature OR land" and there is no token half).
///
/// Covers:
///   - Card shape + dispatch ({B}{G}, Instant, owner/controller).
///   - Reveal up to top 5; cards leave the library.
///   - A creature or land in the top 5 goes to the caster's hand.
///   - Non-creature/non-land cards go to the graveyard.
///   - All-ineligible: nothing to hand, all 5 to graveyard.
///   - Empty library: clean no-op (no throw).
/// </summary>
[Trait("Color", "BG")]
public class GrislySalvageFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity + dispatch ───────────────────────────────────────────────────

    [Fact]
    public void GrislySalvage_HasInstantShape_AtCostBG()
    {
        var card = GrislySalvageFactory.Create(_alice);

        card.Name.Should().Be("Grisly Salvage");
        card.ManaCost.Should().Be("{B}{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GrislySalvage()
    {
        var card = NamedCardFactory.Create("Grisly Salvage", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Grisly Salvage");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{B}{G}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NameAndCost_AreScryfallExact()
    {
        GrislySalvageFactory.CardName.Should().Be("Grisly Salvage");
        GrislySalvageFactory.PrintedManaCost.Should().Be("{B}{G}");
    }

    // ── Resolve: reveal-5 / creature-or-land → hand / rest → graveyard ────────

    [Fact]
    public void Resolve_RevealsTopFive_FromLibrary()
    {
        // Seed 7 cards on the library (only top 5 are revealed).
        var c1 = SeedLibraryCard(new Instant("I1", ""));
        var c2 = SeedLibraryCard(new Instant("I2", ""));
        var c3 = SeedLibraryCard(new Instant("I3", ""));
        var c4 = SeedLibraryCard(new Instant("I4", ""));
        var c5 = SeedLibraryCard(new Instant("I5", ""));
        var c6 = SeedLibraryCard(new Instant("I6", ""));
        var c7 = SeedLibraryCard(new Instant("I7", ""));

        Resolve();

        // Top 5 left the library; bottom 2 remain.
        _alice.Zones.Library.GetCards().Should().NotContain(new[] { c1, c2, c3, c4, c5 });
        _alice.Zones.Library.GetCards().Should().Contain(new[] { c6, c7 });
    }

    [Fact]
    public void Resolve_FirstCreatureOrLand_GoesToHand_RestGoToGraveyard()
    {
        var instant1 = SeedLibraryCard(new Instant("Counterspell", "UU"));
        var sorcery = SeedLibraryCard(new Sorcery("Doom Blade", "1B"));
        var bear = SeedLibraryCard(new Creature("Bear", "1G", 2, 2));
        var instant2 = SeedLibraryCard(new Instant("Shock", "R"));
        var instant3 = SeedLibraryCard(new Instant("Opt", "U"));

        Resolve();

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(bear, "first creature/land in the top 5 goes to hand");
        bear.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Graveyard.GetCards()
            .Should().Contain(new Card[] { instant1, sorcery, instant2, instant3 },
                "non-creature/non-land cards go to graveyard");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void Resolve_LandCard_IsEligible_GoesToHand()
    {
        SeedLibraryCard(new Instant("Counterspell", "UU"));
        var forest = SeedLibraryCard(
            new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest }));

        Resolve();

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(forest, "a land card is a legal pick (creature OR land)");
        forest.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_AllIneligible_NothingGoesToHand_AllGoToGraveyard()
    {
        var cards = new[] { "A", "B", "C", "D", "E" }
            .Select(n => SeedLibraryCard(new Instant(n, "")))
            .ToList();

        Resolve();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(cards);
    }

    [Fact]
    public void Resolve_EmptyLibrary_DoesNotThrow()
    {
        var act = () => Resolve();

        act.Should().NotThrow("empty library is a clean no-op");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Resolve()
    {
        foreach (var e in GrislySalvageFactory.BuildResolveEffect(_alice))
            e.Execute();
    }

    private T SeedLibraryCard<T>(T card) where T : Card
    {
        card.SetOwner(_alice);
        card.SetController(_alice);
        _alice.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        return card;
    }
}
