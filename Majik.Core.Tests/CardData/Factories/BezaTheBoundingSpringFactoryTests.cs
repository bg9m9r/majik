using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BezaTheBoundingSpringFactory"/> (Bloomburrow,
/// {2}{W}{W}, Legendary Creature — Elemental Elk 4/5). Oracle text (verified
/// against Scryfall 2026-06-23):
///   "When Beza enters, create a Treasure token if an opponent controls more
///    lands than you. You gain 4 life if an opponent has more life than you.
///    Create two 1/1 blue Fish creature tokens if an opponent controls more
///    creatures than you. Draw a card if an opponent has more cards in hand
///    than you."
///
/// Covers only Beza's UNIQUE behaviour — the four independent intervening-if
/// ETB clauses (CR 603.1) — plus a single identity assert. NamedCardFactory
/// dispatch + well-formedness are covered automatically by
/// <see cref="Majik.Core.Tests.CardData.CardFactoryContractTests"/>.
/// </summary>
[Trait("Color", "W")]
public class BezaTheBoundingSpringFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ───────────────────────────────────────────────────────────────────
    // Identity
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Beza_IsLegendaryElementalElk4_5_AtCost2WW()
    {
        var card = BezaTheBoundingSpringFactory.Create(_alice);

        card.Name.Should().Be("Beza, the Bounding Spring");
        card.ManaCost.Should().Be("{2}{W}{W}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        card.HasSubtype(CardSubtype.Elk).Should().BeTrue();
        card.Power.Should().Be(4);
        card.Toughness.Should().Be(5);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Beza_HasExactlyOneEtbTriggeredAbility()
    {
        var card = BezaTheBoundingSpringFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Beza's four clauses are one ETB triggered ability.");
    }

    // ───────────────────────────────────────────────────────────────────
    // Treasure clause — CR 111.10 / strict "more lands than you" (CR 603.4)
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_CreatesTreasure_WhenAnOpponentControlsMoreLands()
    {
        SeedLandOnBattlefield("Island", _bob);
        // Alice 0 lands, Bob 1 → opponent out-lands you.

        ResolveBezaEtb(out var card);

        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasSubtype(CardSubtype.Treasure)).Should().Be(1,
            "an opponent controls strictly more lands than you (CR 111.10).");
    }

    [Fact]
    public void Resolve_NoTreasure_WhenYouAreNotOutLanded()
    {
        SeedLandOnBattlefield("Plains", _alice);
        SeedLandOnBattlefield("Island", _bob);
        // Tie (1 vs 1) → "more lands than you" is strict, so false.

        ResolveBezaEtb(out var card);

        _alice.Zones.Battlefield.GetCards()
            .Any(c => c.HasSubtype(CardSubtype.Treasure)).Should().BeFalse(
            "a tie is not 'more lands than you' (CR 603.4, strict).");
    }

    // ───────────────────────────────────────────────────────────────────
    // Life clause — "you gain 4 life if an opponent has more life than you"
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_Gains4Life_WhenAnOpponentHasMoreLife()
    {
        _alice.SetLifeTotal(15);
        _bob.SetLifeTotal(20);

        ResolveBezaEtb(out var card);

        _alice.LifeTotal.Should().Be(19,
            "an opponent has strictly more life than you → gain 4 (CR 119.3).");
    }

    [Fact]
    public void Resolve_NoLifeGain_WhenYouAreNotBehindOnLife()
    {
        _alice.SetLifeTotal(20);
        _bob.SetLifeTotal(20);

        ResolveBezaEtb(out var card);

        _alice.LifeTotal.Should().Be(20,
            "a tie is not 'more life than you' (strict).");
    }

    // ───────────────────────────────────────────────────────────────────
    // Fish clause — "create two 1/1 blue Fish if an opponent controls more
    // creatures than you"
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_CreatesTwoBlueFish_WhenAnOpponentControlsMoreCreatures()
    {
        // Bob controls one creature; Alice controls none *other than Beza*.
        // Beza itself is on Alice's battlefield when the ETB resolves, so the
        // comparison counts Beza (1) vs Bob (2) → Bob still out-creatures.
        SeedCreatureOnBattlefield(_bob);
        SeedCreatureOnBattlefield(_bob);

        ResolveBezaEtb(out var card);

        var fish = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Fish))
            .ToList();
        fish.Should().HaveCount(2, "an opponent controls more creatures than you.");
        fish.Should().OnlyContain(f => f.Power == 1 && f.Toughness == 1);
        fish.Should().OnlyContain(f => CardColors.GetColors(f).Contains(ManaColor.Blue),
            "Beza's Fish tokens are blue (CR 111.4).");
    }

    [Fact]
    public void Resolve_NoFish_WhenYouAreNotOutCreatured()
    {
        // Only Beza on Alice's side (1); Bob has none → Beza is not out-creatured.
        ResolveBezaEtb(out var card);

        _alice.Zones.Battlefield.GetCards()
            .Any(c => c is Creature ct && ct.IsToken && ct.HasSubtype(CardSubtype.Fish))
            .Should().BeFalse("no opponent controls more creatures than you.");
    }

    // ───────────────────────────────────────────────────────────────────
    // Draw clause — "draw a card if an opponent has more cards in hand"
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_DrawsACard_WhenAnOpponentHasMoreCardsInHand()
    {
        SeedInHand("Plains", _bob);
        SeedInHand("Plains", _bob);
        // Alice 0 in hand, Bob 2 → opponent has more cards in hand.
        var top = SeedInLibrary("Forest", _alice);

        ResolveBezaEtb(out var card);

        _alice.Zones.Hand.GetCards().Should().Contain(top,
            "an opponent has more cards in hand than you → draw a card (CR 120.2).");
    }

    [Fact]
    public void Resolve_NoDraw_WhenYouAreNotBehindOnHand()
    {
        SeedInHand("Plains", _alice);
        SeedInHand("Island", _bob);
        // Tie (1 vs 1) → strict, false.
        var top = SeedInLibrary("Forest", _alice);

        ResolveBezaEtb(out var card);

        _alice.Zones.Library.GetCards().Should().Contain(top,
            "a tie is not 'more cards in hand than you'.");
    }

    // ───────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build Beza for Alice, place it on Alice's battlefield, and resolve its
    /// ETB trigger through the async path with a live <see cref="GameContext"/>
    /// (Alice + Bob), so every clause's intervening-if reads opponents exactly
    /// as in a live match.
    /// </summary>
    private void ResolveBezaEtb(out Creature card)
    {
        card = BezaTheBoundingSpringFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetController(_alice);
        card.SetZone(ZoneType.Battlefield);

        var game = new GameContext(
            self: _alice,
            allPlayers: new[] { _alice, _bob },
            activePlayer: _alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new EventBus()));

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ResolveAsync(agent: null, game: game).AsTask().GetAwaiter().GetResult();
    }

    private static ICard SeedInLibrary(string name, Player owner)
    {
        var c = NamedCardFactory.Create(name, owner);
        c.SetZone(ZoneType.Library);
        owner.Zones.Library.AddCard(c);
        return c;
    }

    private static ICard SeedInHand(string name, Player owner)
    {
        var c = NamedCardFactory.Create(name, owner);
        c.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(c);
        return c;
    }

    private void SeedLandOnBattlefield(string name, Player owner)
    {
        var c = NamedCardFactory.Create(name, owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
    }

    private static void SeedCreatureOnBattlefield(Player owner)
    {
        var c = new Creature("Bear", "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
    }
}
