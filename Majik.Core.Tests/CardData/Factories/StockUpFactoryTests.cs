using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Stock Up (Outlaws of Thunder Junction, {2}{U}, Sorcery).
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "Look at the top five cards of your library. Put two of them into your
///    hand and the rest on the bottom of your library in any order."
///
/// Covers:
///   - Card identity (Sorcery, {2}{U}, blue, owner/controller) materialised
///     from the embedded JSON definition via CardDefinitionLoader.
///   - NamedCardFactory dispatch by name.
///   - Look-5 / hand-2 / bottom-rest resolve — default-selector deterministic
///     path (first two to hand, remaining three to the bottom in peek order).
///   - Fewer than five cards in library: peeks what exists, still hands two
///     when at least two are present, bottoms the rest.
///   - Fewer than two cards in library: hands however many exist (no underflow).
///   - Empty library: no moves.
/// </summary>
[Trait("Color", "U")]
public class StockUpFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void StockUp_HasSorceryShape_Blue_AtCost2U()
    {
        var card = StockUpFactory.Create(_alice);

        card.Name.Should().Be("Stock Up");
        card.ManaCost.Should().Be("{2}{U}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCostValue.TotalValue.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void StockUp_Resolve_FullLibrary_HandsTopTwo_BottomsRestInOrder()
    {
        // Library top-to-bottom: [L1, L2, L3, L4, L5, L6].
        // Look at top five [L1..L5]. Default selector: hand L1, L2; bottom
        // L3, L4, L5 in peek order. Result library top-to-bottom:
        // [L6, L3, L4, L5].
        var l1 = NewLibraryCardAtEnd("L1");
        var l2 = NewLibraryCardAtEnd("L2");
        var l3 = NewLibraryCardAtEnd("L3");
        var l4 = NewLibraryCardAtEnd("L4");
        var l5 = NewLibraryCardAtEnd("L5");
        var l6 = NewLibraryCardAtEnd("L6");

        var effect = StockUpFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { l1, l2 });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { l6, l3, l4, l5 });
        l1.Zone.Should().Be(ZoneType.Hand);
        l2.Zone.Should().Be(ZoneType.Hand);
        l3.Zone.Should().Be(ZoneType.Library);
        l4.Zone.Should().Be(ZoneType.Library);
        l5.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void StockUp_Resolve_FewerThanFive_PeeksWhatExists_HandsTwo_BottomsRest()
    {
        // Only four cards. Look at all four. Hand L1, L2; bottom L3, L4.
        // Library top-to-bottom ends [L3, L4] (the two non-handed cards
        // re-appended in peek order; nothing else remained).
        var l1 = NewLibraryCardAtEnd("L1");
        var l2 = NewLibraryCardAtEnd("L2");
        var l3 = NewLibraryCardAtEnd("L3");
        var l4 = NewLibraryCardAtEnd("L4");

        var effect = StockUpFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { l1, l2 });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { l3, l4 });
    }

    [Fact]
    public void StockUp_Resolve_OneCardLibrary_HandsThatOne_NoUnderflow()
    {
        // Single card: hand it (HandAmount=2 but Take caps at available),
        // nothing left to bottom.
        var l1 = NewLibraryCardAtEnd("L1");

        var effect = StockUpFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { l1 });
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        l1.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void StockUp_Resolve_EmptyLibrary_NoMoves()
    {
        var effect = StockUpFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void StockUp_Resolve_CustomSelector_DrivesHandAndBottomOrder()
    {
        // Library top-to-bottom: [L1, L2, L3, L4, L5]. Custom selector hands
        // L3 + L5, bottoms the rest in order [L4, L2, L1]. Result library
        // top-to-bottom: [L4, L2, L1] (all five peeked, three bottomed).
        var l1 = NewLibraryCardAtEnd("L1");
        var l2 = NewLibraryCardAtEnd("L2");
        var l3 = NewLibraryCardAtEnd("L3");
        var l4 = NewLibraryCardAtEnd("L4");
        var l5 = NewLibraryCardAtEnd("L5");

        StockUpFactory.Selector selector = peeked =>
        {
            var toHand = new[]
            {
                peeked.First(c => c.Name == "L3"),
                peeked.First(c => c.Name == "L5"),
            };
            var toBottom = new[]
            {
                peeked.First(c => c.Name == "L4"),
                peeked.First(c => c.Name == "L2"),
                peeked.First(c => c.Name == "L1"),
            };
            return (toHand, toBottom);
        };

        var effect = StockUpFactory.BuildResolveEffect(_alice, selector).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { l3, l5 });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { l4, l2, l1 });
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private ICard NewLibraryCardAtEnd(string name)
    {
        var c = new Sorcery(name, "{0}") { Owner = _alice, Controller = _alice };
        c.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(c);
        return c;
    }
}
