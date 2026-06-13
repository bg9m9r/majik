using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Combo;

/// <summary>
/// The #1 engine risk for the Azorius Lotus Belcher combo (plan 2026-06-13,
/// Phase B): does the engine treat a modal-DFC card sitting in the LIBRARY by
/// its FRONT face (CR 712.4a — "while a double-faced card isn't on the
/// battlefield, consider only the characteristics of its front face")?
///
/// If the engine wrongly counts an MDFC whose BACK face is a land (e.g.
/// "Sea Gate Restoration // Sea Gate, Reborn") as a LAND while in the library,
/// then Goblin Charbelcher's "reveal until a nonland card" stops on the FIRST
/// MDFC — counting it as a land and ending the reveal at 1 — and the whole
/// combo is dead. So this probe builds the real Belcher MDFC fronts through the
/// production deck-build path (<see cref="GameFacade.Create"/> →
/// <see cref="DeckCardBuilder"/>) and asserts each reports
/// <c>HasType(Land) == false</c> in the library, then drives a full Charbelcher
/// reveal over a library of those fronts and asserts the kill arithmetic.
/// </summary>
public sealed class MdfcLibraryProbeTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    // The six MDFC fronts the Belcher manabase is built from — each is a
    // nonland spell FRONT // land BACK (referenced by FRONT name in the deck).
    // Single source of truth in BelcherLines.MdfcFronts.
    private static readonly string[] MdfcFronts = BelcherLines.MdfcFronts.ToArray();

    [Theory]
    [InlineData("Hydroelectric Specimen")]
    [InlineData("Jwari Disruption")]
    [InlineData("Sea Gate Restoration")]
    [InlineData("Sink into Stupor")]
    [InlineData("Razorgrass Ambush")]
    [InlineData("Waterlogged Teachings")]
    public void MdfcFront_InLibrary_IsNotALand(string frontName)
    {
        // Build the card EXACTLY as a live deck does: repo shell → GameFacade
        // deck-build (routed through named factories, which is prod default).
        var live = BuildLiveLibraryCard(frontName);

        live.HasType(CardType.Land).Should().BeFalse(
            $"CR 712.4a — '{frontName}' in the library is considered by its FRONT " +
            "(nonland) face; if it reported Land here, Goblin Charbelcher would " +
            "stop its reveal on it and the combo would be dead");
    }

    [Fact]
    public void Charbelcher_RevealsOverMdfcFrontLibrary_AllNonland_DamageEqualsLibrarySize()
    {
        // A library of ONLY MDFC fronts (all nonland by their front face). The
        // REAL Charbelcher reveals until a LAND card; with no land present the
        // reveal walks the WHOLE library (CR 608.2b clean stop on empty) and
        // damage = number of NONLAND cards revealed = the whole library. THIS
        // is the Belcher combo: an all-MDFC manabase is all-nonland in the
        // library, so the reveal burns the entire deck into the opponent.
        var alice = new Player("Alice", 60);
        var bob = new Player("Bob", 60);
        SeedLibraryWithLiveCards(alice, MdfcFronts);
        GameRandomRegistry.Set(alice, new GameRandom(seed: 99));

        var result = GoblinCharbelcherFactory.ResolveBelch(alice, bob);

        result.NonlandCount.Should().Be(MdfcFronts.Length,
            "every MDFC front is a nonland by its front face and there is no land to stop on");
        result.Damage.Should().Be(MdfcFronts.Length);
        result.Revealed.Should().HaveCount(MdfcFronts.Length,
            "the reveal walks the whole landless library");
        bob.LifeTotal.Should().Be(60 - MdfcFronts.Length);
    }

    [Fact]
    public void Charbelcher_LandlessLibrary_BurnsWholeDeck_IsLethal()
    {
        // THE combo floor: a landless library (all MDFC fronts) → the reveal
        // walks to the end, damage = nonland count = whole library size. With a
        // 30-card library that is 30 damage — lethal to a 20-life opponent.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var names = Enumerable.Range(0, 5)
            .SelectMany(_ => MdfcFronts)
            .ToArray(); // 30 cards
        SeedLibraryWithLiveCards(alice, names);
        GameRandomRegistry.Set(alice, new GameRandom(seed: 7));

        var result = GoblinCharbelcherFactory.ResolveBelch(alice, bob);

        result.NonlandCount.Should().Be(names.Length);
        result.Damage.Should().Be(names.Length, "30 nonland cards revealed → 30 damage");
        bob.LifeTotal.Should().BeLessThanOrEqualTo(0, "30 damage kills a 20-life opponent");
    }

    // -----------------------------------------------------------------------
    // Helpers (also used by the combo-line harness)
    // -----------------------------------------------------------------------

    internal static ICard BuildLiveLibraryCard(string name)
    {
        var shell = DeckCardShellBuilder.Build(
            Repo.GetByName(name)
            ?? throw new InvalidOperationException($"'{name}' not in embedded seed"));
        var facade = GameFacade.Create(
            aliceName: "A", bobName: "B",
            aliceDeck: new[] { shell },
            bobDeck: System.Array.Empty<ICard>(),
            cardRepo: Repo);
        return facade.Alice.Zones.GetZone(ZoneType.Library).GetCards().Single();
    }

    private static void SeedLibraryWithLiveCards(Player target, string[] names)
    {
        var shells = names.Select(n => DeckCardShellBuilder.Build(
            Repo.GetByName(n)
            ?? throw new InvalidOperationException($"'{n}' not in embedded seed"))).ToList();
        var facade = GameFacade.Create(
            aliceName: target.Name, bobName: "Opp",
            aliceDeck: shells,
            bobDeck: System.Array.Empty<ICard>(),
            cardRepo: Repo);

        var built = facade.Alice.Zones.GetZone(ZoneType.Library).GetCards().ToList();
        foreach (var c in built)
        {
            c.SetOwner(target);
            c.SetController(target);
            target.Zones.Library.AddCard(c);
        }
    }
}
