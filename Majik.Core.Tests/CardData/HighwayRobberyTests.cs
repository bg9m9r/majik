using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="HighwayRobberyFactory"/>.
///
/// Highway Robbery (Outlaws of Thunder Junction, {1}{R}):
///   Sorcery. You may discard a card or sacrifice a land. If you do, draw
///   two cards. Plot {1}{R}.
///
/// Same discard-then-draw looter family as
/// <see cref="TormentingVoiceFactory"/> / <see cref="ThrillOfPossibilityFactory"/>,
/// but the cost is an OPTIONAL choice between discarding a card OR sacrificing
/// a land, and the draw is conditional ("If you do" — CR 608.2j).
///
/// Plot rider deferred (CR 718 not yet an engine primitive — same posture as
/// <see cref="SlickshotShowOffFactory"/>).
///
/// Covers (the card's UNIQUE behaviour):
///   - Card identity (Sorcery, {1}{R}).
///   - Discard arm: hand non-empty → discard last card, draw 2.
///   - Sacrifice arm: empty hand + a land on battlefield → sacrifice the
///     land, draw 2.
///   - Decline: empty hand + no land → no payment, no draw (conditional).
///   - Empty library: draws what's available, SBA flag set (CR 704.5b).
/// </summary>
[Trait("Color", "R")]
public class HighwayRobberyTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void HighwayRobbery_Identity()
    {
        var c = HighwayRobberyFactory.Create(_alice);

        c.Name.Should().Be("Highway Robbery");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_DiscardArm_DiscardsCard_ThenDrawsTwo()
    {
        // Hand: 1 card. Library: 3 cards. Deterministic v1 prefers the
        // discard arm. Net hand: 1 - 1 discarded + 2 drawn = 2.
        var inHand = SeedHandCard(_alice, "Hand1");
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        var top3 = SeedLibraryCard(_alice, "Top3");

        var effects = HighwayRobberyFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(2)
            .And.Contain(new[] { top1, top2 });
        _alice.Zones.Graveyard.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(inHand);
        _alice.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(top3);
        inHand.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Resolve_SacrificeArm_EmptyHand_SacrificesLand_ThenDrawsTwo()
    {
        // Empty hand → deterministic v1 falls to the sacrifice-a-land arm.
        var land = SeedBattlefieldLand(_alice, "Mountain");
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");

        var effects = HighwayRobberyFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(land);
        land.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Hand.GetCards().Should().HaveCount(2)
            .And.Contain(new[] { top1, top2 });
    }

    [Fact]
    public void Resolve_CannotPay_NoDiscardNoLand_NoDraw()
    {
        // Empty hand + no land → the optional cost can't be paid; "If you do"
        // (CR 608.2j) gates the draw, so no draw happens.
        SeedLibraryCard(_alice, "Top1");
        SeedLibraryCard(_alice, "Top2");

        var effects = HighwayRobberyFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void Resolve_EmptyLibrary_DrawsWhatsAvailable_AndFlagsSbaLoss()
    {
        // Hand: 1. Library: 1. Discard the card, draw the only library card,
        // second draw hits empty → SBA flag set (CR 704.5b).
        SeedHandCard(_alice, "Hand1");
        var only = SeedLibraryCard(_alice, "Only");

        var effects = HighwayRobberyFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "second draw hit an empty library — SBA flag must be set");
        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(only);
        _alice.Zones.Graveyard.GetCards().Should().ContainSingle();
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

    private static Land SeedBattlefieldLand(Player p, string name)
    {
        var land = new Land(name);
        land.SetOwner(p);
        land.SetController(p);
        p.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        return land;
    }
}
