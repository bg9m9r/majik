using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Hogaak, Arisen Necropolis (Modern Horizons, {B}{B}{G}{G}, 8/8).
///
/// Coverage:
/// - Identity (name / type / P/T / Legendary supertype / Avatar subtype /
///   mana cost) + NamedCardFactory dispatch.
/// - Trample + Convoke keyword markers wired (CR 702.19 + 702.51).
/// - Convoke alt-cost surfaced via BuildAlternativeCost (mirrors Chord of
///   Calling); ReduceCost trims pips per tapped creature.
/// - Additional cost (exile 2 creature cards from controller's graveyard,
///   CR 601.2f) — CanPay gates on creature-card count; Pay moves the
///   first two creature cards from graveyard to exile and surfaces them
///   on Exiled.
/// </summary>
public class HogaakFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Hogaak_Identity()
    {
        var c = HogaakFactory.Create(_alice);

        c.Name.Should().Be("Hogaak, Arisen Necropolis");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Hogaak is a Legendary Creature (CR 205.4a)");
        c.HasSubtype(CardSubtype.Avatar).Should().BeTrue(
            "Hogaak is an Avatar (CR 205.3m)");
        c.Power.Should().Be(8);
        c.Toughness.Should().Be(8);
        c.ManaCost.Should().Be("{B}{B}{G}{G}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Hogaak_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Hogaak, Arisen Necropolis", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Hogaak, Arisen Necropolis");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Avatar).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Keywords — Trample + Convoke
    // -----------------------------------------------------------------------

    [Fact]
    public void Hogaak_HasTrampleAndConvokeKeywords()
    {
        var c = HogaakFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();

        keywords.Should().Contain("Trample", "CR 702.19 — Trample is printed");
        keywords.Should().Contain("Convoke", "CR 702.51 — Convoke is printed");

        CombatAbilities.HasTrample(c).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Convoke alt-cost surface (parallels Chord of Calling)
    // -----------------------------------------------------------------------

    [Fact]
    public void Hogaak_BuildAlternativeCost_SurfacesPrintedCost_AndReducesByTappedCreatures()
    {
        var convoke = HogaakFactory.BuildAlternativeCost();
        convoke.Description.Should().Be("Convoke");
        convoke.AlternativeManaCost.Should().Be(ManaCost.Parse("BBGG"));

        var bear1 = new Creature("Bear", "1G", 2, 2);
        var bear2 = new Creature("Bear", "1G", 2, 2);
        var reduced = ConvokeAlternativeCost.ReduceCost(
            convoke.AlternativeManaCost, new[] { bear1, bear2 });

        // Printed cost has 0 generic + 2 black + 2 green pips. Two taps
        // (no generic to peel first) consume two pips in WUBRG order —
        // both blacks first, leaving 0 black + 2 green.
        reduced.Black.Should().Be(0);
        reduced.Green.Should().Be(2);
        reduced.Generic.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Additional cost — exile two creature cards from graveyard (CR 601.2f)
    // -----------------------------------------------------------------------

    [Fact]
    public void Hogaak_AdditionalCost_CanPay_RequiresTwoCreatureCardsInGraveyard()
    {
        var cost = HogaakFactory.BuildExileTwoCreaturesAdditionalCost();

        // Empty graveyard → CanPay is false.
        cost.CanPay(_alice).Should().BeFalse();

        // One creature card → still false (cost requires two).
        var bear = new Creature("Bear", "1G", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);
        cost.CanPay(_alice).Should().BeFalse();

        // Two creature cards → true.
        var elf = new Creature("Llanowar Elf", "G", 1, 1);
        elf.SetOwner(_alice);
        elf.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(elf);
        elf.SetZone(ZoneType.Graveyard);
        cost.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void Hogaak_AdditionalCost_Pay_ExilesTwoCreaturesFromGraveyardToExile()
    {
        var cost = HogaakFactory.BuildExileTwoCreaturesAdditionalCost();

        var bear = new Creature("Bear", "1G", 2, 2);
        var elf = new Creature("Llanowar Elf", "G", 1, 1);
        var giant = new Creature("Giant", "4RR", 4, 4);
        foreach (var c in new[] { bear, elf, giant })
        {
            c.SetOwner(_alice);
            c.SetController(_alice);
            _alice.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }

        cost.Pay(_alice).Should().BeTrue();

        // First two creature cards exiled (deterministic v1 — insertion
        // order in the graveyard). Third creature card remains.
        cost.Exiled.Should().HaveCount(2);
        cost.Exiled.Should().Contain(new ICard[] { bear, elf });

        _alice.Zones.Exile.GetCards().Should()
            .Contain(new ICard[] { bear, elf },
                "the exile payment moves the cards graveyard → exile");
        _alice.Zones.Graveyard.GetCards().Should().Contain(giant,
            "only the first two creature cards were exiled");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(new ICard[] { bear, elf });

        bear.Zone.Should().Be(ZoneType.Exile);
        elf.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Hogaak_AdditionalCost_Pay_ReturnsFalseWhenInsufficientCreatures()
    {
        var cost = HogaakFactory.BuildExileTwoCreaturesAdditionalCost();

        // Single creature card — Pay should refuse and not mutate state.
        var bear = new Creature("Bear", "1G", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        cost.Pay(_alice).Should().BeFalse();
        cost.Exiled.Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(bear,
            "failed payment must not mutate the graveyard");
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
    }
}
