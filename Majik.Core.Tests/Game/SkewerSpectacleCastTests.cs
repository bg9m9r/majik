using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

/// <summary>
/// End-to-end check that Skewer the Critics (CR 702.118 — Spectacle)
/// resolves through <see cref="SpellCastFlow"/> at the spectacle cost
/// when an opponent's <see cref="Player.LifeLostThisTurn"/> &gt; 0, and at
/// the printed cost otherwise.
///
/// Assertion strategy: the test capture-agent records the cost
/// <see cref="SpellCastFlow"/> presents at the mana-payment step. With
/// the alt-cost wired the agent is asked for {R}; without it, {2}{R}.
/// This is the same surface a real bot/agent would query, so passing
/// here proves the engine offered the right cost to the player.
/// </summary>
public class SkewerSpectacleCastTests
{
    private const string SkewerOracle =
        "Spectacle {R} (You may cast this spell for its spectacle cost rather than " +
        "its mana cost if an opponent lost life this turn.)\n" +
        "Skewer the Critics deals 3 damage to any target.";

    /// <summary>Records every cost the engine asked the agent to fund.
    /// Delegates everything else to a wrapped <see cref="ScriptedAgent"/>.
    /// </summary>
    private sealed class CostCapturingAgent : IPlayerAgent
    {
        private readonly ScriptedAgent _inner = new();
        public List<ManaCost> AskedCosts { get; } = new();

        public CostCapturingAgent()
        {
            // Pre-queue an empty payment for whatever cost is asked.
            _inner.QueueMana(ManaPayment.Empty);
        }

        public Task<ManaPayment> ChooseManaSourcesAsync(
            GameContext ctx, ManaCost cost, CancellationToken ct = default)
        {
            AskedCosts.Add(cost);
            return _inner.ChooseManaSourcesAsync(ctx, cost, ct);
        }

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext c, CancellationToken ct = default)
            => _inner.ChoosePriorityActionAsync(c, ct);
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext c, IReadOnlyList<ICard> h, int m, CancellationToken ct = default)
            => _inner.ChooseMulliganAsync(c, h, m, ct);
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext c, IReadOnlyList<ICard> h, int n, CancellationToken ct = default)
            => _inner.ChooseCardsToBottomAsync(c, h, n, ct);
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext c, TargetRequest r, CancellationToken ct = default)
            => _inner.ChooseTargetsAsync(c, r, ct);
        public Task<int> ChooseXAsync(GameContext c, ICard s, CancellationToken ct = default)
            => _inner.ChooseXAsync(c, s, ct);
        public Task<int> ChooseModeAsync(GameContext c, IReadOnlyList<string> m, IReadOnlyList<Majik.Core.Cards.BotIntent>? mi = null, CancellationToken ct = default)
            => _inner.ChooseModeAsync(c, m, mi, ct);
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext c, IReadOnlyList<ITriggeredAbility> m, CancellationToken ct = default)
            => _inner.OrderTriggersAsync(c, m, ct);
        public Task<CombatPlan> DeclareAttackersAsync(GameContext c, IReadOnlyList<Majik.Core.Cards.Creature> e, CancellationToken ct = default)
            => _inner.DeclareAttackersAsync(c, e, ct);
        public Task<BlockPlan> DeclareBlockersAsync(GameContext c, IReadOnlyList<Majik.Core.Cards.Creature> a, IReadOnlyList<Majik.Core.Cards.Creature> e, CancellationToken ct = default)
            => _inner.DeclareBlockersAsync(c, a, e, ct);
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? c, IReadOnlyList<ICard> p, CancellationToken ct = default)
            => _inner.ChooseScryDecisionAsync(c, p, ct);
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? c, IReadOnlyList<ICard> p, CancellationToken ct = default)
            => _inner.ChooseSurveilDecisionAsync(c, p, ct);
        public Task<ICard?> ChooseLibraryPickAsync(GameContext? c, IReadOnlyList<ICard> cs, string label, CancellationToken ct = default)
            => _inner.ChooseLibraryPickAsync(c, cs, label, ct);
    }

    [Fact]
    public async Task Skewer_CastableForR_WhenOpponentLostLifeThisTurn()
    {
        // Arrange: Bob took 2 damage earlier this turn (opponent at <20).
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        bob.LoseLife(2);
        bob.LifeTotal.Should().Be(18);
        bob.LifeLostThisTurn.Should().Be(2);

        var skewer = new Sorcery("Skewer the Critics", "2R")
        { Owner = alice, Zone = ZoneType.Hand };
        alice.Zones.Hand.AddCard(skewer);

        // Binder offers the alt-cost iff some opponent lost life this turn.
        var alt = SpectacleBinder.TryBind(SkewerOracle, alice, new[] { alice, bob });
        alt.Should().NotBeNull("opponent took 2 damage this turn — spectacle eligible");
        alt!.AlternativeManaCost.Should().Be(ManaCost.Parse("R"));

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);
        var agent = new CostCapturingAgent();
        var ctx = new GameContext(alice, new[] { alice, bob }, alice, 1, PhaseStateType.PreCombatMain, stack);

        // Vanilla effects — the damage body is exercised by the Damage
        // spell-template test suite; here we care only about the COST gate.
        var def = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => Array.Empty<IEffect>());

        // Act
        var spell = await flow.CastAsync(alice, skewer, def, agent, ctx, alternativeCost: alt);

        // Assert: engine asked the agent for the SPECTACLE cost ({R}),
        // not the printed cost ({2}{R}). The spell now lives on the stack.
        agent.AskedCosts.Should().ContainSingle();
        agent.AskedCosts[0].Should().Be(ManaCost.Parse("R"));
        stack.Count.Should().Be(1);
        skewer.Zone.Should().Be(ZoneType.Stack);
    }

    [Fact]
    public async Task Skewer_CastableFor2R_WhenNoOpponentLostLife()
    {
        // Arrange: clean turn — nobody has lost life.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        bob.LifeLostThisTurn.Should().Be(0);

        var skewer = new Sorcery("Skewer the Critics", "2R")
        { Owner = alice, Zone = ZoneType.Hand };
        alice.Zones.Hand.AddCard(skewer);

        // Binder declines — no opponent lost life. Caller falls back to
        // the printed cost.
        var alt = SpectacleBinder.TryBind(SkewerOracle, alice, new[] { alice, bob });
        alt.Should().BeNull("no opponent has lost life this turn — spectacle ineligible");

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);
        var agent = new CostCapturingAgent();
        var ctx = new GameContext(alice, new[] { alice, bob }, alice, 1, PhaseStateType.PreCombatMain, stack);

        var def = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => Array.Empty<IEffect>());

        // Act — cast at printed cost (no alt cost passed).
        var spell = await flow.CastAsync(alice, skewer, def, agent, ctx);

        // Assert: engine asked for the PRINTED cost {2}{R}.
        agent.AskedCosts.Should().ContainSingle();
        agent.AskedCosts[0].Should().Be(ManaCost.Parse("2R"));
        stack.Count.Should().Be(1);
    }

    [Fact]
    public async Task Skewer_AfterTurnReset_NoSpectacle()
    {
        // Bob lost life last turn, but TurnDriver-style reset zeroed it.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        bob.LoseLife(5);
        bob.LifeLostThisTurn.Should().Be(5);

        // Simulate turn change.
        alice.ResetTurnTrackers();
        bob.ResetTurnTrackers();
        bob.LifeLostThisTurn.Should().Be(0);

        var skewer = new Sorcery("Skewer the Critics", "2R")
        { Owner = alice, Zone = ZoneType.Hand };
        alice.Zones.Hand.AddCard(skewer);

        var alt = SpectacleBinder.TryBind(SkewerOracle, alice, new[] { alice, bob });
        alt.Should().BeNull("life-loss tracker was reset at turn start");
    }
}
