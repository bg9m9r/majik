using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="FaithlessLootingFactory"/>.
///
/// Card: Faithless Looting — Sorcery {R} (Innistrad / Modern Horizons).
///   "Draw two cards, then discard two cards.
///    Flashback {2}{R}."
///
/// Covers:
///   - Identity + <see cref="NamedCardFactory"/> dispatch.
///   - Flashback alt-cost surfaced as {2}{R} via the oracle binder
///     (<see cref="FlashbackOracleParser"/>).
///   - Resolve: draw 2 + discard 2; net hand size unchanged when the
///     hand had ≥2 starting cards.
///   - Flashback cast: same resolve effect; cost <c>OnResolved</c> exiles
///     the card from graveyard (CR 702.34b).
///   - Empty library: draws what's available, then discards from the
///     resulting (possibly smaller) hand.
/// </summary>
public class FaithlessLootingTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FaithlessLooting_Identity()
    {
        var c = FaithlessLootingFactory.Create(_alice);

        c.Name.Should().Be("Faithless Looting");
        c.ManaCost.Should().Be("{R}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FaithlessLooting()
    {
        var card = NamedCardFactory.Create("Faithless Looting", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Faithless Looting");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FlashbackCost_ParsedFromOracle_Is2R()
    {
        var fb = FaithlessLootingFactory.BuildFlashbackCost();

        fb.AlternativeManaCost.Should().Be(ManaCost.Parse("2R"));
        fb.Description.Should().Contain("Flashback");
    }

    // -----------------------------------------------------------------------
    // Resolve: draw 2, then discard 2
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsTwo_ThenDiscardsTwo_NetHandSizeUnchanged()
    {
        // Starting hand: 2 cards. Library: 3 cards.
        var inHand1 = SeedHandCard(_alice, "Hand1");
        var inHand2 = SeedHandCard(_alice, "Hand2");
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        var top3 = SeedLibraryCard(_alice, "Top3");

        var effects = FaithlessLootingFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        // Net hand size: 2 starting + 2 drawn - 2 discarded = 2.
        _alice.Zones.Hand.GetCards().Should().HaveCount(2);

        // The two fresh top-of-library draws (top1, top2) entered hand.
        // Deterministic v1 discard picks the last two cards in hand (the
        // freshly drawn cards) — so post-resolve the original two cards
        // remain in hand and the two drawn cards land in the graveyard.
        _alice.Zones.Hand.GetCards().Should().Contain(new[] { inHand1, inHand2 });
        _alice.Zones.Graveyard.GetCards().Should().Contain(new[] { top1, top2 });

        // Library lost exactly two cards off the top.
        _alice.Zones.Library.GetCards().Should().ContainSingle().Which.Should().BeSameAs(top3);

        top1.Zone.Should().Be(ZoneType.Graveyard);
        top2.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Resolve_FromEmptyHand_DrawsTwoThenDiscardsThoseTwo()
    {
        // No starting hand. Library: 2 cards. Net: drew 2 then discarded
        // both — hand ends empty, both drawn cards land in graveyard.
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");

        var effects = FaithlessLootingFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(new[] { top1, top2 });
    }

    // -----------------------------------------------------------------------
    // Flashback cast: from graveyard, paying {2}{R}, then exile.
    // -----------------------------------------------------------------------

    [Fact]
    public void FlashbackCast_FromGraveyard_AppliesResolveEffect_ThenExiles()
    {
        // Faithless Looting is in Alice's graveyard (cast from grave via
        // flashback alt-cost).
        var fl = FaithlessLootingFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(fl);
        fl.SetZone(ZoneType.Graveyard);

        // Library has the two cards we'll draw.
        var top1 = SeedLibraryCard(_alice, "FBTop1");
        var top2 = SeedLibraryCard(_alice, "FBTop2");

        // Sanity: flashback cost legal here.
        var fb = FaithlessLootingFactory.BuildFlashbackCost();
        fb.CanCastFor(fl, _alice).Should().BeTrue();
        fb.AlternativeManaCost.Should().Be(ManaCost.Parse("2R"));

        // Run the printed resolve effect — same effect for printed cast and
        // flashback cast (CR 702.34a; the cost is the only difference).
        foreach (var e in FaithlessLootingFactory.BuildResolveEffect(_alice)) e.Execute();

        // Then flashback's post-resolve hook fires — card exiles from
        // graveyard (CR 702.34b). Simulate the same wrap SpellCastFlow
        // does in production by invoking OnResolved directly.
        fb.OnResolved(fl, _alice);

        fl.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(fl);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(fl);

        // Resolve side-effect still hit: both top-of-library cards drew
        // into hand and were then discarded into the graveyard (Alice
        // started with no other hand cards).
        _alice.Zones.Graveyard.GetCards().Should().Contain(new[] { top1, top2 });
        top1.Zone.Should().Be(ZoneType.Graveyard);
        top2.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void FlashbackCost_CannotCast_FromHandOrBattlefield()
    {
        // CR 702.34 — flashback is only castable from graveyard.
        var fl = FaithlessLootingFactory.Create(_alice);
        fl.SetZone(ZoneType.Hand);

        var fb = FaithlessLootingFactory.BuildFlashbackCost();
        fb.CanCastFor(fl, _alice).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Empty library: draw what's possible, discard from new hand.
    // -----------------------------------------------------------------------

    [Fact]
    public void EmptyLibrary_DrawsWhatsAvailable_AndDiscardsFromResultingHand()
    {
        // Starting hand has one card; library has only one card. The first
        // draw lands; the second draw hits empty and flags the SBA loss.
        // Then "discard two" pulls the two cards now in hand (originally
        // 1 + 1 drawn) into the graveyard. Hand ends empty.
        var inHand = SeedHandCard(_alice, "InHand");
        var only = SeedLibraryCard(_alice, "OnlyTop");

        var effects = FaithlessLootingFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "the second draw hit an empty library — SBA flag must be set");

        // Both the original hand card and the single drawn card got
        // discarded (only two cards were ever in hand for the discard
        // step to pick from).
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(new[] { inHand, only });
        only.Zone.Should().Be(ZoneType.Graveyard);
        inHand.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void EmptyLibrary_EmptyHand_DiscardIsNoOp_AndFlagsSbaLoss()
    {
        // No library, no hand — both draws underflow and the discard step
        // has nothing to pick. Should not throw; SBA flag must be set.
        var effects = FaithlessLootingFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

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
