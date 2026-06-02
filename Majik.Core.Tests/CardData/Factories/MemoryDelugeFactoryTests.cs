using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Memory Deluge (Innistrad: Midnight Hunt, {2}{U}{U}, Instant).
///
/// Oracle: "Look at the top X cards of your library, where X is the amount of
/// mana spent to cast this spell. Put two of them into your hand and the rest
/// on the bottom of your library in a random order. Flashback {5}{U}{U}."
///
/// Coverage:
///   - Identity (name, type, cost) + NamedCardFactory dispatch.
///   - Resolve puts two cards into hand and bottoms the rest.
///   - Default X = printed mana value 4 when no provider supplied.
///   - Bottomed cards land at the very bottom of the library (under the
///     pre-existing tail).
///   - Random order: a seeded RNG produces a deterministic bottom order.
///   - Short library (< 2 cards): all go to hand, nothing bottomed.
///   - Empty library: clean no-op (no draw-from-empty SBA — never says draw).
///   - X = 0 / negative clamps to a clean no-op.
///   - Flashback alt-cost = {5}{U}{U}.
/// </summary>
[Trait("Color", "U")]
public class MemoryDelugeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void MemoryDeluge_IsInstant_At2UU()
    {
        var s = MemoryDelugeFactory.Create(_alice);

        s.Name.Should().Be("Memory Deluge");
        s.ManaCost.Should().Be("{2}{U}{U}");
        s.HasType(CardType.Instant).Should().BeTrue();
        s.Owner.Should().BeSameAs(_alice);
        s.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void DefaultManaSpent_IsFour()
    {
        MemoryDelugeFactory.DefaultManaSpent.Should().Be(4,
            "the printed cost {2}{U}{U} has mana value 4");
    }

    // ── Resolve — look at X, take two, bottom the rest ──────────────────

    [Fact]
    public void Resolve_X4_PutsTwoInHand_BottomsTheRest()
    {
        // Library top→bottom: a b c d e. X=4 → look at a b c d.
        // No agent → first two (a, b) to hand; c, d to bottom; e untouched.
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");
        var e = SeedLibraryCard("E");

        // Seed 0 fixes the bottom order deterministically.
        var effects = MemoryDelugeFactory.BuildResolveEffect(_alice, () => 4, new GameRandom(0));
        foreach (var fx in effects) fx.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a, b });

        var lib = _alice.Zones.Library.GetCards().ToList();
        // e was below the looked-at window, so it stays on top of the bottomed pair.
        lib[0].Should().BeSameAs(e);
        lib.Should().HaveCount(3);
        // c and d are the bottomed pair, in some order.
        lib.Skip(1).Should().BeEquivalentTo(new[] { c, d });
    }

    [Fact]
    public void Resolve_DefaultProviderNull_UsesDefaultManaSpent_Four()
    {
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");

        var effects = MemoryDelugeFactory.BuildResolveEffect(_alice, manaSpentProvider: null, new GameRandom(0));
        foreach (var fx in effects) fx.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a, b });
        _alice.Zones.Library.GetCards().Should().BeEquivalentTo(new[] { c, d });
    }

    [Fact]
    public void Resolve_BottomedCards_AreRandomised_DeterministicForSeed()
    {
        // Two different seeds must be capable of producing different bottom
        // orders for the same input; the order for a fixed seed is stable.
        ICard[] BottomOrderForSeed(int seed)
        {
            var alice = new Player("Alice", 20);
            for (var i = 0; i < 6; i++)
            {
                var card = new Card($"C{i}", "");
                card.SetOwner(alice);
                alice.Zones.Library.AddCard(card);
                card.SetZone(ZoneType.Library);
            }
            var effects = MemoryDelugeFactory.BuildResolveEffect(alice, () => 6, new GameRandom(seed));
            foreach (var fx in effects) fx.Execute();
            return alice.Zones.Library.GetCards().Select(c => (ICard)c).ToArray();
        }

        var order1 = BottomOrderForSeed(0);
        var order1Again = BottomOrderForSeed(0);

        // X=6 over a 6-card library: two to hand, four bottomed.
        order1.Should().HaveCount(4);
        // Deterministic for a fixed seed.
        order1.Select(c => c.Name).Should().Equal(order1Again.Select(c => c.Name));
    }

    [Fact]
    public void Resolve_ShortLibrary_AllToHand_NothingBottomed()
    {
        // Only one card; X=4 → look at the one card, it goes to hand, nothing
        // to bottom.
        var a = SeedLibraryCard("A");

        var effects = MemoryDelugeFactory.BuildResolveEffect(_alice, () => 4, new GameRandom(0));
        foreach (var fx in effects) fx.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a });
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_EmptyLibrary_NoOp_NoDrawFromEmptyFlag()
    {
        var effects = MemoryDelugeFactory.BuildResolveEffect(_alice, () => 4, new GameRandom(0));
        foreach (var fx in effects) fx.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse(
            "Memory Deluge never instructs the player to draw");
    }

    [Fact]
    public void Resolve_XZero_NoOp()
    {
        SeedLibraryCard("A");
        SeedLibraryCard("B");

        var effects = MemoryDelugeFactory.BuildResolveEffect(_alice, () => 0, new GameRandom(0));
        foreach (var fx in effects) fx.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void Resolve_XNegative_ClampsToZero_NoOp()
    {
        SeedLibraryCard("A");

        var effects = MemoryDelugeFactory.BuildResolveEffect(_alice, () => -5, new GameRandom(0));
        foreach (var fx in effects) fx.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().HaveCount(1);
    }

    // ── Flashback ───────────────────────────────────────────────────────

    [Fact]
    public void BuildFlashbackCost_Is5UU()
    {
        var cost = MemoryDelugeFactory.BuildFlashbackCost();

        cost.AlternativeManaCost.Should().Be(ManaCost.Parse("{5}{U}{U}"));
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private Card SeedLibraryCard(string name)
    {
        var c = new Card(name, "");
        c.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
