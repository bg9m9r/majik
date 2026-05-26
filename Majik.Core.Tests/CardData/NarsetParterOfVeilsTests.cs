using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Random;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Narset, Parter of Veils — Legendary Planeswalker {1}{U}{U},
/// loyalty 5 (CR 117.1a printed static + CR 606 loyalty abilities).
///
/// Covers:
/// - Identity / subtype / loyalty / dispatcher routing.
/// - Printed-static draw restriction: opponents cap at one draw per
///   turn via <see cref="NarsetDrawRestrictionReplacement"/> on each
///   opponent's <see cref="ReplacementBus"/>.
/// - Per-turn reset on <see cref="TurnStartedEvent"/>.
/// - Controller is not affected by the restriction.
/// - LTB releases the restriction.
/// - -2: top-4 peek picks the first noncreature/nonland and bottoms
///   the rest in random order.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class NarsetParterOfVeilsTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public NarsetParterOfVeilsTests()
    {
        GameRandomRegistry.Clear();
        _zones = new ZoneService(_bus);
        _alice.AttachReplacementBus(new ReplacementBus());
        _bob.AttachReplacementBus(new ReplacementBus());
    }

    public void Dispose()
    {
        GameRandomRegistry.Clear();
    }

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Narset_HasCorrectIdentity_AndLoyalty5_AndNarsetSubtype()
    {
        var narset = NarsetParterOfVeilsFactory.Create(_alice);

        narset.Name.Should().Be("Narset, Parter of Veils");
        narset.ManaCost.Should().Be("{1}{U}{U}");
        narset.HasType(CardType.Planeswalker).Should().BeTrue();
        narset.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        narset.HasSubtype(CardSubtype.Narset).Should().BeTrue();
        narset.StartingLoyalty.Should().Be(5);
        narset.Loyalty.Should().Be(5);
        narset.Owner.Should().BeSameAs(_alice);
        narset.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_RoutesNarsetParterOfVeils_ToFactory()
    {
        var card = NamedCardFactory.Create("Narset, Parter of Veils", _alice);

        card.Should().BeOfType<Planeswalker>();
        card.Name.Should().Be("Narset, Parter of Veils");
        ((Planeswalker)card).StartingLoyalty.Should().Be(5);
        card.HasSubtype(CardSubtype.Narset).Should().BeTrue();
    }

    [Fact]
    public void Narset_HasMinus2_LoyaltyAbility()
    {
        var narset = NarsetParterOfVeilsFactory.Create(_alice);

        var loyaltyAbilities = narset.Abilities.OfType<LoyaltyAbility>().ToList();
        loyaltyAbilities.Should().HaveCount(1);
        loyaltyAbilities.Should().Contain(la => la.LoyaltyChange == -2);
    }

    // -----------------------------------------------------------------------
    // Printed static — CR 117.1a "opponent can't draw more than 1/turn"
    // -----------------------------------------------------------------------

    [Fact]
    public void NarsetOnBattlefield_LetsOpponentFirstDrawThrough_ThenCancelsSubsequentDraws()
    {
        // Seed Bob's library with 3 cards so we can attempt 3 draws.
        SeedLibrary(_bob, 3);

        var narset = NarsetParterOfVeilsFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: _bus);

        _alice.Zones.Library.AddCard(narset);
        narset.SetZone(ZoneType.Library);
        _zones.MoveCard(narset, ZoneType.Library, ZoneType.Battlefield);

        // Bob attempts to draw 3. Only the first should land in hand.
        var drawn = Fx.DrawCards(_bob, 3);

        drawn.Should().HaveCount(1);
        _bob.Zones.Hand.GetCards().Should().HaveCount(1);
        // Remaining two library cards untouched (cancelled draws don't
        // mill the library).
        _bob.Zones.Library.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void NarsetOnBattlefield_DoesNotRestrictNarsetController()
    {
        // Alice is the controller — restriction does NOT apply to her.
        SeedLibrary(_alice, 3);

        var narset = NarsetParterOfVeilsFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: _bus);
        _alice.Zones.Library.AddCard(narset);
        narset.SetZone(ZoneType.Library);
        _zones.MoveCard(narset, ZoneType.Library, ZoneType.Battlefield);

        var drawn = Fx.DrawCards(_alice, 3);
        drawn.Should().HaveCount(3);
        _alice.Zones.Hand.GetCards().Should().HaveCount(3);
    }

    [Fact]
    public void NarsetRestriction_ResetsOnTurnStart_AllowingOneMoreDrawNextTurn()
    {
        SeedLibrary(_bob, 4);

        var narset = NarsetParterOfVeilsFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: _bus);
        _alice.Zones.Library.AddCard(narset);
        narset.SetZone(ZoneType.Library);
        _zones.MoveCard(narset, ZoneType.Library, ZoneType.Battlefield);

        // Turn 1: Bob draws — first draw goes through, second is cancelled.
        Fx.DrawCards(_bob, 2);
        _bob.Zones.Hand.GetCards().Should().HaveCount(1);

        // Turn 2: publish TurnStartedEvent — counter resets.
        _bus.Publish(new TurnStartedEvent(_bob, 2));

        // Bob can draw again — first draw of new turn passes through.
        Fx.DrawCards(_bob, 2);
        _bob.Zones.Hand.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void NarsetLeavingBattlefield_ReleasesRestriction()
    {
        SeedLibrary(_bob, 3);

        var narset = NarsetParterOfVeilsFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: _bus);
        _alice.Zones.Library.AddCard(narset);
        narset.SetZone(ZoneType.Library);
        _zones.MoveCard(narset, ZoneType.Library, ZoneType.Battlefield);

        // Narset dies → restriction lifts.
        _zones.MoveCard(narset, ZoneType.Battlefield, ZoneType.Graveyard);

        // All 3 draws now succeed.
        var drawn = Fx.DrawCards(_bob, 3);
        drawn.Should().HaveCount(3);
        _bob.Zones.Hand.GetCards().Should().HaveCount(3);
    }

    // -----------------------------------------------------------------------
    // -2: peek top 4, grab noncreature/nonland, rest to bottom in random order
    // -----------------------------------------------------------------------

    [Fact]
    public void NarsetMinus2_PicksFirstNoncreatureNonlandFromTop4_AndBottomsTheRest()
    {
        // Top 4: Forest, Bear, Counterspell, Ornithopter
        // Eligible noncreature/nonland: Counterspell (first match)
        // Remaining to bottom (random order): Forest, Bear, Ornithopter
        var forest = new Land("Forest");
        var bear = new Creature("Bear", "1G", 2, 2);
        var counter = new Instant("Counterspell", "UU");
        var ornithopter = new Artifact("Ornithopter", "0");
        var deeperCard = new Land("Plains"); // already on bottom

        foreach (var c in new ICard[] { forest, bear, counter, ornithopter, deeperCard })
        {
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        // Deterministic RNG so test is stable.
        GameRandomRegistry.Set(_alice, new GameRandom(seed: 42));

        var narset = NarsetParterOfVeilsFactory.Create(
            _alice,
            opponentResolver: null,
            eventBus: null);

        narset.Abilities.OfType<LoyaltyAbility>().Single(la => la.LoyaltyChange == -2).Activate();

        // Counterspell went to hand.
        _alice.Zones.Hand.GetCards().Should().Contain(counter);
        counter.Zone.Should().Be(ZoneType.Hand);

        // Library no longer contains Counterspell.
        _alice.Zones.Library.GetCards().Should().NotContain(counter);

        // Forest, Bear, Ornithopter still in library (now at bottom) along
        // with the original bottom card (Plains stays where it was, since
        // we only peeked at the top 4).
        var libCards = _alice.Zones.Library.GetCards().ToList();
        libCards.Should().HaveCount(4);
        libCards.Should().Contain(new ICard[] { forest, bear, ornithopter, deeperCard });

        // Plains should remain at its original position (deepest was Plains
        // before; after the operation Plains is no longer the deepest since
        // we appended 3 cards beneath it). Confirm Plains is the top of the
        // remaining library (no eligible cards were ahead of it after the
        // 4 originally on top were processed).
        // The original library order was: Forest, Bear, Counter, Ornithopter, Plains.
        // After Counter to hand and Forest/Bear/Ornithopter shuffled to the
        // bottom, Plains is now on top.
        libCards[0].Should().BeSameAs(deeperCard);

        // Loyalty change applied.
        narset.Loyalty.Should().Be(3); // 5 - 2
    }

    [Fact]
    public void NarsetMinus2_NoEligibleCard_LeavesLibrarySize_RemainsOrderedRandomly()
    {
        // Top 4 are all creatures/lands — no eligible card to reveal.
        // Remainder still goes to bottom in random order (per Oracle:
        // "Put the rest on the bottom of your library in a random order"
        // — the "rest" is the whole peek when no reveal happens).
        var top1 = new Creature("Bear", "1G", 2, 2);
        var top2 = new Land("Forest");
        var top3 = new Creature("Wolf", "G", 1, 1);
        var top4 = new Land("Plains");

        foreach (var c in new ICard[] { top1, top2, top3, top4 })
        {
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        GameRandomRegistry.Set(_alice, new GameRandom(seed: 7));

        var narset = NarsetParterOfVeilsFactory.Create(_alice);

        narset.Abilities.OfType<LoyaltyAbility>().Single(la => la.LoyaltyChange == -2).Activate();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().HaveCount(4);
        _alice.Zones.Library.GetCards().Should().BeEquivalentTo(new ICard[] { top1, top2, top3, top4 });
    }

    [Fact]
    public void NarsetMinus2_EmptyLibrary_IsCleanNoOp()
    {
        var narset = NarsetParterOfVeilsFactory.Create(_alice);
        var act = () => narset.Abilities.OfType<LoyaltyAbility>()
            .Single(la => la.LoyaltyChange == -2).Activate();

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        narset.Loyalty.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void SeedLibrary(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Creature($"Stub-{i}", "", 1, 1);
            c.SetOwner(p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }
}
