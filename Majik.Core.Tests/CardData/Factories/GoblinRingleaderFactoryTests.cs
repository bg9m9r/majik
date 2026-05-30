using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GoblinRingleaderFactory"/>.
///
/// Card: Goblin Ringleader — Creature — Goblin {3}{R} 2/2.
/// Oracle text (verified against Scryfall):
///   "Haste (This creature can attack and {T} as soon as it comes under your
///    control.)
///    When this creature enters, reveal the top four cards of your library.
///    Put all Goblin cards revealed this way into your hand and the rest on
///    the bottom of your library in any order."
///
/// Covers:
/// - Identity ({3}{R}, 2/2, Creature — Goblin, red).
/// - NamedCardFactory dispatch.
/// - Haste keyword marker (CR 702.10).
/// - Exactly one battlefield-active ETB TriggeredAbility.
/// - ETB: stocked library — Goblins among top 4 go to hand, rest to bottom.
/// - ETB: no Goblins among top 4 — all four go to the bottom, hand unchanged.
/// - ETB: fewer than 4 cards — reveals what's available (graceful).
/// - ETB: empty library — no-op, no crash.
/// </summary>
public class GoblinRingleaderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinRingleader_Identity()
    {
        var c = GoblinRingleaderFactory.Create(_alice);

        c.Name.Should().Be("Goblin Ringleader");
        c.ManaCost.Should().Be("{3}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue("Goblin Ringleader is a Goblin");
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GoblinRingleader_IsRed()
    {
        var c = GoblinRingleaderFactory.Create(_alice);

        var colors = Majik.Core.Cards.CardColors.GetColors(c);
        colors.Should().Contain(Majik.Core.ValueObjects.ManaColor.Red,
            "Goblin Ringleader has {R} in its mana cost");
    }

    [Fact]
    public void GoblinRingleader_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Goblin Ringleader", _alice);

        c.Should().BeOfType<Creature>("Goblin Ringleader is a Creature");
        c.Name.Should().Be("Goblin Ringleader");
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.ManaCost.Should().Be("{3}{R}");
    }

    // -----------------------------------------------------------------------
    // Haste — CR 702.10
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinRingleader_HasHasteKeyword()
    {
        var c = GoblinRingleaderFactory.Create(_alice);

        Majik.Core.Combat.CombatAbilities.HasHaste(c).Should().BeTrue(
            "Goblin Ringleader has the printed Haste keyword (CR 702.10)");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinRingleader_ExactlyOneBattlefieldActiveEtbTrigger()
    {
        var c = GoblinRingleaderFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();

        triggers.Should().HaveCount(1,
            "Goblin Ringleader has exactly one triggered ability — the ETB reveal");

        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are active while the permanent is on the battlefield (CR 603.6a)");
    }

    // -----------------------------------------------------------------------
    // ETB — reveal top 4, Goblins to hand, rest to bottom
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinRingleader_EtbTrigger_GoblinsToHand_RestToBottom()
    {
        var alice = new Player("Alice", 20);

        // Top 4: Goblin, nonland nonGoblin, Goblin, nonGoblin. Then a 5th
        // card that should NOT be touched (only the top four are revealed).
        var g1 = Goblin("Goblin Guide");
        var nonGoblinA = NonGoblin("Lightning Bolt");
        var g2 = Goblin("Goblin Piledriver");
        var nonGoblinB = NonGoblin("Mountain");
        var bottomMarker = NonGoblin("UntouchedFifth");

        foreach (var card in new[] { g1, nonGoblinA, g2, nonGoblinB, bottomMarker })
        {
            card.SetOwner(alice);
            alice.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        var ringleader = GoblinRingleaderFactory.Create(alice);
        var etb = ringleader.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        // Both Goblins among the top four go to hand.
        var hand = alice.Zones.Hand.GetCards();
        hand.Should().HaveCount(2, "both revealed Goblins go to hand");
        hand.Should().Contain(g1);
        hand.Should().Contain(g2);

        // The two non-Goblins go to the bottom; the fifth card stays where it
        // was (it was never revealed) and is now above the bottomed cards.
        var library = alice.Zones.Library.GetCards().ToList();
        library.Should().HaveCount(3,
            "5 started; 2 Goblins removed to hand; the other 3 remain");
        library.Should().Contain(nonGoblinA);
        library.Should().Contain(nonGoblinB);
        library.Should().Contain(bottomMarker);

        // The untouched fifth card is now on TOP (the two non-Goblins were
        // moved to the bottom beneath it).
        library[0].Should().BeSameAs(bottomMarker,
            "the unrevealed fifth card is on top; bottomed non-Goblins sit beneath it");
        library.Should().ContainInOrder(bottomMarker, nonGoblinA, nonGoblinB);
    }

    [Fact]
    public void GoblinRingleader_EtbTrigger_NoGoblins_AllFourToBottom()
    {
        var alice = new Player("Alice", 20);

        var a = NonGoblin("A");
        var b = NonGoblin("B");
        var c = NonGoblin("C");
        var d = NonGoblin("D");

        foreach (var card in new[] { a, b, c, d })
        {
            card.SetOwner(alice);
            alice.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        var ringleader = GoblinRingleaderFactory.Create(alice);
        var etb = ringleader.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no Goblins revealed → nothing goes to hand");
        alice.Zones.Library.GetCards().Should().HaveCount(4,
            "all four revealed non-Goblins go to the bottom; none leave the library");
    }

    [Fact]
    public void GoblinRingleader_EtbTrigger_FewerThanFourCards_RevealsWhatsAvailable()
    {
        var alice = new Player("Alice", 20);

        var g = Goblin("Mogg Fanatic");
        var n = NonGoblin("Shock");

        foreach (var card in new[] { g, n })
        {
            card.SetOwner(alice);
            alice.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        var ringleader = GoblinRingleaderFactory.Create(alice);
        var etb = ringleader.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };
        act.Should().NotThrow("fewer than 4 cards is a graceful short-circuit (CR 701.16)");

        alice.Zones.Hand.GetCards().Should().Contain(g, "the revealed Goblin goes to hand");
        alice.Zones.Hand.GetCards().Should().HaveCount(1);
        alice.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(n, "the revealed non-Goblin goes to the bottom");
    }

    [Fact]
    public void GoblinRingleader_EtbTrigger_EmptyLibrary_NoCrash_NothingMoves()
    {
        var alice = new Player("Alice", 20);
        // Library intentionally empty.

        var ringleader = GoblinRingleaderFactory.Create(alice);
        var etb = ringleader.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };
        act.Should().NotThrow("empty library is a valid no-op");

        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "nothing in library → nothing moves to hand");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature Goblin(string name) =>
        new(name: name, manaCost: "{R}", power: 1, toughness: 1,
            subtypes: new[] { CardSubtype.Goblin });

    private static Card NonGoblin(string name) => new(name, "");
}
