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
/// Unit tests for <see cref="JacesIngenuityFactory"/>.
///
/// Card: Jace's Ingenuity — Instant {3}{U}{U}.
///   "Draw three cards."
///
/// Covers:
///   - Identity (name, Instant type, mana cost {3}{U}{U}, owner/controller).
///   - NamedCardFactory dispatch returns an Instant.
///   - Resolve effect draws exactly three cards from top of library.
///   - Library shrinks by three on resolve.
///   - Empty library mid-resolve flags the SBA-driven loss (CR 704.5b).
///   - One-card library: draws one, flags SBA loss on second draw attempt.
/// </summary>
[Trait("Color", "U")]
public class JacesIngenuityFactoryTests
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
    public void JacesIngenuity_Identity()
    {
        var c = JacesIngenuityFactory.Create(_alice);

        c.Name.Should().Be("Jace's Ingenuity");
        c.ManaCost.Should().Be("{3}{U}{U}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // -------------------------------------------------------------------------
    // Resolve: draw three cards (CR 121.1)
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsThreeCardsFromTopOfLibrary()
    {
        var c1 = SeedLibraryCard(_alice, "Top1");
        var c2 = SeedLibraryCard(_alice, "Top2");
        var c3 = SeedLibraryCard(_alice, "Top3");
        SeedLibraryCard(_alice, "Top4"); // remains in library

        var effects = JacesIngenuityFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(new[] { c1, c2, c3 },
            "all three drawn cards move to hand");
        c1.Zone.Should().Be(ZoneType.Hand);
        c2.Zone.Should().Be(ZoneType.Hand);
        c3.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().HaveCount(1,
            "exactly three cards were drawn off the top");
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void Resolve_EmptyLibrary_FlagsSbaLossOnFirstDraw()
    {
        // No cards in library — first draw attempt flags SBA loss.
        var effects = JacesIngenuityFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "empty library mid-draw flags the SBA-driven loss (CR 704.5b)");
    }

    [Fact]
    public void Resolve_OneCardLibrary_DrawsOne_FlagsSbaLossOnSecondDraw()
    {
        var only = SeedLibraryCard(_alice, "OnlyCard");

        var effects = JacesIngenuityFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(only);
        only.Zone.Should().Be(ZoneType.Hand);
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "the second draw came up empty — SBA flag is set");
    }
}
