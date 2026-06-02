using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FaithfulMendingFactory"/>.
///
/// Card: Faithful Mending — Instant {W}{U} (Innistrad: Midnight Hunt).
///   "You gain 2 life, draw two cards, then discard two cards.
///    Flashback {1}{W}{U}."
///
/// Covers:
///   - Identity + <see cref="NamedCardFactory"/> dispatch.
///   - Flashback alt-cost surfaced as {1}{W}{U} (MV 3) via the oracle
///     binder (<see cref="FlashbackOracleParser"/>).
///   - Resolve: gain 2 life, draw 2, discard 2 in that order; net hand
///     size unchanged when hand had ≥2 starting cards.
///   - Flashback cast: same resolve effect; cost <c>OnResolved</c> exiles
///     the card from graveyard (CR 702.33b).
///   - Empty library: draws what's available, then discards from resulting
///     hand.
/// </summary>
[Trait("Color", "M")]
public class FaithfulMendingFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FaithfulMending_Identity()
    {
        var c = FaithfulMendingFactory.Create(_alice);

        c.Name.Should().Be("Faithful Mending");
        c.ManaCost.Should().Be("{W}{U}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FaithfulMending_ManaValue_Is2()
    {
        var c = FaithfulMendingFactory.Create(_alice);

        // {W}{U} = 2 generic pip-equivalents — mana value 2.
        c.ManaCostValue.TotalValue.Should().Be(2);
    }
    // -----------------------------------------------------------------------
    // Flashback cost: {1}{W}{U}, mana value 3
    // -----------------------------------------------------------------------

    [Fact]
    public void FlashbackCost_ParsedFromOracle_Is1WU()
    {
        var fb = FaithfulMendingFactory.BuildFlashbackCost();

        fb.AlternativeManaCost.Should().Be(ManaCost.Parse("1WU"));
        fb.Description.Should().Contain("Flashback");
    }

    [Fact]
    public void FlashbackCost_ManaValue_Is3()
    {
        var fb = FaithfulMendingFactory.BuildFlashbackCost();

        // {1}{W}{U} = 1 + 1 + 1 = 3.
        fb.AlternativeManaCost.TotalValue.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Resolve: gain 2 life, draw 2, discard 2 (net hand size unchanged)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_Gains2Life_Draws2_Discards2_InOrder()
    {
        // Starting hand: 2 cards. Library: 3 cards. Life: 20.
        var inHand1 = SeedHandCard(_alice, "Hand1");
        var inHand2 = SeedHandCard(_alice, "Hand2");
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        _ = SeedLibraryCard(_alice, "Top3");

        var effects = FaithfulMendingFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        // Life gain happens first: 20 + 2 = 22.
        _alice.LifeTotal.Should().Be(22);

        // Net hand size: 2 starting + 2 drawn - 2 discarded = 2.
        _alice.Zones.Hand.GetCards().Should().HaveCount(2);

        // Deterministic v1 discard picks last-2-in-hand (the freshly drawn
        // cards top1, top2) — original hand cards remain.
        _alice.Zones.Hand.GetCards().Should().Contain(new[] { inHand1, inHand2 });
        _alice.Zones.Graveyard.GetCards().Should().Contain(new[] { top1, top2 });

        top1.Zone.Should().Be(ZoneType.Graveyard);
        top2.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Resolve_FromEmptyHand_DrawsTwoThenDiscardsThoseTwo()
    {
        // No starting hand. Library: 2 cards.
        // After gain-2-life: draw top1+top2 into hand; discard both.
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");

        var effects = FaithfulMendingFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.LifeTotal.Should().Be(22, "2 life gained regardless of hand/library state");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(new[] { top1, top2 });
    }

    // -----------------------------------------------------------------------
    // Flashback cast: from graveyard, pays {1}{W}{U}, then exiles (CR 702.33b)
    // -----------------------------------------------------------------------

    [Fact]
    public void FlashbackCast_FromGraveyard_AppliesResolveEffect_ThenExiles()
    {
        // Faithful Mending is in Alice's graveyard (cast via flashback).
        var fm = FaithfulMendingFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(fm);
        fm.SetZone(ZoneType.Graveyard);

        var top1 = SeedLibraryCard(_alice, "FBTop1");
        var top2 = SeedLibraryCard(_alice, "FBTop2");

        // Flashback cost must be castable from graveyard.
        var fb = FaithfulMendingFactory.BuildFlashbackCost();
        fb.CanCastFor(fm, _alice).Should().BeTrue();
        fb.AlternativeManaCost.Should().Be(ManaCost.Parse("1WU"));

        // Run the printed resolve effect (same body for both cast paths —
        // CR 702.33a; cost is the only difference).
        foreach (var e in FaithfulMendingFactory.BuildResolveEffect(_alice)) e.Execute();

        // Then flashback's post-resolve hook fires — card exiles from
        // graveyard (CR 702.33b). Simulate the same wrap SpellCastFlow does
        // in production by invoking OnResolved directly.
        fb.OnResolved(fm, _alice);

        fm.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(fm);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(fm);

        // Resolve side-effect hit: both top-of-library cards drew into hand
        // and were then discarded (Alice started with no other hand cards).
        _alice.Zones.Graveyard.GetCards().Should().Contain(new[] { top1, top2 });
        _alice.LifeTotal.Should().Be(22);
    }

    [Fact]
    public void FlashbackCost_CannotCast_FromHandOrBattlefield()
    {
        // CR 702.33 — flashback is only castable from graveyard.
        var fm = FaithfulMendingFactory.Create(_alice);
        fm.SetZone(ZoneType.Hand);

        var fb = FaithfulMendingFactory.BuildFlashbackCost();
        fb.CanCastFor(fm, _alice).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Empty library: draw what's possible, discard from resulting hand.
    // -----------------------------------------------------------------------

    [Fact]
    public void EmptyLibrary_DrawsWhatsAvailable_AndDiscardsFromResultingHand()
    {
        // Starting hand has one card; library has only one card. The first
        // draw lands; the second draw hits empty and flags the SBA loss.
        // Then "discard two" pulls both cards now in hand into the graveyard.
        var inHand = SeedHandCard(_alice, "InHand");
        var only = SeedLibraryCard(_alice, "OnlyTop");

        var effects = FaithfulMendingFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "the second draw hit an empty library — SBA flag must be set");

        // Both the original hand card and the single drawn card were
        // discarded (only two cards were ever in hand for the discard step).
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(new[] { inHand, only });
        _alice.LifeTotal.Should().Be(22, "life is gained even when library is short");
    }

    [Fact]
    public void EmptyLibrary_EmptyHand_DiscardIsNoOp_AndFlagsSbaLoss()
    {
        // No library, no hand — both draws underflow and the discard step
        // has nothing to pick. Should not throw; SBA flag must be set.
        var effects = FaithfulMendingFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
        _alice.LifeTotal.Should().Be(22, "2 life gained regardless");
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
