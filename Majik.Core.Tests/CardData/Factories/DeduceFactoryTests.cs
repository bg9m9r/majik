using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="DeduceFactory"/> — Instant {1}{U}.
///
/// Oracle (verified against Scryfall):
///   "Draw a card. Investigate. (Create a Clue token. It's an artifact with
///    "{2}, Sacrifice this token: Draw a card.")"
///
/// CardFactoryContractTests already asserts NamedCardFactory dispatch +
/// well-formedness for every implemented card, so this suite covers only
/// Deduce's UNIQUE behaviour (its resolve: draw a card + investigate) plus a
/// single identity assert for the exact mana cost.
/// </summary>
[Trait("Color", "U")]
public class DeduceFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity (exact mana cost / type / colour)
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_ManaCostAndType()
    {
        var card = DeduceFactory.Create(_alice);

        card.Name.Should().Be("Deduce");
        card.ManaCost.Should().Be("{1}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Blue, "the {U} pip makes it blue");
        colors.Should().NotContain(ManaColor.Red);
    }

    // -----------------------------------------------------------------------
    // Resolve — draw a card + investigate (create a Clue)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsOneCard_AndCreatesClue()
    {
        var top1 = SeedLibraryCard(_alice, "Top1");
        SeedLibraryCard(_alice, "Top2");

        foreach (var e in DeduceFactory.BuildResolveEffect(_alice)) e.Execute();

        // CR 121.1 — exactly one card drawn (the top of library).
        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(top1);
        _alice.Zones.Library.GetCards().Should().ContainSingle();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();

        // CR 701.39 — one Clue token created under the caster.
        _alice.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Should().ContainSingle(p => p.HasSubtype(CardSubtype.Clue),
                "Deduce investigates — it creates one Clue token (CR 701.39)");
    }

    [Fact]
    public void Resolve_EmptyLibrary_StillCreatesClue_FlagsSbaLoss()
    {
        // No cards in library — the draw runs the library dry.
        foreach (var e in DeduceFactory.BuildResolveEffect(_alice)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "the draw hit an empty library — SBA loss flag must be set (CR 704.5b)");

        // Investigate still resolves even when the draw runs the library dry.
        _alice.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Should().ContainSingle(p => p.HasSubtype(CardSubtype.Clue),
                "the Clue is still created even when the draw runs the library dry");
    }

    private static ICard SeedLibraryCard(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
