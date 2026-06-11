using FluentAssertions;
using Majik.Bot.Decks;
using Majik.Bot.Search;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Simulation;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Unit tests for the observation-augmentation overload of
/// <see cref="DeterminizationSampler.Resample(System.Collections.Generic.IReadOnlyList{Player}, System.Guid, System.Collections.Generic.IReadOnlyList{string}, int, System.Collections.Generic.IReadOnlyList{string}?, ScryfallCardFactory?)"/>.
///
/// When the opponent has publicly REVEALED a card, the sampler should bias the
/// sampled HIDDEN pool (hand + library) toward MORE copies of that card. The
/// no-<c>observedPublic</c> path must stay byte-identical to the shipped sampler
/// (back-compat).
///
/// Fixtures mirror <see cref="DeterminizationSamplerTests"/>: two real
/// <see cref="Player"/>s built via the SAME prod-equivalent path the sampler uses
/// (<see cref="ScryfallCardFactory"/>). Because <c>GameStateCloner.Clone</c> assigns
/// the clones FRESH Guids, the searched-seat Id and opponent reference are taken from
/// the clone's <c>PlayerMap</c>, not the original players.
/// </summary>
public class DeterminizationAugmentTests
{
    private static readonly EmbeddedCardRepository Repo = new();
    private static readonly ScryfallCardFactory Factory = new(Repo);

    private static ICard Build(string name, Player owner) => Factory.Create(name, owner);

    /// <summary>
    /// Two-seat board: alice = searched seat (known hand + library); bob = opponent
    /// with a hidden hand + library plus ONE copy of <paramref name="revealed"/> in
    /// the PUBLIC graveyard. The graveyard copy is subtracted from the unknown
    /// multiset by the existing visible-subtraction step.
    /// </summary>
    private static (Player alice, Player bob) BuildOppWithRevealed(string revealed)
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // alice HAND (known): 3 cards.
        foreach (var n in new[] { "Mountain", "Lightning Bolt", "Goblin Guide" })
            alice.Zones.Hand.AddCard(Build(n, alice));
        // alice LIBRARY (order hidden): a handful.
        foreach (var n in new[] { "Mountain", "Mountain", "Lightning Bolt", "Goblin Guide" })
            alice.Zones.GetZone(ZoneType.Library).AddCard(Build(n, alice));

        // bob HAND (hidden): 4 placeholder cards — the sampler clears + refills.
        for (var i = 0; i < 4; i++)
            bob.Zones.Hand.AddCard(Build("Mountain", bob));
        // bob LIBRARY (hidden): 6 placeholder cards.
        for (var i = 0; i < 6; i++)
            bob.Zones.GetZone(ZoneType.Library).AddCard(Build("Mountain", bob));

        // bob GRAVEYARD (PUBLIC): one copy of the revealed card. This is what the bot
        // has observed the opponent commit to.
        bob.Zones.Graveyard.AddCard(Build(revealed, bob));

        return (alice, bob);
    }

    /// <summary>
    /// Does the cloned opponent (the non-searched seat) hold <paramref name="card"/>
    /// in a HIDDEN zone (hand or library) after resample?
    /// </summary>
    private static bool OppHiddenContains(ClonedGame clone, Player originalAlice, string card)
    {
        var aliceClone = clone.PlayerFor(originalAlice);
        var opp = clone.Players.First(p => p.Id != aliceClone.Id);
        return opp.Zones.Hand.GetCards()
            .Concat(opp.Zones.GetZone(ZoneType.Library).GetCards())
            .Any(c => c.Name == card);
    }

    [Fact]
    public void Resample_WithObservedPublic_RaisesRevealedCardFrequency_InHiddenZones()
    {
        // Sacred Foundry is a 1-of in the Burn list — boosting toward 4 is observable.
        const string card = "Sacred Foundry";
        // Sanity: the chosen card really is a low-count card in the decklist.
        BotDeckCatalog.Get("Burn").Count(n => n == card).Should().Be(1);

        int augHits = 0, baseHits = 0;
        for (int seed = 0; seed < 40; seed++)
        {
            var b1 = BuildOppWithRevealed(card);
            var c1 = GameStateCloner.Clone(new[] { b1.alice, b1.bob });
            DeterminizationSampler.Resample(
                c1.Players, c1.PlayerFor(b1.alice).Id, BotDeckCatalog.Get("Burn"), seed);
            if (OppHiddenContains(c1, b1.alice, card)) baseHits++;

            var b2 = BuildOppWithRevealed(card);
            var c2 = GameStateCloner.Clone(new[] { b2.alice, b2.bob });
            DeterminizationSampler.Resample(
                c2.Players, c2.PlayerFor(b2.alice).Id, BotDeckCatalog.Get("Burn"), seed,
                observedPublic: new[] { card, card, card });
            if (OppHiddenContains(c2, b2.alice, card)) augHits++;
        }

        augHits.Should().BeGreaterThan(
            baseHits,
            "revealed cards should appear more often in sampled hidden zones");
    }

    [Fact]
    public void Resample_NullObservedPublic_IsUnchangedFromShippedPath()
    {
        // Back-compat: the existing 4-arg call and the new call with a null/omitted
        // observedPublic must produce the IDENTICAL hidden hand + library for a given
        // seed. Proves the augmentation is inert when no observation is supplied.
        var burn = BotDeckCatalog.Get("Burn");

        for (int seed = 0; seed < 10; seed++)
        {
            var bA = BuildOppWithRevealed("Sacred Foundry");
            var cA = GameStateCloner.Clone(new[] { bA.alice, bA.bob });
            DeterminizationSampler.Resample(cA.Players, cA.PlayerFor(bA.alice).Id, burn, seed);

            var bB = BuildOppWithRevealed("Sacred Foundry");
            var cB = GameStateCloner.Clone(new[] { bB.alice, bB.bob });
            DeterminizationSampler.Resample(
                cB.Players, cB.PlayerFor(bB.alice).Id, burn, seed, observedPublic: null);

            var oppA = cA.Players.First(p => p.Id != cA.PlayerFor(bA.alice).Id);
            var oppB = cB.Players.First(p => p.Id != cB.PlayerFor(bB.alice).Id);

            var handA = oppA.Zones.Hand.GetCards().Select(c => c.Name).ToList();
            var handB = oppB.Zones.Hand.GetCards().Select(c => c.Name).ToList();
            handA.Should().Equal(handB);

            var libA = oppA.Zones.GetZone(ZoneType.Library).GetCards().Select(c => c.Name).ToList();
            var libB = oppB.Zones.GetZone(ZoneType.Library).GetCards().Select(c => c.Name).ToList();
            libA.Should().Equal(libB);
        }
    }
}
