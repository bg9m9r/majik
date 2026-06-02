using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="WildGuessFactory"/> — Sorcery {R}{R}
/// (Time Spiral / reprints).
///
/// "As an additional cost to cast this spell, discard a card.
///  Draw two cards."
///
/// Covers:
///   - Identity (Sorcery, {R}{R}, owner / controller) + NamedCardFactory
///     dispatch (built from the embedded JSON definition).
///   - SpellDefinition shape: <see cref="DiscardACardAdditionalCost"/>
///     additional cost (CR 601.2f), no modes, no X, no target requests.
///   - Resolve: caster draws two cards (CR 121.1).
///   - Resolve: empty library mid-draw → draws what's available, SBA loss
///     flag set (CR 704.5b).
///   - Cost discards the first card in hand and is unpayable with an empty
///     hand (CR 117.1 / CR 601.2g).
/// </summary>
public class WildGuessFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var card = WildGuessFactory.Create(_alice);

        card.Name.Should().Be("Wild Guess");
        card.ManaCost.Should().Be("{R}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_WildGuess()
    {
        var card = NamedCardFactory.Create("Wild Guess", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Wild Guess");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{R}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresDiscardACardCost_NoTargets()
    {
        var def = WildGuessFactory.BuildSpellDefinition(_alice);

        def.AdditionalCostsOrEmpty.Should().ContainSingle()
            .Which.Should().BeOfType<DiscardACardAdditionalCost>(
                "Wild Guess prints 'As an additional cost to cast this spell, discard a card.' (CR 601.2f)");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty("Wild Guess has no targets");
    }

    // -----------------------------------------------------------------------
    // Resolve — draw two cards
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsTwoCards()
    {
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        SeedLibraryCard(_alice, "Top3");

        foreach (var e in WildGuessFactory.BuildResolveEffect(_alice)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(2)
            .And.Contain(new[] { top1, top2 });
        _alice.Zones.Library.GetCards().Should().ContainSingle();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void Resolve_EmptyLibraryMidDraw_DrawsWhatsAvailable_FlagsSbaLoss()
    {
        var only = SeedLibraryCard(_alice, "Only");

        foreach (var e in WildGuessFactory.BuildResolveEffect(_alice)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(only);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "second draw hit an empty library — SBA flag must be set (CR 704.5b)");
    }

    // -----------------------------------------------------------------------
    // Cost: discard a card (CR 601.2f)
    // -----------------------------------------------------------------------

    [Fact]
    public void Cost_DiscardsFirstCardInHand()
    {
        var spareCard = new Instant("Bogus Spell", "{R}");
        spareCard.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(spareCard);
        spareCard.SetZone(ZoneType.Hand);

        var cost = new DiscardACardAdditionalCost();
        cost.CanPay(_alice).Should().BeTrue();
        cost.Pay(_alice).Should().BeTrue();

        cost.Discarded.Should().BeSameAs(spareCard);
        spareCard.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Cost_CanPay_FalseWhenEmptyHand()
    {
        var cost = new DiscardACardAdditionalCost();
        cost.CanPay(_alice).Should().BeFalse(
            "the discard is mandatory and the hand is empty (CR 117.1 / CR 601.2g)");
    }

    [Fact]
    public void Cost_DiscardsNominatedTargetWhenSet()
    {
        var keep = new Instant("Keep", "{R}");
        keep.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(keep);
        keep.SetZone(ZoneType.Hand);

        var pitch = new Instant("Pitch", "{R}");
        pitch.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(pitch);
        pitch.SetZone(ZoneType.Hand);

        var cost = new DiscardACardAdditionalCost { Target = pitch };
        cost.Pay(_alice).Should().BeTrue();

        cost.Discarded.Should().BeSameAs(pitch);
        pitch.Zone.Should().Be(ZoneType.Graveyard);
        keep.Zone.Should().Be(ZoneType.Hand, "only the nominated card is discarded");
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
