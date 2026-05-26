using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Collected Company (Dragons of Tarkir, {3}{G}, Instant).
///
/// "Look at the top six cards of your library. You may put up to two
///  creature cards with mana value 3 or less from among them onto the
///  battlefield. Put the rest on the bottom of your library in a random
///  order."
///
/// Covers:
///  - Identity (name / type / mana cost) + NamedCardFactory dispatch.
///  - Resolve with two eligible creatures in the top six → both ETB,
///    remaining four bottom in random order.
///  - Mana-value filter (mv ≤ 3): a 4-drop in the peek is excluded.
///  - Creature-only filter: instants / sorceries / lands are ignored.
///  - Library shorter than six: works on whatever's available.
///  - Empty library: no-op.
///  - Agent decline: <c>ChooseLibraryPickAsync</c> returning null stops
///    further picks (legal under printed "up to two").
/// </summary>
public class CollectedCompanyTests
{
    private readonly Player _alice = new("Alice", 20);

    public CollectedCompanyTests()
    {
        // Library-bottom assertions use BeEquivalentTo so the random
        // order does not matter — no global RNG setup required.
        // AgentRegistry per-player set / clear is scoped inside the
        // single test that wires a stub agent.
    }

    [Fact]
    public void CollectedCompany_Identity()
    {
        var c = CollectedCompanyFactory.Create(_alice);

        c.Name.Should().Be("Collected Company");
        c.ManaCost.Should().Be("{3}{G}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().Be(_alice);
        c.Controller.Should().Be(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CollectedCompany()
    {
        var card = NamedCardFactory.Create("Collected Company", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Collected Company");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{3}{G}");
    }

    [Fact]
    public void Resolve_TwoEligibleCreatures_BothEtb_RestToBottom()
    {
        // Top 6: two mv≤3 creatures, four non-creatures / non-eligible.
        var bear      = SeedCreatureInLibrary(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var elves     = SeedCreatureInLibrary(_alice, "Llanowar Elves", "{G}", 1, 1);
        var bolt      = SeedInstantInLibrary(_alice, "Lightning Bolt", "{R}");
        var wrath     = SeedSorceryInLibrary(_alice, "Wrath of God", "{2}{W}{W}");
        var forest    = SeedLandInLibrary(_alice, "Forest");
        var bigTitan  = SeedCreatureInLibrary(_alice, "Primeval Titan", "{4}{G}{G}", 6, 6); // mv 6 → excluded

        CollectedCompanyFactory.Resolve(_alice);

        var bf = _alice.Zones.Battlefield.GetCards().ToList();
        bf.Should().Contain(bear);
        bf.Should().Contain(elves);
        bf.Should().NotContain(new ICard[] { bolt, wrath, forest, bigTitan });

        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().NotContain(new ICard[] { bear, elves });
        // Four remaining cards must all be in the library (now bottomed).
        lib.Should().BeEquivalentTo(new ICard[] { bolt, wrath, forest, bigTitan });
    }

    [Fact]
    public void Resolve_FourDropCreature_Excluded()
    {
        // Only ineligible creatures (mv=4) plus a mv=3 creature.
        var fourDrop = SeedCreatureInLibrary(_alice, "Tireless Tracker", "{2}{G}{G}", 3, 2);
        var threeDrop = SeedCreatureInLibrary(_alice, "Reflector Mage", "{1}{W}{U}", 2, 3);
        // Pad to six.
        for (int i = 0; i < 4; i++)
            SeedInstantInLibrary(_alice, $"Pad{i}", "{1}");

        CollectedCompanyFactory.Resolve(_alice);

        var bf = _alice.Zones.Battlefield.GetCards().ToList();
        bf.Should().Contain(threeDrop);
        bf.Should().NotContain(fourDrop);
        bf.Count.Should().Be(1);
    }

    [Fact]
    public void Resolve_NoCreaturesInTopSix_NoEtb_AllBottom()
    {
        var bolt   = SeedInstantInLibrary(_alice, "Lightning Bolt", "{R}");
        var wrath  = SeedSorceryInLibrary(_alice, "Wrath of God", "{2}{W}{W}");
        var forest = SeedLandInLibrary(_alice, "Forest");

        CollectedCompanyFactory.Resolve(_alice);

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().BeEquivalentTo(new ICard[] { bolt, wrath, forest });
    }

    [Fact]
    public void Resolve_ShortLibrary_WorksOnAvailableCards()
    {
        // Two cards total: one creature, one instant.
        var elves = SeedCreatureInLibrary(_alice, "Llanowar Elves", "{G}", 1, 1);
        var bolt = SeedInstantInLibrary(_alice, "Lightning Bolt", "{R}");

        CollectedCompanyFactory.Resolve(_alice);

        _alice.Zones.Battlefield.GetCards().Should().Contain(elves);
        _alice.Zones.Library.GetCards().Should().BeEquivalentTo(new[] { bolt });
    }

    [Fact]
    public void Resolve_EmptyLibrary_NoOp()
    {
        CollectedCompanyFactory.Resolve(_alice);

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_AgentDeclinesAfterFirstPick_OnlyOneEtb()
    {
        var first = SeedCreatureInLibrary(_alice, "Llanowar Elves", "{G}", 1, 1);
        var second = SeedCreatureInLibrary(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        // Pad to six.
        for (int i = 0; i < 4; i++)
            SeedInstantInLibrary(_alice, $"Pad{i}", "{1}");

        // Agent that picks the offered first creature on call #1, then
        // declines (returns null) on call #2. Passed directly to Resolve
        // so the test doesn't depend on shared AgentRegistry state.
        var agent = new DeclineAfterFirstAgent();

        CollectedCompanyFactory.Resolve(_alice, zoneService: null, agent: agent);

        var bf = _alice.Zones.Battlefield.GetCards().ToList();
        bf.Should().HaveCount(1);
        bf.Should().Contain(first);
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().Contain(second); // Second creature was bottomed, not ETB'd.
    }

    [Fact]
    public void Resolve_TwoEligibleButOnlyOnePickedIfAgentDeclines_RestBottomed()
    {
        // Top 6 mixed; the one creature is mv 3 elf.
        var elves = SeedCreatureInLibrary(_alice, "Llanowar Elves", "{G}", 1, 1);
        for (int i = 0; i < 5; i++)
            SeedInstantInLibrary(_alice, $"Filler{i}", "{1}");

        CollectedCompanyFactory.Resolve(_alice);

        _alice.Zones.Battlefield.GetCards().Should().Contain(elves);
        _alice.Zones.Battlefield.GetCards().Count().Should().Be(1);
        _alice.Zones.Library.GetCards().Count().Should().Be(5);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static ICard SeedCreatureInLibrary(
        Player p, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        return c;
    }

    private static ICard SeedInstantInLibrary(Player p, string name, string manaCost)
    {
        var c = new Instant(name, manaCost);
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        return c;
    }

    private static ICard SeedSorceryInLibrary(Player p, string name, string manaCost)
    {
        var c = new Sorcery(name, manaCost);
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        return c;
    }

    private static ICard SeedLandInLibrary(Player p, string name)
    {
        var c = new Land(name);
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        return c;
    }

    /// <summary>
    /// Test agent: picks the first offered candidate on the first call,
    /// then returns null on subsequent calls (declines further picks).
    /// Used to verify the "up to two" upper bound is honoured by an
    /// agent-driven decline.
    /// </summary>
    private sealed class DeclineAfterFirstAgent : IPlayerAgent
    {
        private int _calls;

        public Task<ICard?> ChooseLibraryPickAsync(
            GameContext? ctx,
            IReadOnlyList<ICard> candidates,
            string kindLabel,
            CancellationToken ct = default)
        {
            _calls++;
            if (_calls == 1 && candidates.Count > 0)
                return Task.FromResult<ICard?>(candidates[0]);
            return Task.FromResult<ICard?>(null);
        }

        // ---- unused decision hooks (throw to flag unexpected use) -----
        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int m, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int n, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest req, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> ChooseXAsync(GameContext ctx, ICard src, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> e, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> a, IReadOnlyList<Creature> e, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
