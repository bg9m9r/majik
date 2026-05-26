using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Jace, the Mind Sculptor (Worldwake, {2}{U}{U}).
///
/// Covers:
///   - Card identity (Legendary Planeswalker — Jace, starting loyalty 3,
///     mana cost {2}{U}{U}).
///   - Loyalty ability shape (four abilities: +2, 0, -1, -12).
///   - +2: top of target player's library moves to the bottom (v1 auto-
///     accepts the "may bottom" option).
///   - 0: draw 3 then put 2 cards from hand on top of library.
///   - -1: bounce target creature to owner's hand (CR 400.3 owner-routed).
///   - -12: exile target's library, hand → library, shuffle.
///   - NamedCardFactory dispatch.
/// </summary>
public class JaceTheMindSculptorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Jace_IsLegendaryPlaneswalker_Jace_3Loyalty_AtCost2UU()
    {
        var jace = JaceTheMindSculptorFactory.Create(_alice);

        jace.Name.Should().Be("Jace, the Mind Sculptor");
        jace.ManaCost.Should().Be("{2}{U}{U}");
        jace.HasType(CardType.Planeswalker).Should().BeTrue();
        jace.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        jace.HasSubtype(CardSubtype.Jace).Should().BeTrue();
        jace.Loyalty.Should().Be(3);
        jace.StartingLoyalty.Should().Be(3);
        jace.Owner.Should().BeSameAs(_alice);
        jace.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Jace_HasFourLoyaltyAbilities_Plus2_Zero_Minus1_Minus12()
    {
        var jace = JaceTheMindSculptorFactory.Create(_alice);
        var loyaltyAbilities = jace.Abilities.OfType<LoyaltyAbility>().ToList();

        loyaltyAbilities.Should().HaveCount(4);
        loyaltyAbilities.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +2, 0, -1, -12 });
    }

    [Fact]
    public void Jace_Plus2_MovesTopOfTargetLibraryToBottom()
    {
        // Bob's library: A on top, B, C on bottom. After +2 (auto-bottom):
        // B, C, A.
        var a = new Instant("A", "U") { Owner = _bob };
        var b = new Instant("B", "U") { Owner = _bob };
        var c = new Instant("C", "U") { Owner = _bob };
        _bob.Zones.Library.AddCard(a);
        _bob.Zones.Library.AddCard(b);
        _bob.Zones.Library.AddCard(c);
        a.SetZone(ZoneType.Library);
        b.SetZone(ZoneType.Library);
        c.SetZone(ZoneType.Library);

        var jace = JaceTheMindSculptorFactory.Create(
            _alice,
            targetPlayerResolver: () => new[] { _bob },
            targetCreatureResolver: null);

        var plus2 = jace.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +2);
        plus2.Activate();

        jace.Loyalty.Should().Be(5, "3 + 2 = 5");
        var libOrder = _bob.Zones.Library.GetCards().ToList();
        libOrder.Should().HaveCount(3);
        libOrder[0].Should().BeSameAs(b, "B was second, now on top");
        libOrder[2].Should().BeSameAs(a, "A was on top, now on bottom");
    }

    [Fact]
    public void Jace_Plus2_NoResolver_IsLegalNoOp()
    {
        var jace = JaceTheMindSculptorFactory.Create(_alice);

        var plus2 = jace.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +2);
        plus2.Activate();

        jace.Loyalty.Should().Be(5);
    }

    [Fact]
    public void Jace_Zero_DrawsThreeCardsThenPutsTwoFromHandOnTopOfLibrary()
    {
        // Library: l1 (top), l2, l3, l4, l5 (bottom).
        var libCards = new[] { "l1", "l2", "l3", "l4", "l5" }
            .Select(n => new Instant(n, "U") { Owner = _alice })
            .ToArray();
        foreach (var c in libCards)
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
        // Hand: h1, h2 — these will be put on top of library after the draw.
        var h1 = new Instant("h1", "U") { Owner = _alice };
        var h2 = new Instant("h2", "U") { Owner = _alice };
        _alice.Zones.Hand.AddCard(h1);
        _alice.Zones.Hand.AddCard(h2);
        h1.SetZone(ZoneType.Hand);
        h2.SetZone(ZoneType.Hand);

        var jace = JaceTheMindSculptorFactory.Create(_alice);

        var zero = jace.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == 0);
        zero.Activate();

        jace.Loyalty.Should().Be(3, "loyalty 0 = unchanged");

        // After draw 3: hand had h1 + h2 + drew l1, l2, l3 → hand has
        // {h1, h2, l1, l2, l3}, library has {l4, l5}.
        // Then put 2 cards from hand on top of library. v1 takes the
        // first two in hand (h1, h2) — first iterated lands deepest.
        // After insertion at index 0 in iteration order: h2 ends up on
        // top, h1 underneath h2.
        var libOrder = _alice.Zones.Library.GetCards().ToList();
        libOrder.Should().HaveCount(4, "5 - 3 drawn + 2 returned");
        libOrder[0].Should().BeSameAs(h2, "second pick lands on top");
        libOrder[1].Should().BeSameAs(h1, "first pick lands directly under second");

        var hand = _alice.Zones.Hand.GetCards().ToList();
        hand.Should().HaveCount(3);
        hand.Should().NotContain(new ICard[] { h1, h2 });
        hand.Should().Contain(new ICard[] { libCards[0], libCards[1], libCards[2] });
    }

    [Fact]
    public void Jace_Minus1_BouncesTargetCreatureToOwnersHand()
    {
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", power: 2, toughness: 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(grizzly);
        grizzly.SetZone(ZoneType.Battlefield);

        var jace = JaceTheMindSculptorFactory.Create(
            _alice,
            targetPlayerResolver: null,
            targetCreatureResolver: () => new[] { grizzly });

        var minus1 = jace.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -1);
        minus1.Activate();

        jace.Loyalty.Should().Be(2, "3 - 1 = 2");
        // CR 400.3 — returns to owner's (Bob's) hand.
        _bob.Zones.Hand.GetCards().Should().Contain(grizzly);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(grizzly);
        grizzly.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Jace_Minus12_ExilesTargetLibraryThenShufflesHandIntoLibrary()
    {
        // Bob's library: 4 cards. Bob's hand: 3 cards.
        var libCards = new[] { "L1", "L2", "L3", "L4" }
            .Select(n => new Instant(n, "U") { Owner = _bob })
            .ToArray();
        foreach (var c in libCards)
        {
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var handCards = new[] { "H1", "H2", "H3" }
            .Select(n => new Instant(n, "U") { Owner = _bob })
            .ToArray();
        foreach (var c in handCards)
        {
            _bob.Zones.Hand.AddCard(c);
            c.SetZone(ZoneType.Hand);
        }

        var jace = JaceTheMindSculptorFactory.Create(
            _alice,
            targetPlayerResolver: () => new[] { _bob },
            targetCreatureResolver: null);
        jace.AddLoyalty(9); // 3 → 12, -12 legal.

        var minus12 = jace.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -12);
        minus12.CanActivate().Should().BeTrue();
        minus12.Activate();

        jace.Loyalty.Should().Be(0, "12 - 12 = 0");
        _bob.Zones.Exile.GetCards().Should().Contain(libCards);
        _bob.Zones.Hand.GetCards().Should().BeEmpty();
        _bob.Zones.Library.GetCards().Should().Contain(handCards);
        _bob.Zones.Library.Count.Should().Be(3, "all 3 hand cards moved to library");
        _bob.Zones.Library.GetCards().Should().NotContain(libCards as IEnumerable<ICard>,
            "library was bulk-exiled first");
    }

    [Fact]
    public void Jace_Minus12_NoResolver_IsLegalNoOp()
    {
        var jace = JaceTheMindSculptorFactory.Create(_alice);
        jace.AddLoyalty(9); // 3 → 12

        var minus12 = jace.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -12);
        minus12.Activate();

        jace.Loyalty.Should().Be(0, "loyalty change still applies");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_JaceTheMindSculptor()
    {
        var card = NamedCardFactory.Create("Jace, the Mind Sculptor", _alice);

        card.Should().BeOfType<Planeswalker>();
        card.Name.Should().Be("Jace, the Mind Sculptor");
        card.HasType(CardType.Planeswalker).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Jace).Should().BeTrue();
        ((Planeswalker)card).Loyalty.Should().Be(3);
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<LoyaltyAbility>().Should().HaveCount(4);
    }
}
