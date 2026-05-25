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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SleightOfHandFactory"/>.
///
/// Sleight of Hand (Portal / Modern Horizons 3, {U}, Sorcery):
///   "Look at the top two cards of your library. Put one of them into your
///    hand and the other on the bottom of your library."
///
/// Covers:
///   - Card identity (name, sorcery type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Default resolve (no agent registered) — top card to hand, second
///     card to bottom of library.
///   - Agent picks the SECOND peeked card via
///     <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> — second card
///     to hand, first card to bottom of library.
///   - One-card-library: single card goes to hand; nothing to bottom.
///   - Empty library: no-op (oracle text never reaches a draw clause, so
///     no empty-library SBA flag fires).
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class SleightOfHandTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    [Fact]
    public void SleightOfHand_HasExpectedShape()
    {
        var card = SleightOfHandFactory.Create(_alice);

        card.Name.Should().Be("Sleight of Hand");
        card.ManaCost.Should().Be("{U}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SleightOfHand()
    {
        var card = NamedCardFactory.Create("Sleight of Hand", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Sleight of Hand");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SleightOfHand_Resolve_NoAgent_TakesTop_BottomsSecond()
    {
        // Library: [top, second, third]. No agent registered → default
        // picks the first peeked card (`top`) for hand; the OTHER peeked
        // card (`second`) gets bottomed. Library ends [third, second];
        // top is in hand.
        var top = SeedLibraryCard("Top");
        var second = SeedLibraryCard("Second");
        var third = SeedLibraryCard("Third");

        var effect = SleightOfHandFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { top });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { third, second });
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        top.Zone.Should().Be(ZoneType.Hand);
        second.Zone.Should().Be(ZoneType.Library);
        third.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void SleightOfHand_Resolve_AgentPicksSecond_TakesSecond_BottomsTop()
    {
        // Library: [top, second, third]. Agent picks `second` for hand;
        // `top` gets bottomed. Library ends [third, top]; second is in hand.
        var top = SeedLibraryCard("Top");
        var second = SeedLibraryCard("Second");
        var third = SeedLibraryCard("Third");

        // ScriptedAgent's default library-pick returns the FIRST candidate;
        // use a local agent that picks `Second` instead. Models a remote /
        // human controller actively reaching for the deeper card (e.g.
        // because the top card is a land they already have on board).
        AgentRegistry.Set(_alice, new PickByNameAgent("Second"));

        var effect = SleightOfHandFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { second });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { third, top });
        second.Zone.Should().Be(ZoneType.Hand);
        top.Zone.Should().Be(ZoneType.Library);
        third.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void SleightOfHand_Resolve_SingleCardLibrary_TakesIt_NothingToBottom()
    {
        // Library has one card. Peek returns [a]; that card goes to hand;
        // there's no "other" to bottom. Library ends empty; no empty-draw
        // flag (Sleight of Hand has no "draw" clause).
        var a = SeedLibraryCard("A");

        var effect = SleightOfHandFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a });
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void SleightOfHand_Resolve_EmptyLibrary_NoOp_NoDrawFromEmptyFlag()
    {
        // No library cards. Effect short-circuits — no draw clause means
        // the empty-library SBA does NOT fire (unlike Opt / Consider /
        // Preordain / Ponder, which all have an explicit draw tail).
        var effect = SleightOfHandFactory.BuildResolveEffect(_alice).Single();
        Action act = () => effect.Execute();

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

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
    /// by matching a candidate's <see cref="ICard.Name"/>. Falls back to
    /// the deterministic first-candidate default (matching the legacy
    /// pre-agent posture) when no match is found. Other decision hooks
    /// throw to flag accidental calls from future engine changes.
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
