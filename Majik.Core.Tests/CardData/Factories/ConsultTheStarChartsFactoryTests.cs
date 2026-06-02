using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ConsultTheStarChartsFactory"/>.
///
/// Consult the Star Charts (Khans of Tarkir, {1}{U}, Instant):
///   "Kicker {1}{U} (You may pay an additional {1}{U} as you cast this spell.)
///    Look at the top X cards of your library, where X is the number of lands
///    you control. Put one of those cards into your hand. If this spell was
///    kicked, put two of those cards into your hand instead. Put the rest on
///    the bottom of your library in a random order."
///
/// Covers:
///   - Card identity: name, instant type, {1}{U} mana cost, owner/controller.
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Kicker discovery via <see cref="KickerAltCostProbe.DefaultLookup"/>
///     ({1}{U}).
///   - X = lands controlled: unkicked, peek = lands count, take 1.
///   - Unkicked, no agent: take top card; rest to bottom.
///   - Unkicked, agent picks a specific card.
///   - Kicked: take TWO cards into hand; rest to bottom.
///   - Kicked, agent picks two specific cards.
///   - Zero lands: X = 0, peek empty, no-op (no draw clause → no SBA flag).
///   - Short library (fewer cards than X): peek tolerates it.
///   - Kicked with only one card available: takes the single card, no crash.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
[Trait("Color", "U")]
public class ConsultTheStarChartsFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void ConsultTheStarCharts_HasExpectedShape()
    {
        var card = ConsultTheStarChartsFactory.Create(_alice);

        card.Name.Should().Be("Consult the Star Charts");
        card.ManaCost.Should().Be("{1}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void KickerAltCostProbe_Recognises_ConsultTheStarCharts()
    {
        var card = ConsultTheStarChartsFactory.Create(_alice);

        var kicker = KickerAltCostProbe.DefaultLookup(card);

        kicker.Should().NotBeNull();
        kicker!.Should().Be(ManaCost.Parse("{1}{U}"));
    }

    // ── Resolve — unkicked ───────────────────────────────────────────────────

    [Fact]
    public void Resolve_Unkicked_NoAgent_PeeksLandsCount_TakesTop_BottomsRest()
    {
        // 3 lands → X = 3. Library: [top, second, third, fourth].
        SeedLands(3);
        var top    = SeedLibraryCard("Top");
        var second = SeedLibraryCard("Second");
        var third  = SeedLibraryCard("Third");
        var fourth = SeedLibraryCard("Fourth");

        var card = ConsultTheStarChartsFactory.Create(_alice);
        RunResolve(card);

        // No agent → take the first peeked card; second + third to bottom.
        _alice.Zones.Hand.GetCards().Should().Equal(new[] { top });
        _alice.Zones.Library.GetCards().Should().HaveCount(3);
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(fourth);
        _alice.Zones.Library.GetCards().Should().Contain(second).And.Contain(third);
        top.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_Unkicked_AgentPicksSecond_TakesSecond()
    {
        SeedLands(3);
        var top    = SeedLibraryCard("Top");
        var second = SeedLibraryCard("Second");
        var third  = SeedLibraryCard("Third");
        SeedLibraryCard("Fourth");

        AgentRegistry.Set(_alice, new PickByNameAgent("Second"));

        var card = ConsultTheStarChartsFactory.Create(_alice);
        RunResolve(card);

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { second });
        _alice.Zones.Hand.GetCards().Should().NotContain(top).And.NotContain(third);
        second.Zone.Should().Be(ZoneType.Hand);
    }

    // ── Resolve — kicked ─────────────────────────────────────────────────────

    [Fact]
    public void Resolve_Kicked_NoAgent_TakesTwoCards()
    {
        // 4 lands → X = 4. Kicked → take TWO of the peeked cards.
        SeedLands(4);
        var top    = SeedLibraryCard("Top");
        var second = SeedLibraryCard("Second");
        var third  = SeedLibraryCard("Third");
        var fourth = SeedLibraryCard("Fourth");

        var card = ConsultTheStarChartsFactory.Create(_alice);
        card.SetWasKicked(true);
        RunResolve(card);

        // No agent → take the first two peeked cards (top, second).
        _alice.Zones.Hand.GetCards().Should().HaveCount(2);
        _alice.Zones.Hand.GetCards().Should().Contain(top).And.Contain(second);
        _alice.Zones.Hand.GetCards().Should().NotContain(third).And.NotContain(fourth);
        top.Zone.Should().Be(ZoneType.Hand);
        second.Zone.Should().Be(ZoneType.Hand);
        third.Zone.Should().Be(ZoneType.Library);
        fourth.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void Resolve_Kicked_AgentPicksTwoSpecific_TakesThem()
    {
        SeedLands(4);
        var top    = SeedLibraryCard("Top");
        var second = SeedLibraryCard("Second");
        var third  = SeedLibraryCard("Third");
        var fourth = SeedLibraryCard("Fourth");

        // Agent picks "Second" then "Fourth".
        AgentRegistry.Set(_alice, new PickByNameAgent("Second", "Fourth"));

        var card = ConsultTheStarChartsFactory.Create(_alice);
        card.SetWasKicked(true);
        RunResolve(card);

        _alice.Zones.Hand.GetCards().Should().HaveCount(2);
        _alice.Zones.Hand.GetCards().Should().Contain(second).And.Contain(fourth);
        _alice.Zones.Hand.GetCards().Should().NotContain(top).And.NotContain(third);
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_ZeroLands_NoOp_NoDrawFromEmptyFlag()
    {
        // No lands → X = 0 → peek nothing. Effect is a no-op; no draw clause
        // so the empty-library SBA does NOT fire (CR 704.5b).
        SeedLibraryCard("A");
        SeedLibraryCard("B");

        var card = ConsultTheStarChartsFactory.Create(_alice);
        Action act = () => RunResolve(card);

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().HaveCount(2);
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void Resolve_Unkicked_ShortLibrary_PeeksAvailable_TakesTop()
    {
        // 5 lands → X = 5, but only 2 cards in library. Peek returns 2.
        SeedLands(5);
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");

        var card = ConsultTheStarChartsFactory.Create(_alice);
        RunResolve(card);

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { b });
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void Resolve_Kicked_OnlyOneCardAvailable_TakesIt_NoCrash()
    {
        // Kicked wants two, but only one card is reachable. Take what's there.
        SeedLands(3);
        var a = SeedLibraryCard("A");

        var card = ConsultTheStarChartsFactory.Create(_alice);
        card.SetWasKicked(true);
        Action act = () => RunResolve(card);

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a });
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void RunResolve(Card card)
    {
        foreach (var fx in ConsultTheStarChartsFactory.BuildResolveEffect(card, _alice))
        {
            fx.Execute();
        }
    }

    private void SeedLands(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var land = new Land($"Island {i}");
            land.SetOwner(_alice);
            land.SetController(_alice);
            _alice.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
        }
    }

    private Card SeedLibraryCard(string name)
    {
        var c = new Card(name, "");
        c.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    /// <summary>
    /// Test-only agent resolving <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>
    /// by matching candidate <see cref="ICard.Name"/>s in order. Consecutive
    /// calls consume the next name in the list (so a kicked Consult can pick two
    /// distinct cards). Falls back to the first candidate when out of names.
    /// </summary>
    private sealed class PickByNameAgent : IPlayerAgent
    {
        private readonly Queue<string> _names;
        public PickByNameAgent(params string[] names) { _names = new Queue<string>(names); }

        public Task<ICard?> ChooseLibraryPickAsync(
            GameContext? ctx,
            IReadOnlyList<ICard> candidates,
            string kindLabel,
            CancellationToken ct = default)
        {
            ICard? match = null;
            if (_names.Count > 0)
            {
                var want = _names.Dequeue();
                match = candidates.FirstOrDefault(c => c.Name == want);
            }
            match ??= candidates.Count > 0 ? candidates[0] : null;
            return Task.FromResult(match);
        }

        // ---- unused decision hooks -----------------------------------
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
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> a, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> a, IReadOnlyList<Creature> b, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
