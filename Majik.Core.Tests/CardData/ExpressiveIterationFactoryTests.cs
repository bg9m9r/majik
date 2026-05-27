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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ExpressiveIterationFactory"/>.
///
/// Expressive Iteration (Strixhaven: School of Mages, {U}{R}, Sorcery):
///   "Look at the top three cards of your library. Put one of them into
///    your hand, put one of them on the bottom of your library, and exile
///    one of them. You may play the exiled card this turn."
///
/// Covers:
///   - Card identity (name, {U}{R}, Sorcery, mana value 2, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Resolve (no agent): default choices (top → hand, middle → bottom,
///     last → exile with play-this-turn grant).
///   - Resolve (agent picks each slot via scripted name choices):
///     exactly one card in hand, one at bottom of library, one in exile
///     WITH a <see cref="Card.RuntimeExileCastAllowedCaster"/> grant for
///     the caster.
///   - The exile-cast grant can be validated by <see cref="ExileCastAlternativeCost"/>.
///   - Short library (fewer than 3 cards): fills destinations in order;
///     no throws.
///   - Empty library: no-op; no throws.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class ExpressiveIterationFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // ── Identity ──────────────────────────────────────────────────────────

    [Fact]
    public void ExpressiveIteration_HasExpectedShape()
    {
        var card = ExpressiveIterationFactory.Create(_alice);

        card.Name.Should().Be("Expressive Iteration");
        card.ManaCost.Should().Be("{U}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        card.ManaCostValue.TotalValue.Should().Be(2, "mana value of {U}{R} is 2 (CR 202.3)");
        card.ManaCostValue.Blue.Should().Be(1);
        card.ManaCostValue.Red.Should().Be(1);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ExpressiveIteration()
    {
        var card = NamedCardFactory.Create("Expressive Iteration", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Expressive Iteration");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{U}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // ── Resolve — default (no agent): top → hand, middle → bottom, bottom → exile ──

    [Fact]
    public void Resolve_NoAgent_TopToHand_MiddleToBottom_LastToExileWithGrant()
    {
        // Library: [a, b, c, d]. No agent → default: a→hand, b→bottom, c→exile.
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");

        var effects = ExpressiveIterationFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        // Exactly one card in hand
        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a },
            "default: first peeked card goes to hand");

        // Library: d then b (b was second/middle, bottomed; d untouched)
        _alice.Zones.Library.GetCards().Should().Equal(new[] { d, b },
            "default: second peeked card bottomed; fourth card stays on top");

        // Exile: c with play-this-turn grant
        _alice.Zones.Exile.GetCards().Should().Equal(new[] { c },
            "default: third peeked card exiled");
        c.Zone.Should().Be(ZoneType.Exile);
        c.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "exiled card must have a play-this-turn grant for the caster (CR 118.9)");
        c.RuntimeExileCastCost.Should().NotBeNull();

        // a and b zone tracking
        a.Zone.Should().Be(ZoneType.Hand);
        b.Zone.Should().Be(ZoneType.Library);
    }

    // ── Resolve — agent drives all three picks ───────────────────────────

    [Fact]
    public void Resolve_AgentPicks_CorrectDestinations_ExiledCardHasGrant()
    {
        // Library: [X, Y, Z, extra]. Agent: hand=Y, bottom=X, exile=Z.
        var x = SeedLibraryCard("X");
        var y = SeedLibraryCard("Y");
        var z = SeedLibraryCard("Z");
        var extra = SeedLibraryCard("Extra");

        // Agent picks: hand=Y, bottom=X, exile=Z (third slot gets remainder).
        AgentRegistry.Set(_alice, new ScriptedPickAgent(
            handPick: "Y",
            bottomPick: "X"));

        var effects = ExpressiveIterationFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { y });
        y.Zone.Should().Be(ZoneType.Hand);

        // Bottom: X appended behind extra (extra was never peeked → stays at top)
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(extra);
        _alice.Zones.Library.GetCards().Should().Contain(x);
        x.Zone.Should().Be(ZoneType.Library);

        _alice.Zones.Exile.GetCards().Should().Equal(new[] { z });
        z.Zone.Should().Be(ZoneType.Exile);
        z.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "agent-selected exiled card must also get the play-this-turn grant");
        z.RuntimeExileCastCost.Should().NotBeNull();

        // ExileCastAlternativeCost must accept the grant for Alice, not Bob
        var altCost = new ExileCastAlternativeCost("EI grant", z.RuntimeExileCastCost!);
        altCost.CanCastFor(z, _alice).Should().BeTrue();
        altCost.CanCastFor(z, _bob).Should().BeFalse();
    }

    // ── Short library edge cases ─────────────────────────────────────────

    [Fact]
    public void Resolve_TwoCardLibrary_HandAndExile_NoBottom()
    {
        // Only 2 cards: first→hand, second→bottom. No card left for exile.
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");

        var effects = ExpressiveIterationFactory.BuildResolveEffect(_alice);
        Action act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();

        // At least one card must have moved (hand or exile or bottom)
        var handCount = _alice.Zones.Hand.GetCards().Count();
        var exileCount = _alice.Zones.Exile.GetCards().Count();
        var libCount = _alice.Zones.Library.GetCards().Count();
        (handCount + exileCount + libCount).Should().Be(2,
            "all cards stay accounted for across zones");
    }

    [Fact]
    public void Resolve_EmptyLibrary_NoOp_NoThrow()
    {
        var effects = ExpressiveIterationFactory.BuildResolveEffect(_alice);
        Action act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private Card SeedLibraryCard(string name, string cost = "{R}")
    {
        var c = new Card(name, cost);
        c.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    /// <summary>
    /// Test agent that uses name matching for the "hand" and "bottom" picks;
    /// the exile pick gets whatever remains (no explicit choice needed —
    /// factory assigns it automatically).
    /// </summary>
    private sealed class ScriptedPickAgent : IPlayerAgent
    {
        private readonly string _handPick;
        private readonly string _bottomPick;
        private int _callCount;

        public ScriptedPickAgent(string handPick, string bottomPick)
        {
            _handPick = handPick;
            _bottomPick = bottomPick;
        }

        public Task<ICard?> ChooseLibraryPickAsync(
            GameContext? ctx,
            IReadOnlyList<ICard> candidates,
            string kindLabel,
            CancellationToken ct = default)
        {
            _callCount++;
            var target = _callCount == 1 ? _handPick : _bottomPick;
            var match = candidates.FirstOrDefault(c => c.Name == target)
                        ?? (candidates.Count > 0 ? candidates[0] : null);
            return Task.FromResult<ICard?>(match);
        }

        // ---- unused decision hooks ------------------------------------
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
