using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="UnexpectedWindfallFactory"/>.
///
/// Unexpected Windfall (Streets of New Capenna, {2}{R}{R}):
///   Instant. As an additional cost to cast this spell, discard a card.
///   Draw two cards and create two Treasure tokens.
///
/// Character-for-character identical oracle text to <see cref="BigScoreFactory"/>
/// (Big Score, {3}{R}); only the mana cost differs.
///
/// Covers:
///   - Card identity (Instant, {2}{R}{R}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatcher entry.
///   - Resolve: discard 1, draw 2, create 2 Treasures. Net hand size
///     change = -1 + 2 = +1 when hand had a card to discard and the
///     library has ≥2 cards.
///   - Treasure token shape: two artifacts on the battlefield, each
///     colourless with five any-colour ManaAbility options (CR 111.10).
///   - Empty hand: discard is a no-op (v1 deviation — see factory docs),
///     still draws 2 and makes 2 Treasures.
///   - Empty library mid-draw: draws what's available, SBA flag set
///     (CR 704.5b), Treasures still created.
///
/// Note on the documented printed-text deviation (additional cost vs
/// resolve-side discard): see <see cref="UnexpectedWindfallFactory"/>'s XML
/// docs. v1 ships the discard at resolve, so this suite exercises resolve-side
/// discard behaviour rather than the cast-time additional-cost gate.
/// </summary>
public class UnexpectedWindfallTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void UnexpectedWindfall_Identity()
    {
        var c = UnexpectedWindfallFactory.Create(_alice);

        c.Name.Should().Be("Unexpected Windfall");
        c.ManaCost.Should().Be("{2}{R}{R}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_UnexpectedWindfall()
    {
        var card = NamedCardFactory.Create("Unexpected Windfall", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Unexpected Windfall");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{R}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_DiscardsOne_DrawsTwo_CreatesTwoTreasures()
    {
        // Hand: 1 card. Library: 3 cards.
        // Net hand: 1 - 1 discarded + 2 drawn = 2 in hand.
        var inHand = SeedHandCard(_alice, "Hand1");
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        var top3 = SeedLibraryCard(_alice, "Top3");

        var effects = UnexpectedWindfallFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        // Discard happens before draws — the starting hand card is binned.
        _alice.Zones.Graveyard.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(inHand);
        inHand.Zone.Should().Be(ZoneType.Graveyard);

        // Drew the top two; one library card remains.
        _alice.Zones.Hand.GetCards().Should().HaveCount(2)
            .And.Contain(new[] { top1, top2 });
        _alice.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(top3);

        // Two Treasure tokens on the battlefield.
        var treasures = _alice.Zones.Battlefield.GetCards().ToList();
        treasures.Should().HaveCount(2,
            "Unexpected Windfall creates exactly two Treasure tokens (CR 111.10)");
        treasures.Should().OnlyContain(t => t.Name == "Treasure");
        treasures.Should().OnlyContain(t => t.HasType(CardType.Artifact));
        treasures.Should().OnlyContain(t => t.Zone == ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_EachTreasure_HasFiveManaAbilities_AnyColour()
    {
        // CR 111.10 — "{T}, Sacrifice this token: Add one mana of any color."
        // Each Treasure encodes one ManaAbility per colour (W/U/B/R/G).
        var effects = UnexpectedWindfallFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        foreach (var treasure in _alice.Zones.Battlefield.GetCards())
        {
            treasure.Abilities.OfType<ManaAbility>().Should().HaveCount(5,
                "each Treasure token encodes one ManaAbility per colour");
            (treasure as Permanent)?.IsToken.Should().BeTrue();
        }
    }

    [Fact]
    public void Resolve_FromEmptyHand_DiscardIsNoOp_StillDrawsTwo_AndMakesTwoTreasures()
    {
        // Empty hand → no discard (v1 deviation). Library has 2 cards.
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");

        var effects = UnexpectedWindfallFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Hand.GetCards().Should().HaveCount(2)
            .And.Contain(new[] { top1, top2 });
        _alice.Zones.Battlefield.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void Resolve_EmptyLibrary_DrawsWhatsAvailable_FlagsSbaLoss_StillMakesTreasures()
    {
        // Hand: 1 card. Library: 1 card. Discard 1 → hand empty; first draw
        // takes the only library card; second draw hits empty → SBA flag.
        SeedHandCard(_alice, "Hand1");
        var only = SeedLibraryCard(_alice, "Only");

        var effects = UnexpectedWindfallFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "second draw hit an empty library — SBA flag must be set (CR 704.5b)");
        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(only);
        _alice.Zones.Graveyard.GetCards().Should().ContainSingle();
        _alice.Zones.Battlefield.GetCards().Should().HaveCount(2,
            "Treasures are created even when draws ran out (CR 111.10)");
    }

    private static ICard SeedLibraryCard(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static ICard SeedHandCard(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }
}
