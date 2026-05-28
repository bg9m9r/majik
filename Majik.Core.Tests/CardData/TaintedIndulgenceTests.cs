using System.Linq;
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
/// Unit tests for <see cref="TaintedIndulgenceFactory"/>.
///
/// Tainted Indulgence (Streets of New Capenna, {U}{B}, Instant):
///   "Draw two cards. Then discard a card unless there are five or more
///    mana values among cards in your graveyard."
///
/// The discard is the DEFAULT; the unless-clause skips it when the
/// controller's graveyard contains cards spanning ≥5 distinct mana values.
///
/// Covers:
///   - Card identity ({U}{B} Instant, MV 2, blue+black, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Resolve with empty graveyard → discard required (net +1 hand).
///   - Resolve with &lt;5 distinct MV in graveyard → discard required (net +1).
///   - Resolve with exactly 5 distinct MVs → discard skipped (net +2).
///   - Resolve with &gt;5 distinct MVs → discard skipped (net +2).
///   - Empty library at resolution: SBA flag set, discard check still runs.
///   - <see cref="TaintedIndulgenceFactory.GraveyardDistinctMvCount"/>
///     correctly computes distinct MV count (including MV-0 lands).
/// </summary>
public class TaintedIndulgenceTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void TaintedIndulgence_HasInstantShape_UB_MV2()
    {
        var card = TaintedIndulgenceFactory.Create(_alice);

        card.Name.Should().Be("Tainted Indulgence");
        card.ManaCost.Should().Be("{U}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCostValue.TotalValue.Should().Be(2);
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TaintedIndulgence()
    {
        var card = NamedCardFactory.Create("Tainted Indulgence", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Tainted Indulgence");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{U}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — discard required (graveyard < 5 distinct MVs)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_EmptyGraveyard_DiscardsOne_NetPlusOne()
    {
        // Hand: 1 card. Library: 3. Graveyard: empty (0 distinct MVs).
        // After: hand = 2 (drew 2, discarded 1), graveyard = 1.
        SeedHandCard("H1", "{1}");
        SeedLibraryCards(3);

        ExecuteResolve();

        _alice.Zones.Hand.GetCards().Should().HaveCount(2,
            because: "drew 2 then discarded 1 → net +1 from the starting 1 card");
        _alice.Zones.Graveyard.GetCards().Should().ContainSingle(
            because: "exactly one card was discarded (graveyard empty → <5 MVs)");
        _alice.Zones.Graveyard.GetCards().Single().Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Resolve_FourDistinctMvsInGraveyard_DiscardsOne()
    {
        // Graveyard has 4 distinct MVs: 0, 1, 2, 3 → below threshold → discard.
        SeedGraveyardCard("G0", "");     // MV 0 (no mana cost)
        SeedGraveyardCard("G1", "{1}");  // MV 1
        SeedGraveyardCard("G2", "{2}");  // MV 2
        SeedGraveyardCard("G3", "{1}{2}"); // MV 3
        SeedHandCard("H1", "{R}");
        SeedLibraryCards(3);

        var handBefore = _alice.Zones.Hand.GetCards().Count();
        ExecuteResolve();

        // Drew 2, discarded 1 → net +1.
        _alice.Zones.Hand.GetCards().Should().HaveCount(handBefore + 1,
            because: "4 distinct MVs < threshold 5 → discard fires");
    }

    // -----------------------------------------------------------------------
    // Resolve — discard skipped (graveyard ≥ 5 distinct MVs)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_ExactlyFiveDistinctMvsInGraveyard_DiscardsNothing_NetPlusTwo()
    {
        // Graveyard: exactly 5 distinct MVs → discard is skipped.
        SeedGraveyardCard("G0", "");       // MV 0
        SeedGraveyardCard("G1", "{W}");    // MV 1
        SeedGraveyardCard("G2", "{1}{U}"); // MV 2
        SeedGraveyardCard("G3", "{2}{B}"); // MV 3
        SeedGraveyardCard("G4", "{3}{R}"); // MV 4
        SeedHandCard("H1", "{1}");
        SeedLibraryCards(4);

        var handBefore = _alice.Zones.Hand.GetCards().Count();
        ExecuteResolve();

        _alice.Zones.Hand.GetCards().Should().HaveCount(handBefore + 2,
            because: "exactly 5 distinct MVs satisfies the unless-clause → no discard");
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(5,
            because: "only the original graveyard cards — nothing additional discarded");
    }

    [Fact]
    public void Resolve_SixDistinctMvsInGraveyard_DiscardsNothing_NetPlusTwo()
    {
        // Graveyard: 6 distinct MVs (well above threshold).
        for (var mv = 0; mv <= 5; mv++)
        {
            var cost = mv == 0 ? "" : $"{{{mv}}}";
            SeedGraveyardCard($"G{mv}", cost);
        }
        SeedHandCard("H1", "{G}");
        SeedLibraryCards(3);

        var handBefore = _alice.Zones.Hand.GetCards().Count();
        ExecuteResolve();

        _alice.Zones.Hand.GetCards().Should().HaveCount(handBefore + 2,
            because: "6 distinct MVs ≥ threshold 5 → discard skipped");
    }

    // -----------------------------------------------------------------------
    // Edge cases
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_EmptyLibrary_FlagsSbaLoss_DiscardStillRuns()
    {
        // Library empty; hand has 1 card; graveyard empty → discard should fire.
        SeedHandCard("H1", "{1}");
        // No library cards.

        Action act = () => ExecuteResolve();
        act.Should().NotThrow();

        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            because: "drawing from empty library stamps CR 704.5b SBA flag");
        // Drew 0 (empty library), discarded 1 → hand now empty.
        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            because: "the one hand card was discarded (graveyard empty, <5 MVs)");
        _alice.Zones.Graveyard.GetCards().Should().ContainSingle();
    }

    [Fact]
    public void Resolve_EmptyHandAfterDraw_NoDiscardError()
    {
        // Pathological case: hand starts empty, graveyard empty.
        // Draws 2, then would need to discard but hand has those 2 draws.
        // This just confirms no exception; the 2 drawn cards may be discarded.
        SeedLibraryCards(2);

        Action act = () => ExecuteResolve();
        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // GraveyardDistinctMvCount helper
    // -----------------------------------------------------------------------

    [Fact]
    public void GraveyardDistinctMvCount_EmptyGraveyard_ReturnsZero()
    {
        TaintedIndulgenceFactory.GraveyardDistinctMvCount(_alice).Should().Be(0);
    }

    [Fact]
    public void GraveyardDistinctMvCount_MultipleSameMvCards_CountsOnce()
    {
        // Three cards all with MV 2 → distinct count = 1.
        SeedGraveyardCard("A", "{1}{U}");
        SeedGraveyardCard("B", "{U}{1}");
        SeedGraveyardCard("C", "{2}");

        TaintedIndulgenceFactory.GraveyardDistinctMvCount(_alice).Should().Be(1);
    }

    [Fact]
    public void GraveyardDistinctMvCount_LandAndZeroCostCard_CountsAsOneMvZero()
    {
        // Land (no mana cost → MV 0) and a 0-cost card both yield MV 0 → 1 distinct.
        SeedGraveyardCard("Land", "");  // no mana cost → MV 0
        SeedGraveyardCard("Zero", "{0}"); // explicit {0} → MV 0

        TaintedIndulgenceFactory.GraveyardDistinctMvCount(_alice).Should().Be(1,
            because: "both cards have MV 0; distinct count = 1 (CR 202.3)");
    }

    [Fact]
    public void GraveyardDistinctMvCount_FiveDistinctMvs_ReturnsThreshold()
    {
        SeedGraveyardCard("MV0", "");
        SeedGraveyardCard("MV1", "{U}");
        SeedGraveyardCard("MV2", "{1}{B}");
        SeedGraveyardCard("MV3", "{2}{U}");
        SeedGraveyardCard("MV4", "{3}{B}");

        TaintedIndulgenceFactory.GraveyardDistinctMvCount(_alice)
            .Should().Be(TaintedIndulgenceFactory.UnlessThreshold);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void ExecuteResolve()
    {
        foreach (var e in TaintedIndulgenceFactory.BuildResolveEffect(_alice))
            e.Execute();
    }

    private ICard SeedHandCard(string name, string manaCost)
    {
        var c = new Card(name, manaCost);
        c.SetOwner(_alice);
        c.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(c);
        return c;
    }

    private void SeedLibraryCards(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Card($"Lib{i}", "{1}");
            c.SetOwner(_alice);
            c.SetZone(ZoneType.Library);
            _alice.Zones.Library.AddCard(c);
        }
    }

    private void SeedGraveyardCard(string name, string manaCost)
    {
        var c = new Card(name, manaCost);
        c.SetOwner(_alice);
        c.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(c);
    }
}
