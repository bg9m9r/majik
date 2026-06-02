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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Fact or Fiction (Invasion / reprints, {3}{U}, Instant).
///
/// Oracle: "Reveal the top five cards of your library. An opponent
/// separates those cards into two piles. Put one pile into your hand
/// and the other into your graveyard."
///
/// Coverage:
/// - Identity (name, type, cost, colour) + NamedCardFactory dispatch.
/// - SpellDefinition shape: 1..1 target opponent, no modes, no X.
/// - Full resolution: opponent splits 5 revealed into two piles; caster
///   keeps one pile (hand) and the other goes to graveyard; combined = 5,
///   nothing left in the library.
/// - Opponent stops immediately → empty pile A, all five in pile B
///   (a legal split, CR 700.4).
/// - Caster chooses pile B (declines pile A) → that pile reaches the hand.
/// - Fewer than five cards in library → reveal clamps to library size.
/// - Empty library → clean no-op.
/// - Illegal opponent at resolution → whole effect no-ops.
/// </summary>
public class FactOrFictionTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public FactOrFictionTests()
    {
        AgentRegistry.Clear();
    }

    private void SeedLibrary(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var c = new Sorcery($"Card{i}", "{1}");
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    // ---------------------------------------------------------------
    // Identity / dispatch
    // ---------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_BlueFourMana()
    {
        var card = FactOrFictionFactory.Create(_alice);

        card.Name.Should().Be("Fact or Fiction");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{3}{U}");
        card.ManaCostValue.TotalValue.Should().Be(4);
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsFactOrFictionShape()
    {
        var dispatched = NamedCardFactory.Create("Fact or Fiction", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Fact or Fiction");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
        dispatched.ManaCost.Should().Be("{3}{U}");
    }

    // ---------------------------------------------------------------
    // SpellDefinition shape
    // ---------------------------------------------------------------

    [Fact]
    public void BuildDefinition_HasOneTargetOpponentSlot_NoModesNoX()
    {
        var def = FactOrFictionFactory.BuildDefinition(_alice, raw => raw);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Be("target opponent");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
    }

    // ---------------------------------------------------------------
    // Full resolution — 5 revealed, split into two piles, all distributed
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_FiveRevealed_SplitIntoTwoPiles_AllDistributed_NoneLeftInLibrary()
    {
        SeedLibrary(5);

        AgentRegistry.Set(_alice, new DeterministicBotAgent());
        AgentRegistry.Set(_bob, new DeterministicBotAgent());

        var def = FactOrFictionFactory.BuildDefinition(_alice, raw => raw);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        // All five revealed cards end up split between hand + graveyard; none
        // remain in the library (CR 700.4 — every revealed card goes into one
        // pile or the other).
        var distributed = _alice.Zones.Hand.Count + _alice.Zones.Graveyard.Count;
        distributed.Should().Be(5);
        _alice.Zones.Library.Count.Should().Be(0);
        // Both halves are accounted for — neither pile vanished.
        (_alice.Zones.Hand.Count + _alice.Zones.Graveyard.Count).Should().Be(5);
    }

    // ---------------------------------------------------------------
    // Opponent stops immediately → empty pile A, all five in pile B
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_OpponentStopsImmediately_AllFiveInOnePile()
    {
        SeedLibrary(5);

        // Bob declines the very first pile-A prompt → pile A empty, pile B = 5.
        AgentRegistry.Set(_alice, new TakePileAAgent());
        AgentRegistry.Set(_bob, new DeclineSplitAgent());

        var def = FactOrFictionFactory.BuildDefinition(_alice, raw => raw);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        // Alice takes pile A (empty) into hand → graveyard gets the 5-card
        // pile B. CR 700.4 — an empty pile is legal.
        _alice.Zones.Hand.Count.Should().Be(0);
        _alice.Zones.Graveyard.Count.Should().Be(5);
        _alice.Zones.Library.Count.Should().Be(0);
    }

    // ---------------------------------------------------------------
    // Caster declines pile A → pile B reaches the hand
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_CasterDeclinesPileA_PileBGoesToHand()
    {
        SeedLibrary(5);

        // Bob puts exactly one card in pile A; pile B = 4.
        AgentRegistry.Set(_alice, new DeclinePileAAgent());
        AgentRegistry.Set(_bob, new OnePileAAgent());

        var def = FactOrFictionFactory.BuildDefinition(_alice, raw => raw);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        // Alice declines pile A (1 card) → pile B (4 cards) goes to hand;
        // pile A goes to graveyard.
        _alice.Zones.Hand.Count.Should().Be(4);
        _alice.Zones.Graveyard.Count.Should().Be(1);
        _alice.Zones.Library.Count.Should().Be(0);
    }

    // ---------------------------------------------------------------
    // Fewer than five cards — reveal clamps to library size
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_LibraryHasThreeCards_OnlyThreeRevealed()
    {
        SeedLibrary(3);

        AgentRegistry.Set(_alice, new DeterministicBotAgent());
        AgentRegistry.Set(_bob, new DeterministicBotAgent());

        var def = FactOrFictionFactory.BuildDefinition(_alice, raw => raw);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        (_alice.Zones.Hand.Count + _alice.Zones.Graveyard.Count).Should().Be(3);
        _alice.Zones.Library.Count.Should().Be(0);
    }

    // ---------------------------------------------------------------
    // Empty library — clean no-op
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_EmptyLibrary_NoCrashNoMutation()
    {
        AgentRegistry.Set(_alice, new DeterministicBotAgent());
        AgentRegistry.Set(_bob, new DeterministicBotAgent());

        var def = FactOrFictionFactory.BuildDefinition(_alice, raw => raw);
        var act = () =>
        {
            var effects = def.EffectFactory(new ChosenSpellParams(
                ModeIndex: null, X: null,
                Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
                Mana: ManaPayment.Empty));
            foreach (var e in effects) e.Execute();
        };

        act.Should().NotThrow();
        _alice.Zones.Hand.Count.Should().Be(0);
        _alice.Zones.Graveyard.Count.Should().Be(0);
    }

    // ---------------------------------------------------------------
    // Illegal opponent at resolution → no-op
    // ---------------------------------------------------------------

    [Fact]
    public void Resolve_IllegalOpponent_NoOps()
    {
        SeedLibrary(5);

        AgentRegistry.Set(_alice, new DeterministicBotAgent());

        // Resolver returns a non-Player object → CR 608.2b illegal target.
        var def = FactOrFictionFactory.BuildDefinition(_alice, raw => "not a player");
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { new object() } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        _alice.Zones.Library.Count.Should().Be(5, "no reveal happened — library untouched.");
        _alice.Zones.Hand.Count.Should().Be(0);
        _alice.Zones.Graveyard.Count.Should().Be(0);
    }

    // ---------------------------------------------------------------
    // Local test agents
    // ---------------------------------------------------------------

    /// <summary>Caster agent: always take pile A into hand.</summary>
    private sealed class TakePileAAgent : StubAgentBase
    {
        public override Task<bool> ChooseYesNoAsync(string question, BotIntent intent, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    /// <summary>Caster agent: decline pile A (take pile B into hand).</summary>
    private sealed class DeclinePileAAgent : StubAgentBase
    {
        public override Task<bool> ChooseYesNoAsync(string question, BotIntent intent, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    /// <summary>Opponent agent: decline the very first split prompt.</summary>
    private sealed class DeclineSplitAgent : StubAgentBase
    {
        public override Task<ICard?> ChooseFromPileAsync(
            Player chooser, IReadOnlyList<ICard> candidates, string pileLabel,
            BotIntent intent, CancellationToken ct = default)
            => Task.FromResult<ICard?>(null);
    }

    /// <summary>Opponent agent: put exactly one card in pile A, then stop.</summary>
    private sealed class OnePileAAgent : StubAgentBase
    {
        private bool _picked;

        public override Task<ICard?> ChooseFromPileAsync(
            Player chooser, IReadOnlyList<ICard> candidates, string pileLabel,
            BotIntent intent, CancellationToken ct = default)
        {
            if (!_picked && candidates.Count > 0)
            {
                _picked = true;
                return Task.FromResult<ICard?>(candidates[0]);
            }
            return Task.FromResult<ICard?>(null);
        }
    }

    /// <summary>
    /// Minimal <see cref="IPlayerAgent"/> base — every decision throws unless a
    /// subclass overrides it. The interface's default methods cover the rest.
    /// </summary>
    private abstract class StubAgentBase : IPlayerAgent
    {
        public virtual Task<bool> ChooseYesNoAsync(string question, BotIntent intent, CancellationToken ct = default)
            => Task.FromResult(true);
        public virtual Task<ICard?> ChooseFromPileAsync(
            Player chooser, IReadOnlyList<ICard> candidates, string pileLabel,
            BotIntent intent, CancellationToken ct = default)
            => Task.FromResult<ICard?>(candidates.Count > 0 ? candidates[0] : null);

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
        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
