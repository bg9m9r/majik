using FluentAssertions;
using Majik.Bot.Decks;
using Majik.Bot.Evaluation;
using Majik.Bot.Search;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Verifies the determinization plumbing in <see cref="EngineSimulator"/>: when a
/// root's <see cref="SimState.WorldSeed"/> is set, each sandbox clone has its hidden
/// zones resampled from the opponent decklist before it is searched; when WorldSeed
/// is null the perfect-info path is untouched (the clone keeps the opponent's ACTUAL
/// hand). Exercised through the test-only <c>DebugSampledOpponentHand</c> hook so the
/// wiring is observable without driving a whole game.
/// </summary>
public class EngineSimulatorDeterminizationTests
{
    private static readonly EmbeddedCardRepository Repo = new();
    private static readonly ScryfallCardFactory Factory = new(Repo);

    private static ICard Build(string name, Player owner) => Factory.Create(name, owner);

    /// <summary>
    /// Two-seat board: searched seat with a known hand + library; opponent with a
    /// hand of distinctive ACTUAL cards (so the perfect-info path is recognisable)
    /// plus a small library. Fixed Ids survive the clone.
    /// </summary>
    private static SimState BuildRoot(out Player self, out Player opp, int? worldSeed = null)
    {
        self = new Player("Self", 20);
        opp = new Player("Opp", 20);

        foreach (var n in new[] { "Mountain", "Lightning Bolt" })
            self.Zones.Hand.AddCard(Build(n, self));
        foreach (var n in new[] { "Mountain", "Mountain", "Goblin Guide", "Lightning Bolt" })
            self.Zones.GetZone(ZoneType.Library).AddCard(Build(n, self));

        // Opponent ACTUAL hand: distinctive cards that are NOT in the Burn decklist,
        // so the perfect-info assertion (hand unchanged) is meaningful — a resample
        // would necessarily replace them with Burn cards.
        var actualHand = new[] { "Island", "Counterspell", "Llanowar Elves" };
        foreach (var n in actualHand)
            opp.Zones.Hand.AddCard(Build(n, opp));
        foreach (var n in new[] { "Island", "Island", "Counterspell" })
            opp.Zones.GetZone(ZoneType.Library).AddCard(Build(n, opp));

        var players = new[] { self, opp };
        var root = SimState.Capture(
            livePlayers: players,
            activePlayer: self,
            turnNumber: 2,
            phase: PhaseStateType.PreCombatMain,
            searchedSeat: self);

        return worldSeed is int seed
            ? root.WithDeterminization(BotDeckCatalog.Get("Burn"), seed)
            : root;
    }

    private static EngineSimulator NewSim() => new(ArchetypeWeights.Default);

    [Fact]
    public void DebugSampledOpponentHand_WithDeterminization_DrawsFromDecklist_PreservingHandSize()
    {
        var root = BuildRoot(out _, out var opp, worldSeed: 11);
        var burn = BotDeckCatalog.Get("Burn");
        var realHandCount = opp.Zones.Hand.GetCards().Count();

        var sampled = NewSim().DebugSampledOpponentHand(root);

        sampled.Should().HaveCount(realHandCount);
        sampled.Should().OnlyContain(n => burn.Contains(n));
    }

    [Fact]
    public void DebugSampledOpponentHand_PerfectInfo_KeepsActualOpponentHand()
    {
        var root = BuildRoot(out _, out var opp); // WorldSeed null
        var actual = opp.Zones.Hand.GetCards().Select(c => c.Name).OrderBy(n => n).ToList();

        var sampled = NewSim().DebugSampledOpponentHand(root).OrderBy(n => n).ToList();

        sampled.Should().Equal(actual);
    }

    [Fact]
    public void DebugSampledOpponentHand_SameSeed_IsDeterministic()
    {
        var rootA = BuildRoot(out _, out _, worldSeed: 11);
        var rootB = BuildRoot(out _, out _, worldSeed: 11);

        var a = NewSim().DebugSampledOpponentHand(rootA);
        var b = NewSim().DebugSampledOpponentHand(rootB);

        a.Should().Equal(b);
    }
}
