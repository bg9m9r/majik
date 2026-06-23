using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CryptOfAgadeemFactory"/> (Zendikar / reprints).
///
/// Oracle text (Scryfall-verified):
///   "This land enters tapped.
///    {T}: Add {B}.
///    {2}, {T}: Add {B} for each black creature card in your graveyard."
///
/// Covers ONLY the card's unique behaviour:
/// - Enters-tapped replacement (CR 614.1c) — present when wired through a
///   <see cref="ReplacementBus"/>; absent on the shape-only path.
/// - Basic {T}: Add {B} mana ability present.
/// - {2},{T}: Add {B} for each black creature card in your graveyard —
///   counts only black creature cards in the controller's graveyard, scales
///   the produced {B}, pays {2}, taps the land. Zero-creature activation is
///   legal (CR 605.1c). {2} gating (CR 119.4).
/// - The black-creature-graveyard count helper (colour + type predicate).
///
/// (Dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests — not repeated here.)
/// </summary>
[Trait("Color", "B")]
public class CryptOfAgadeemFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (ZoneService zones, ReplacementBus replacements) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        return (zones, rep);
    }

    /// <summary>Put Crypt of Agadeem on Alice's battlefield (untapped).</summary>
    private Land PlaceOnBattlefield()
    {
        var crypt = CryptOfAgadeemFactory.Create(_alice);
        crypt.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(crypt);
        return crypt;
    }

    /// <summary>Add a card to Alice's graveyard.</summary>
    private void AddToGraveyard(ICard card)
    {
        card.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(card);
    }

    private Creature MakeCreature(string name, string manaCost) =>
        new(name, manaCost, power: 1, toughness: 1) { Owner = _alice, Controller = _alice };

    // The single {2},{T} dynamic ability is the one whose ManaGenerated seed
    // is Zero (the basic {T}: Add {B} ability from JSON seeds one black).
    private static ManaAbility DynamicAbility(Land crypt) =>
        crypt.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.TotalValue == 0);

    private static ManaAbility BasicTapAbility(Land crypt) =>
        crypt.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.TotalValue == 1);

    // -----------------------------------------------------------------------
    // Enters-tapped (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersTapped_WhenWiredThroughReplacementBus()
    {
        var (zones, rep) = BuildEngine();

        var crypt = CryptOfAgadeemFactory.Create(_alice, rep);
        crypt.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(crypt);

        zones.MoveCardTo(crypt, ZoneType.Battlefield, controller: _alice);

        crypt.IsTapped.Should().BeTrue("CR 614.1c — this land enters tapped");
        crypt.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void EntersUntapped_OnShapeOnlyPath()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var crypt = CryptOfAgadeemFactory.Create(_alice);
        crypt.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(crypt);

        zones.MoveCardTo(crypt, ZoneType.Battlefield, controller: _alice);

        crypt.IsTapped.Should().BeFalse(
            "shape-only path omits the enters-tapped replacement");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {B}
    // -----------------------------------------------------------------------

    [Fact]
    public void HasBasicTapAddBlack_AndDynamicAbility()
    {
        var crypt = CryptOfAgadeemFactory.Create(_alice);

        // Two mana abilities: the basic {T}: Add {B} (from JSON) and the
        // {2},{T} dynamic ability (attached in code).
        crypt.Abilities.OfType<ManaAbility>().Should().HaveCount(2);

        var basic = BasicTapAbility(crypt);
        var mana = basic.Activate();
        mana.Black.Should().Be(1, "{T}: Add {B} produces exactly one black mana");
    }

    // -----------------------------------------------------------------------
    // Black-creature-graveyard count helper
    // -----------------------------------------------------------------------

    [Fact]
    public void CountBlackCreatureCardsInGraveyard_CountsOnlyBlackCreatures()
    {
        // Black creature → counts.
        AddToGraveyard(MakeCreature("Black Knight", "{B}{B}"));
        // Black-and-other creature (gold) → still counts (includes black).
        AddToGraveyard(MakeCreature("Vampire Nighthawk", "{1}{B}"));
        // Non-black creature → excluded.
        AddToGraveyard(MakeCreature("Grizzly Bears", "{1}{G}"));
        // Black non-creature card → excluded (an instant, not a creature).
        var darkRitual = new Instant("Dark Ritual", "{B}") { Owner = _alice, Controller = _alice };
        AddToGraveyard(darkRitual);

        CryptOfAgadeemFactory.CountBlackCreatureCardsInGraveyard(_alice).Should().Be(2,
            "only black creature cards in the graveyard count (CR 105 + CR 308)");
    }

    [Fact]
    public void CountBlackCreatureCardsInGraveyard_EmptyGraveyard_Zero()
    {
        CryptOfAgadeemFactory.CountBlackCreatureCardsInGraveyard(_alice).Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // {2}, {T}: Add {B} for each black creature card in your graveyard
    // -----------------------------------------------------------------------

    [Fact]
    public void DynamicAbility_ThreeBlackCreatures_AddsThreeBlack_PaysTwo_TapsLand()
    {
        var crypt = PlaceOnBattlefield();
        AddToGraveyard(MakeCreature("Carrion Feeder", "{B}"));
        AddToGraveyard(MakeCreature("Gravecrawler", "{B}"));
        AddToGraveyard(MakeCreature("Bloodghast", "{B}{B}"));

        _alice.AddManaToPool(ManaCost.Parse("2"));

        var ability = DynamicAbility(crypt);
        ability.CanActivate().Should().BeTrue();

        var mana = ability.Activate();

        mana.Black.Should().Be(3, "3 black creature cards → 3{B}");
        _alice.ManaPool.Generic.Should().Be(0, "the {2} cost was paid");
        crypt.IsTapped.Should().BeTrue("{T} is part of the activation cost");
    }

    [Fact]
    public void DynamicAbility_ZeroBlackCreatures_LegalActivation_AddsNoMana_StillPaysTwo()
    {
        // 0 black creature cards — legal per CR 605.1c, produces 0 mana.
        var crypt = PlaceOnBattlefield();
        _alice.AddManaToPool(ManaCost.Parse("2"));

        var ability = DynamicAbility(crypt);
        ability.CanActivate().Should().BeTrue();

        var mana = ability.Activate();

        mana.Black.Should().Be(0);
        mana.Generic.Should().Be(0);
        _alice.ManaPool.Generic.Should().Be(0, "the {2} cost was paid even with 0 mana produced");
        crypt.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void DynamicAbility_NonBlackCreaturesIgnored()
    {
        var crypt = PlaceOnBattlefield();
        AddToGraveyard(MakeCreature("Carrion Feeder", "{B}"));   // counts
        AddToGraveyard(MakeCreature("Grizzly Bears", "{1}{G}")); // ignored
        AddToGraveyard(MakeCreature("Llanowar Elves", "{G}"));   // ignored
        _alice.AddManaToPool(ManaCost.Parse("2"));

        var mana = DynamicAbility(crypt).Activate();

        mana.Black.Should().Be(1, "only the single black creature card counts");
    }

    [Fact]
    public void DynamicAbility_CannotActivateWhenCannotAffordTwo_TapsNothing()
    {
        var crypt = PlaceOnBattlefield();
        AddToGraveyard(MakeCreature("Carrion Feeder", "{B}"));
        // Pool empty — cannot pay {2}.
        var ability = DynamicAbility(crypt);

        ability.CanActivate().Should().BeFalse("cannot pay the {2} additional cost (CR 119.4)");
        crypt.IsTapped.Should().BeFalse("an illegal activation taps nothing");
    }

    [Fact]
    public void DynamicAbility_CannotActivateWhenTapped()
    {
        var crypt = PlaceOnBattlefield();
        crypt.Tap();
        _alice.AddManaToPool(ManaCost.Parse("2"));

        DynamicAbility(crypt).CanActivate().Should().BeFalse(
            "already tapped — {T} cost cannot be paid");
    }

    // -----------------------------------------------------------------------
    // BuildBlackMana internal helper
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BuildBlackMana_ZeroOrNegative_ReturnsZero(int n)
    {
        CryptOfAgadeemFactory.BuildBlackMana(n).Should().Be(ManaCost.Zero);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    public void BuildBlackMana_PositiveN_ReturnsNBlack(int n, int expectedBlack)
    {
        var result = CryptOfAgadeemFactory.BuildBlackMana(n);
        result.Black.Should().Be(expectedBlack);
        result.Generic.Should().Be(0);
    }
}
