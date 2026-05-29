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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="OnslaughtCyclingLandFactory"/> — the Onslaught
/// monocolour cycling-land cycle (Tranquil Thicket, Lonely Sandbar; the
/// remaining three members slot in via attribute additions without
/// changing the body).
///
/// Covers:
/// - Identity per cycle member (Land + correct printed subtype +
///   produced colour).
/// - Cycling ability shape (ManaCostCost + DiscardSelfCost via the shared
///   <see cref="Majik.Core.Keywords.CyclingFactory"/> primitive).
/// - Cycling cost charges the correct colour.
/// - End-to-end cycle: pays {color}, discards self, draws one card,
///   publishes <see cref="Majik.Core.Events.CardCycledEvent"/> when a bus
///   is supplied.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
public class OnslaughtCyclingLandFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    public static IEnumerable<object[]> AllCyclingLands => new[]
    {
        new object[] { "Tranquil Thicket", "G", CardSubtype.Forest },
        new object[] { "Lonely Sandbar",   "U", CardSubtype.Island },
        new object[] { "Barren Moor",      "B", CardSubtype.Swamp },
    };

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllCyclingLands))]
    public void CyclingLand_Dispatch_ReturnsLandWithExpectedSubtype(
        string cardName, string _color, CardSubtype expectedSubtype)
    {
        _ = _color;

        var card = NamedCardFactory.Create(cardName, _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be(cardName);
        card.HasSubtype(expectedSubtype).Should().BeTrue(
            "the printed land subtype");
    }

    [Theory]
    [MemberData(nameof(AllCyclingLands))]
    public void CyclingLand_HasManaAbilityProducingExpectedColor(
        string cardName, string color, CardSubtype _subtype)
    {
        _ = _subtype;

        var land = (Land)NamedCardFactory.Create(cardName, _alice);
        var mana = land.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;

        switch (color)
        {
            case "W": mana.ManaGenerated.White.Should().Be(1); break;
            case "U": mana.ManaGenerated.Blue.Should().Be(1); break;
            case "B": mana.ManaGenerated.Black.Should().Be(1); break;
            case "R": mana.ManaGenerated.Red.Should().Be(1); break;
            case "G": mana.ManaGenerated.Green.Should().Be(1); break;
        }
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllCyclingLands))]
    public void CyclingLand_HasCyclingActivatedAbility_WithManaAndDiscardSelfCosts(
        string cardName, string color, CardSubtype _subtype)
    {
        _ = _subtype;

        var land = (Land)NamedCardFactory.Create(cardName, _alice);
        var cycling = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        ManaForColor(manaCost, color).Should().Be(1,
            $"cycling {{{color}}} charges 1 {{{color}}} mana");
    }

    [Theory]
    [MemberData(nameof(AllCyclingLands))]
    public void CyclingLand_HasCyclingKeywordMarker(
        string cardName, string _color, CardSubtype _subtype)
    {
        _ = _color; _ = _subtype;

        var land = (Land)NamedCardFactory.Create(cardName, _alice);
        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // End-to-end cycling — pays mana, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void TranquilThicket_Cycling_EndToEnd_PaysGreenDiscardsSelfDrawsOne()
    {
        // Seed library so the draw resolves.
        var topCard = new Card("Llanowar Elves", "{G}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var thicket = OnslaughtCyclingLandFactory.Create(
            _alice,
            new[] { "Tranquil Thicket", "G", "Forest" },
            eventBus: bus,
            replacements: null);
        _alice.Zones.Hand.AddCard(thicket);
        thicket.SetZone(ZoneType.Hand);

        _alice.AddManaToPool(ManaCost.Parse("G"));

        var cycling = thicket.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        thicket.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(thicket);
    }

    [Fact]
    public void LonelySandbar_Cycling_ChargesBlueManaSpecifically()
    {
        var sandbar = (Land)NamedCardFactory.Create("Lonely Sandbar", _alice);
        _alice.Zones.Hand.AddCard(sandbar);
        sandbar.SetZone(ZoneType.Hand);

        var cycling = sandbar.Abilities.OfType<ActivatedAbility>().Single();
        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;

        mana.Blue.Should().Be(1, "Lonely Sandbar's cycling cost is {U}");
        mana.Green.Should().Be(0);
        mana.Generic.Should().Be(0);
    }

    [Fact]
    public void BarrenMoor_Cycling_ChargesBlackManaSpecifically()
    {
        var moor = (Land)NamedCardFactory.Create("Barren Moor", _alice);
        _alice.Zones.Hand.AddCard(moor);
        moor.SetZone(ZoneType.Hand);

        var cycling = moor.Abilities.OfType<ActivatedAbility>().Single();
        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;

        mana.Black.Should().Be(1, "Barren Moor's cycling cost is {B}");
        mana.Green.Should().Be(0);
        mana.Generic.Should().Be(0);
    }

    [Fact]
    public void BarrenMoor_Cycling_EndToEnd_PaysBlackDiscardsSelfDrawsOne()
    {
        // Seed library so the draw resolves.
        var topCard = new Card("Dark Ritual", "{B}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var moor = OnslaughtCyclingLandFactory.Create(
            _alice,
            new[] { "Barren Moor", "B", "Swamp" },
            eventBus: bus,
            replacements: null);
        _alice.Zones.Hand.AddCard(moor);
        moor.SetZone(ZoneType.Hand);

        _alice.AddManaToPool(ManaCost.Parse("B"));

        var cycling = moor.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        moor.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(moor);
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_ThrowsOnShortArgs()
    {
        var act = () => OnslaughtCyclingLandFactory.Create(_alice, new[] { "Tranquil Thicket" });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_ThrowsOnUnknownLandSubtype()
    {
        var act = () => OnslaughtCyclingLandFactory.Create(_alice,
            new[] { "Bogus Land", "G", "NotASubtype" });
        act.Should().Throw<ArgumentException>();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static int ManaForColor(ManaCost cost, string color) => color switch
    {
        "W" => cost.White,
        "U" => cost.Blue,
        "B" => cost.Black,
        "R" => cost.Red,
        "G" => cost.Green,
        _ => 0,
    };
}
