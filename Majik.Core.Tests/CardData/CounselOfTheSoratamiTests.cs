using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="CounselOfTheSoratamiFactory"/>.
///
/// Card: Counsel of the Soratami — Sorcery {2}{U} (Champions of Kamigawa).
///   "Draw two cards."
///
/// Covers:
///   - Identity (name, Sorcery type, mana cost {2}{U}, owner/controller, mana value 3).
///   - NamedCardFactory dispatch returns a Sorcery.
///   - Resolve effect draws two cards from top of library.
///   - Empty library mid-resolve flags the SBA-driven loss (CR 704.5b).
///   - One-card library: draws one, then flags on the second attempt.
/// </summary>
public class CounselOfTheSoratamiTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Card SeedLibraryCard(Player owner, string name)
    {
        var c = new Creature(name, "{0}", 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void CounselOfTheSoratami_Identity()
    {
        var c = CounselOfTheSoratamiFactory.Create(_alice);

        c.Name.Should().Be("Counsel of the Soratami");
        c.ManaCost.Should().Be("{2}{U}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CounselOfTheSoratami_ManaValue_IsThree()
    {
        var c = CounselOfTheSoratamiFactory.Create(_alice);

        // {2}{U} = generic 2 + one blue pip = CMC 3
        c.ManaCostValue.TotalValue.Should().Be(3);
    }

    [Fact]
    public void CounselOfTheSoratami_IsBlue()
    {
        var c = CounselOfTheSoratamiFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Blue,
            "Counsel of the Soratami has a {U} pip so it is blue");
    }

    [Fact]
    public void CounselOfTheSoratami_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Counsel of the Soratami", _alice);

        c.Should().BeOfType<Sorcery>();
        c.Name.Should().Be("Counsel of the Soratami");
        c.HasType(CardType.Sorcery).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Resolve: draw two cards (CR 121.1)
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsTwoCardsFromTopOfLibrary()
    {
        var c1 = SeedLibraryCard(_alice, "Top1");
        var c2 = SeedLibraryCard(_alice, "Top2");
        SeedLibraryCard(_alice, "Top3"); // remains in library

        var effects = CounselOfTheSoratamiFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(new[] { c1, c2 });
        c1.Zone.Should().Be(ZoneType.Hand);
        c2.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().HaveCount(1,
            "exactly two cards were drawn off the top");
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void Resolve_EmptyLibrary_FlagsSbaLossOnFirstDraw()
    {
        // Library is empty — first draw attempt flags the SBA loss.
        var effects = CounselOfTheSoratamiFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "empty library mid-draw flags the SBA-driven loss (CR 704.5b)");
    }

    [Fact]
    public void Resolve_OneCardLibrary_DrawsOne_FlagsSbaLossOnSecondDraw()
    {
        var only = SeedLibraryCard(_alice, "OnlyCard");

        var effects = CounselOfTheSoratamiFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(only);
        only.Zone.Should().Be(ZoneType.Hand);
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "the second draw came up empty — SBA flag is set");
    }
}
