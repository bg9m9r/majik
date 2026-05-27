using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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
/// Unit tests for <see cref="AnticipateFactory"/>.
///
/// Anticipate (Magic Origins / Fate Reforged, {1}{U}, Instant):
///   "Look at the top three cards of your library. Put one of them into
///    your hand and the rest on the bottom of your library in any order."
///
/// Covers:
///   - Card identity: name, instant type, {1}{U} mana cost, blue, owner/controller.
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Default resolve (no agent) — top card to hand, second and third
///     cards to bottom of library in order.
///   - Agent picks the SECOND peeked card — second to hand, top and
///     third to bottom.
///   - Agent picks the THIRD peeked card — third to hand, top and
///     second to bottom.
///   - Two-card library: peek returns 2 cards; pick goes to hand, other
///     to bottom. Net library size = 1.
///   - One-card library: single card goes to hand; library ends empty.
///   - Empty library: effect is a no-op (no draw clause → no empty-library
///     SBA flag).
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class AnticipateFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void Anticipate_HasExpectedShape()
    {
        var card = AnticipateFactory.Create(_alice);

        card.Name.Should().Be("Anticipate");
        card.ManaCost.Should().Be("{1}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Anticipate()
    {
        var card = NamedCardFactory.Create("Anticipate", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Anticipate");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // ── Resolve — default (no agent) ─────────────────────────────────────────

    [Fact]
    public void Anticipate_Resolve_NoAgent_TakesTop_BottomsSecondAndThird()
    {
        // Library: [top, second, third, fourth].
        // No agent → deterministic first-card pick:
        //   top → hand; second, third → bottom (in that order).
        //   Library ends [fourth, second, third].
        var top    = SeedLibraryCard("Top");
        var second = SeedLibraryCard("Second");
        var third  = SeedLibraryCard("Third");
        var fourth = SeedLibraryCard("Fourth");

        var effect = AnticipateFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { top });
        // fourth stays in place at index 0; second and third are appended to bottom
        _alice.Zones.Library.GetCards().Should().HaveCount(3);
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(fourth);
        _alice.Zones.Hand.GetCards().Should().NotContain(second);
        _alice.Zones.Hand.GetCards().Should().NotContain(third);
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();

        top.Zone.Should().Be(ZoneType.Hand);
        second.Zone.Should().Be(ZoneType.Library);
        third.Zone.Should().Be(ZoneType.Library);
        fourth.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void Anticipate_Resolve_NoAgent_NetLibrarySizeMinusOne()
    {
        // Confirm net library size: started 4, ended 3 (one to hand).
        SeedLibraryCard("A");
        SeedLibraryCard("B");
        SeedLibraryCard("C");
        SeedLibraryCard("D");

        var libBefore = _alice.Zones.Library.GetCards().Count();
        var effect = AnticipateFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Library.GetCards().Should().HaveCount(libBefore - 1);
        _alice.Zones.Hand.GetCards().Should().HaveCount(1);
    }

    // ── Resolve — agent picks ────────────────────────────────────────────────

    [Fact]
    public void Anticipate_Resolve_AgentPicksSecond_TakesSecond_BottomsOthers()
    {
        // Library: [top, second, third, fourth].
        // Agent picks 'second' → second to hand; top and third to bottom.
        var top    = SeedLibraryCard("Top");
        var second = SeedLibraryCard("Second");
        var third  = SeedLibraryCard("Third");
        var fourth = SeedLibraryCard("Fourth");

        AgentRegistry.Set(_alice, new PickByNameAgent("Second"));

        var effect = AnticipateFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { second });
        // fourth unchanged at head of remaining library
        _alice.Zones.Library.GetCards().Should().HaveCount(3);
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(fourth);
        _alice.Zones.Hand.GetCards().Should().NotContain(top);
        _alice.Zones.Hand.GetCards().Should().NotContain(third);

        second.Zone.Should().Be(ZoneType.Hand);
        top.Zone.Should().Be(ZoneType.Library);
        third.Zone.Should().Be(ZoneType.Library);
        fourth.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void Anticipate_Resolve_AgentPicksThird_TakesThird_BottomsOthers()
    {
        // Library: [top, second, third, fourth].
        // Agent picks 'third' → third to hand; top and second to bottom.
        var top    = SeedLibraryCard("Top");
        var second = SeedLibraryCard("Second");
        var third  = SeedLibraryCard("Third");
        var fourth = SeedLibraryCard("Fourth");

        AgentRegistry.Set(_alice, new PickByNameAgent("Third"));

        var effect = AnticipateFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { third });
        _alice.Zones.Library.GetCards().Should().HaveCount(3);
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(fourth);

        third.Zone.Should().Be(ZoneType.Hand);
        top.Zone.Should().Be(ZoneType.Library);
        second.Zone.Should().Be(ZoneType.Library);
        fourth.Zone.Should().Be(ZoneType.Library);
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [Fact]
    public void Anticipate_Resolve_TwoCardLibrary_TakesTop_BottomsSecond()
    {
        // Library has two cards. Peek returns [a, b]; a goes to hand, b to bottom.
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");

        var effect = AnticipateFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { b });
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void Anticipate_Resolve_SingleCardLibrary_TakesIt_NothingToBottom()
    {
        // Library has one card. Peek returns [a]; that card goes to hand;
        // there's no "rest" to bottom. Library ends empty; no empty-draw flag.
        var a = SeedLibraryCard("A");

        var effect = AnticipateFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a });
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void Anticipate_Resolve_EmptyLibrary_NoOp_NoDrawFromEmptyFlag()
    {
        // Empty library: effect short-circuits — Anticipate has no draw clause,
        // so the empty-library SBA does NOT fire.
        var effect = AnticipateFactory.BuildResolveEffect(_alice).Single();
        Action act = () => effect.Execute();

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Card SeedLibraryCard(string name)
    {
        var c = new Card(name, "");
        c.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    /// <summary>
    /// Test-only agent that resolves <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>
    /// by matching a candidate's <see cref="ICard.Name"/>. Falls back to the
    /// deterministic first-candidate default when no match is found. Other
    /// decision hooks throw to flag accidental calls.
    /// </summary>
    private sealed class PickByNameAgent : IPlayerAgent
    {
        private readonly string _name;
        public PickByNameAgent(string name) { _name = name; }

        public Task<ICard?> ChooseLibraryPickAsync(
            GameContext? ctx,
            IReadOnlyList<ICard> candidates,
            string kindLabel,
            CancellationToken ct = default)
        {
            var match = candidates.FirstOrDefault(c => c.Name == _name)
                        ?? (candidates.Count > 0 ? candidates[0] : null);
            return Task.FromResult<ICard?>(match);
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
