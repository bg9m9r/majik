using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="SeizeTheSpoilsFactory"/> — Sorcery {2}{R}.
///
/// "As an additional cost to cast this spell, discard a card.
///  Draw two cards and create a Treasure token."
///
/// Covers (the card's UNIQUE behaviour + a single identity assert):
///   - Identity (Sorcery, {2}{R}, red, owner / controller).
///   - SpellDefinition shape: <see cref="DiscardACardAdditionalCost"/>
///     additional cost (CR 601.2f), no modes, no X, no target requests.
///   - Resolve: caster draws two cards (CR 121.1) AND creates one Treasure
///     token (CR 111.10).
///   - Resolve: empty library mid-draw → draws what's available, SBA loss
///     flag set (CR 704.5b); the Treasure is still created.
///   - Cost: discards a card from hand; CanPay false with an empty hand
///     (CR 601.2g / 117.1).
///
/// (Dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests — no per-card dispatch test here.)
/// </summary>
[Trait("Color", "R")]
public class SeizeTheSpoilsTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeManaCost_AndRed()
    {
        var card = SeizeTheSpoilsFactory.Create(_alice);

        card.Name.Should().Be("Seize the Spoils");
        card.ManaCost.Should().Be("{2}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(Majik.Core.ValueObjects.ManaColor.Red,
            "the {R} pip makes it red");
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresDiscardCost_NoTargets()
    {
        var def = SeizeTheSpoilsFactory.BuildSpellDefinition(_alice);

        def.AdditionalCostsOrEmpty.Should().ContainSingle()
            .Which.Should().BeOfType<DiscardACardAdditionalCost>(
                "Seize the Spoils prints 'As an additional cost to cast this spell, discard a card.' (CR 601.2f)");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty("Seize the Spoils has no targets");
    }

    // -----------------------------------------------------------------------
    // Resolve — draw two cards + create a Treasure
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsTwoCards_AndCreatesTreasure()
    {
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        SeedLibraryCard(_alice, "Top3");

        foreach (var e in SeizeTheSpoilsFactory.BuildResolveEffect(_alice)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(2)
            .And.Contain(new[] { top1, top2 });
        _alice.Zones.Library.GetCards().Should().ContainSingle();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();

        _alice.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Should().ContainSingle(p => p.HasSubtype(CardSubtype.Treasure),
                "Seize the Spoils creates one Treasure token (CR 111.10)");
    }

    [Fact]
    public void Resolve_EmptyLibraryMidDraw_StillCreatesTreasure_FlagsSbaLoss()
    {
        var only = SeedLibraryCard(_alice, "Only");

        foreach (var e in SeizeTheSpoilsFactory.BuildResolveEffect(_alice)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(only);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "second draw hit an empty library — SBA flag must be set (CR 704.5b)");

        _alice.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Should().ContainSingle(p => p.HasSubtype(CardSubtype.Treasure),
                "the Treasure is still created even when the draw runs the library dry");
    }

    // -----------------------------------------------------------------------
    // Cost: discards a card from hand
    // -----------------------------------------------------------------------

    [Fact]
    public void Cost_DiscardsCardFromHand_WhenAvailable()
    {
        var pitch = new Card("Mountain", "");
        pitch.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(pitch);
        pitch.SetZone(ZoneType.Hand);

        var cost = new DiscardACardAdditionalCost();
        cost.CanPay(_alice).Should().BeTrue();
        cost.Pay(_alice).Should().BeTrue();

        cost.Discarded.Should().BeSameAs(pitch);
        pitch.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Cost_CanPay_FalseWhenHandEmpty()
    {
        var cost = new DiscardACardAdditionalCost();
        cost.CanPay(_alice).Should().BeFalse(
            "an empty hand cannot pay the mandatory discard (CR 601.2g / 117.1)");
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
