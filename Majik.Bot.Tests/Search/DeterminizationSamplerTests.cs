using FluentAssertions;
using Majik.Bot.Search;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Unit tests for <see cref="DeterminizationSampler"/> — the pure, deterministic,
/// decklist-aware resampler that replaces a game's HIDDEN zones (opponent hand +
/// both libraries) with a plausible sampled arrangement.
///
/// The fixture builds two real <see cref="Player"/>s with live cards via the SAME
/// prod-equivalent build path the sampler uses (<see cref="ScryfallCardFactory"/>),
/// so sampled cards and fixture cards are constructed identically.
/// </summary>
public class DeterminizationSamplerTests
{
    // A small, real opponent decklist drawn from the embedded seed. Mono-color,
    // common Burn staples so GetByName always resolves. Counts chosen to exercise
    // multiset subtraction (4 Lightning Bolt).
    private static readonly IReadOnlyList<string> OppDecklist = new[]
    {
        "Lightning Bolt", "Lightning Bolt", "Lightning Bolt", "Lightning Bolt",
        "Mountain", "Mountain", "Mountain", "Mountain", "Mountain", "Mountain",
        "Goblin Guide", "Goblin Guide", "Goblin Guide", "Goblin Guide",
        "Monastery Swiftspear", "Monastery Swiftspear",
    };

    private static readonly EmbeddedCardRepository Repo = new();
    private static readonly ScryfallCardFactory Factory = new(Repo);

    private static ICard Build(string name, Player owner) => Factory.Create(name, owner);

    /// <summary>
    /// Builds a fresh, deterministic two-seat fixture:
    /// - self: a 10-card library (real names) + a 3-card hand (KNOWN, untouched).
    /// - opp:  a 4-card hand (hidden), a 6-card library (hidden), 1 battlefield
    ///         permanent (Goblin Guide — a VISIBLE opponent card), 1 graveyard card.
    /// Players preserve fixed Ids so the searched seat can be located after a clone.
    /// </summary>
    private static (Player self, Player opp) BuildFixture()
    {
        var self = new Player("Self", 20);
        var opp = new Player("Opp", 20);

        // self HAND (known): 3 cards.
        foreach (var n in new[] { "Mountain", "Lightning Bolt", "Goblin Guide" })
            self.Zones.Hand.AddCard(Build(n, self));

        // self LIBRARY (order hidden): 10 cards.
        var selfLib = new[]
        {
            "Mountain", "Mountain", "Mountain", "Mountain", "Mountain",
            "Lightning Bolt", "Lightning Bolt", "Goblin Guide", "Goblin Guide",
            "Monastery Swiftspear",
        };
        foreach (var n in selfLib)
            self.Zones.GetZone(ZoneType.Library).AddCard(Build(n, self));

        // opp HAND (hidden): 4 placeholder cards — the sampler clears + refills.
        for (var i = 0; i < 4; i++)
            opp.Zones.Hand.AddCard(Build("Mountain", opp));

        // opp LIBRARY (hidden): 6 placeholder cards.
        for (var i = 0; i < 6; i++)
            opp.Zones.GetZone(ZoneType.Library).AddCard(Build("Mountain", opp));

        // opp BATTLEFIELD (VISIBLE): one Goblin Guide — must be subtracted from
        // the unknown multiset (4 in decklist → at most 3 unknown).
        opp.Zones.Battlefield.AddCard(Build("Goblin Guide", opp));

        // opp GRAVEYARD (VISIBLE): one Mountain — also subtracted.
        opp.Zones.Graveyard.AddCard(Build("Mountain", opp));

        return (self, opp);
    }

    private static IReadOnlyList<Player> Players(Player self, Player opp) => new[] { self, opp };

    [Fact]
    public void Resample_PreservesOppHandSize_AndAllSampledNamesAreInDecklist()
    {
        var (self, opp) = BuildFixture();
        var handSizeBefore = opp.Zones.Hand.GetCards().Count();

        DeterminizationSampler.Resample(Players(self, opp), self.Id, OppDecklist, worldSeed: 1234);

        opp.Zones.Hand.GetCards().Should().HaveCount(handSizeBefore);

        var sampled = opp.Zones.Hand.GetCards()
            .Concat(opp.Zones.GetZone(ZoneType.Library).GetCards())
            .Select(c => c.Name);
        sampled.Should().OnlyContain(n => OppDecklist.Contains(n));
    }

    [Fact]
    public void Resample_SameSeed_YieldsIdenticalOppHandSequence()
    {
        var (selfA, oppA) = BuildFixture();
        var (selfB, oppB) = BuildFixture();

        DeterminizationSampler.Resample(Players(selfA, oppA), selfA.Id, OppDecklist, worldSeed: 777);
        DeterminizationSampler.Resample(Players(selfB, oppB), selfB.Id, OppDecklist, worldSeed: 777);

        var handA = oppA.Zones.Hand.GetCards().Select(c => c.Name).ToList();
        var handB = oppB.Zones.Hand.GetCards().Select(c => c.Name).ToList();
        handA.Should().Equal(handB);

        var libA = oppA.Zones.GetZone(ZoneType.Library).GetCards().Select(c => c.Name).ToList();
        var libB = oppB.Zones.GetZone(ZoneType.Library).GetCards().Select(c => c.Name).ToList();
        libA.Should().Equal(libB);
    }

    [Fact]
    public void Resample_DifferentSeeds_ProduceDifferentArrangements()
    {
        var (selfA, oppA) = BuildFixture();
        var (selfB, oppB) = BuildFixture();

        DeterminizationSampler.Resample(Players(selfA, oppA), selfA.Id, OppDecklist, worldSeed: 1);
        DeterminizationSampler.Resample(Players(selfB, oppB), selfB.Id, OppDecklist, worldSeed: 99999);

        var seqA = oppA.Zones.Hand.GetCards()
            .Concat(oppA.Zones.GetZone(ZoneType.Library).GetCards())
            .Select(c => c.Name).ToList();
        var seqB = oppB.Zones.Hand.GetCards()
            .Concat(oppB.Zones.GetZone(ZoneType.Library).GetCards())
            .Select(c => c.Name).ToList();

        // Same multiset, but the order should differ for these well-separated seeds.
        seqA.Should().NotEqual(seqB);
    }

    [Fact]
    public void Resample_LeavesSelfHandUntouched()
    {
        var (self, opp) = BuildFixture();
        var handBefore = self.Zones.Hand.GetCards().ToList();

        DeterminizationSampler.Resample(Players(self, opp), self.Id, OppDecklist, worldSeed: 42);

        var handAfter = self.Zones.Hand.GetCards().ToList();
        // Same instances, same order — the known hand must be byte-for-byte untouched.
        handAfter.Should().Equal(handBefore);
    }

    [Fact]
    public void Resample_LeavesOppBattlefieldAndGraveyardUntouched()
    {
        var (self, opp) = BuildFixture();
        var bfBefore = opp.Zones.Battlefield.GetCards().ToList();
        var gyBefore = opp.Zones.Graveyard.GetCards().ToList();

        DeterminizationSampler.Resample(Players(self, opp), self.Id, OppDecklist, worldSeed: 42);

        opp.Zones.Battlefield.GetCards().Should().Equal(bfBefore);
        opp.Zones.Graveyard.GetCards().Should().Equal(gyBefore);
    }

    [Fact]
    public void Resample_ReshufflesSelfLibrary_PreservingNameMultiset()
    {
        var (self, opp) = BuildFixture();
        var before = self.Zones.GetZone(ZoneType.Library).GetCards()
            .Select(c => c.Name).OrderBy(n => n).ToList();

        DeterminizationSampler.Resample(Players(self, opp), self.Id, OppDecklist, worldSeed: 555);

        var after = self.Zones.GetZone(ZoneType.Library).GetCards()
            .Select(c => c.Name).OrderBy(n => n).ToList();
        // Same multiset of names (reshuffle preserves contents).
        after.Should().Equal(before);
    }

    [Fact]
    public void Resample_DoesNotOverCount_VisibleOppPermanents()
    {
        var (self, opp) = BuildFixture();
        // Decklist has 4 Goblin Guide; 1 is on opp's battlefield (visible) and 1
        // in the fixture is built into the self hand — but only OPP-visible cards
        // are subtracted, so at most 3 Goblin Guides should appear across opp's
        // hidden zones (hand + library).
        DeterminizationSampler.Resample(Players(self, opp), self.Id, OppDecklist, worldSeed: 8);

        var hiddenGoblinGuides = opp.Zones.Hand.GetCards()
            .Concat(opp.Zones.GetZone(ZoneType.Library).GetCards())
            .Count(c => c.Name == "Goblin Guide");
        hiddenGoblinGuides.Should().BeLessThanOrEqualTo(3);
    }

    [Fact]
    public void Resample_SampledOppCards_AreOwnedByOpp()
    {
        var (self, opp) = BuildFixture();

        DeterminizationSampler.Resample(Players(self, opp), self.Id, OppDecklist, worldSeed: 17);

        var sampled = opp.Zones.Hand.GetCards()
            .Concat(opp.Zones.GetZone(ZoneType.Library).GetCards());
        sampled.Should().OnlyContain(c => c.Owner == opp);
    }
}
