using FluentAssertions;
using Majik.Bot;
using Majik.Bot.Search;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Belcher-trace regression (2026-06-12): a hand with ONLY MDFC-back-land cards
/// (zero true lands) was permanently mana-locked — the bot enumerated no land
/// play (MDFCs are not Land objects) and could not afford the front face at 0
/// mana, so it passed/discarded every turn (16/16 in the trace, maxAvail=0
/// throughout).
///
/// <para>Post Tasks 1-2 the heuristic bot enumerates the MDFC back-face land
/// play and MdfcFacePolicy picks the land at 0 mana, so a real land hits the
/// battlefield and mana starts flowing. This test drives a full bot-vs-bot game
/// for a few turns and asserts the paralysis is gone.</para>
/// </summary>
public sealed class MdfcBackLandIntegrationTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    /// <summary>
    /// A deck of ONLY MDFC-back-land cards (Sink into Stupor // Soporific
    /// Springs) — no true lands at all. This is the paralysis setup: every
    /// card the bot draws can only produce mana via its back land face.
    /// </summary>
    private static IReadOnlyList<ICard> MdfcLandOnlyDeck(int count = 40)
    {
        var entity = Repo.GetByName("Sink into Stupor")
            ?? throw new InvalidOperationException(
                "Sink into Stupor not in embedded seed — cannot build the MDFC-land regression deck.");
        return Enumerable.Range(0, count)
            .Select(_ => DeckCardShellBuilder.Build(entity))
            .ToList();
    }

    [Fact]
    public async Task MdfcLandOnlyHand_EscapesZeroManaDeadlock()
    {
        // Both seats run the MDFC-land-only deck so the game is symmetric; we
        // inspect Alice (the heuristic seat under test).
        var facade = GameFacade.Create(
            aliceName: "Heuristic",
            bobName:   "Opponent",
            aliceDeck: MdfcLandOnlyDeck(),
            bobDeck:   MdfcLandOnlyDeck(),
            cardRepo:  Repo);

        facade.ReplaceAliceAgent(new BotPlayerAgent(
            facade.Alice, new BotConfig("Control", Strategy: "heuristic", RandomSeed: 1)));
        facade.ReplaceBobAgent(new BotPlayerAgent(
            facade.Bob, new BotConfig("Control", Strategy: "heuristic", RandomSeed: 2)));

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        // Run a few turns. maxTurns=4 is enough to prove the bot lays a land by
        // turn 2 and has mana available by turn 3 (the exact assertions the
        // Belcher trace proved false).
        await facade.StartFullGameAsync(maxTurns: 4, ct: cts.Token, rng: new GameRandom(42));
        await facade.FullGameTask!;

        var alice = facade.Alice;

        // ≥1 land on the battlefield: the back-face land WAS played (it can only
        // get there by the bot choosing the MDFC back land face — there are no
        // true lands in the deck).
        var lands = alice.Zones.GetZone(ZoneType.Battlefield).GetCards()
            .Where(c => c.HasType(CardType.Land)).ToList();
        lands.Should().NotBeEmpty(
            "the bot must escape the deadlock by playing an MDFC back-face land");
        lands.Should().Contain(c => c.Name == "Soporific Springs",
            "the only land in the deck is the Sink into Stupor back face");

        // Mana is actually available now (the trace's maxAvail=0 → > 0).
        LegalActionEnumerator.UntappedManaSources(alice).Should().BeGreaterThan(0,
            "with a back-face land in play the bot can tap for mana (deadlock broken)");
    }
}
