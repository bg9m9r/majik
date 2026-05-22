using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
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

namespace Majik.Core.Tests.Game;

/// <summary>
/// End-to-end behaviour of the Convoke cost-modifier hook (CR 702.51).
///
/// <para><b>v1 scope:</b> ConvokeAlternativeCost is wired infrastructure
/// but the spell-cast flow does NOT yet consult it for cost reduction —
/// casters still pay the printed mana cost. These tests pin that
/// behaviour explicitly so a follow-up that hooks the real reduction
/// will trip the assertions and force the implementer to update both
/// the engine + this contract.</para>
///
/// <para><see cref="ConvokeAlternativeCost.ReduceCost"/> is a pure
/// function — tests exercise it directly to confirm the deterministic
/// strategy (each tap = one generic OR one coloured pip in WUBRG order)
/// works as documented.</para>
/// </summary>
public class ConvokeTests
{
    private sealed class CostCapturingAgent : IPlayerAgent
    {
        private readonly ScriptedAgent _inner = new();
        public List<ManaCost> AskedCosts { get; } = new();
        public CostCapturingAgent() { _inner.QueueMana(ManaPayment.Empty); }

        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
        { AskedCosts.Add(cost); return _inner.ChooseManaSourcesAsync(ctx, cost, ct); }
        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext c, CancellationToken ct = default) => _inner.ChoosePriorityActionAsync(c, ct);
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext c, IReadOnlyList<ICard> h, int m, CancellationToken ct = default) => _inner.ChooseMulliganAsync(c, h, m, ct);
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext c, IReadOnlyList<ICard> h, int n, CancellationToken ct = default) => _inner.ChooseCardsToBottomAsync(c, h, n, ct);
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext c, TargetRequest r, CancellationToken ct = default) => _inner.ChooseTargetsAsync(c, r, ct);
        public Task<int> ChooseXAsync(GameContext c, ICard s, CancellationToken ct = default) => _inner.ChooseXAsync(c, s, ct);
        public Task<int> ChooseModeAsync(GameContext c, IReadOnlyList<string> m, IReadOnlyList<BotIntent>? mi = null, CancellationToken ct = default) => _inner.ChooseModeAsync(c, m, mi, ct);
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext c, IReadOnlyList<ITriggeredAbility> m, CancellationToken ct = default) => _inner.OrderTriggersAsync(c, m, ct);
        public Task<CombatPlan> DeclareAttackersAsync(GameContext c, IReadOnlyList<Creature> e, CancellationToken ct = default) => _inner.DeclareAttackersAsync(c, e, ct);
        public Task<BlockPlan> DeclareBlockersAsync(GameContext c, IReadOnlyList<Creature> a, IReadOnlyList<Creature> e, CancellationToken ct = default) => _inner.DeclareBlockersAsync(c, a, e, ct);
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? c, IReadOnlyList<ICard> p, CancellationToken ct = default) => _inner.ChooseScryDecisionAsync(c, p, ct);
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? c, IReadOnlyList<ICard> p, CancellationToken ct = default) => _inner.ChooseSurveilDecisionAsync(c, p, ct);
        public Task<ICard?> ChooseLibraryPickAsync(GameContext? c, IReadOnlyList<ICard> cs, string label, CancellationToken ct = default) => _inner.ChooseLibraryPickAsync(c, cs, label, ct);
    }

    [Fact]
    public void ReduceCost_NoCreatures_ReturnsPrintedCostUnchanged()
    {
        var printed = ManaCost.Parse("3WW");
        var reduced = ConvokeAlternativeCost.ReduceCost(printed, Array.Empty<Creature>());
        reduced.Should().Be(printed);
    }

    [Fact]
    public void ReduceCost_NullList_ReturnsPrintedCostUnchanged()
    {
        var printed = ManaCost.Parse("2R");
        var reduced = ConvokeAlternativeCost.ReduceCost(printed, null);
        reduced.Should().Be(printed);
    }

    [Fact]
    public void ReduceCost_ConsumesGenericFirst()
    {
        // Printed {3}{W}{W}, tap 2 creatures → {1}{W}{W} (generic only).
        var printed = ManaCost.Parse("3WW");
        var taps = new[] { Bear(), Bear() };
        var reduced = ConvokeAlternativeCost.ReduceCost(printed, taps);
        reduced.Generic.Should().Be(1);
        reduced.White.Should().Be(2);
    }

    [Fact]
    public void ReduceCost_OverflowConsumesColouredPip()
    {
        // Printed {1}{W}{W}, tap 2 creatures → {0}{W}{W} after 1 generic,
        // then the second creature eats {W} → final {0}{W}.
        var printed = ManaCost.Parse("1WW");
        var taps = new[] { Bear(), Bear() };
        var reduced = ConvokeAlternativeCost.ReduceCost(printed, taps);
        reduced.Generic.Should().Be(0);
        reduced.White.Should().Be(1);
    }

    [Fact]
    public void ReduceCost_DoesNotGoNegative_WhenMoreCreaturesThanCost()
    {
        // Printed {1}{W}, tap 5 creatures → {0}{0} (floored at 0, no neg pips).
        var printed = ManaCost.Parse("1W");
        var taps = new[] { Bear(), Bear(), Bear(), Bear(), Bear() };
        var reduced = ConvokeAlternativeCost.ReduceCost(printed, taps);
        reduced.Generic.Should().Be(0);
        reduced.White.Should().Be(0);
        reduced.TotalValue.Should().Be(0);
    }

    [Fact]
    public async Task SpellCastFlow_CastingConvokeSpell_StillCostsPrintedMana_v1()
    {
        // The v1 contract: ConvokeAlternativeCost.AlternativeManaCost
        // returns the PRINTED cost unchanged, so even though it's passed
        // as the alternative cost, the engine asks for the same mana as
        // it would without Convoke. Pins the lossy behaviour so the
        // follow-up that wires real reduction will trip this and force
        // the implementer to choose.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var spell = new Sorcery("Gather Courage", "G") { Owner = alice, Zone = ZoneType.Hand };
        alice.Zones.Hand.AddCard(spell);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);
        var agent = new CostCapturingAgent();
        var ctxGame = new GameContext(alice, new[] { alice, bob }, alice, 1, PhaseStateType.Main, stack);

        var convoke = new ConvokeAlternativeCost(ManaCost.Parse("G"));
        var def = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => Array.Empty<IEffect>());

        await flow.CastAsync(alice, spell, def, agent, ctxGame, alternativeCost: convoke);

        agent.AskedCosts.Should().ContainSingle();
        agent.AskedCosts[0].Should().Be(ManaCost.Parse("G"),
            "v1 ConvokeAlternativeCost is a no-op cost modifier — caster pays printed cost");
        stack.Count.Should().Be(1);
    }

    [Fact]
    public void ConvokeAlternativeCost_CanCastFor_RequiresHand()
    {
        var alice = new Player("Alice", 20);
        var card = new Sorcery("Gather Courage", "G") { Owner = alice, Zone = ZoneType.Hand };
        alice.Zones.Hand.AddCard(card);
        var convoke = new ConvokeAlternativeCost(ManaCost.Parse("G"));
        convoke.CanCastFor(card, alice).Should().BeTrue();

        // Move to graveyard — Convoke is only legal from hand (no Flashback semantics).
        alice.Zones.Hand.RemoveCard(card);
        alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
        convoke.CanCastFor(card, alice).Should().BeFalse();
    }

    private static Creature Bear() => new("Bear", "1G", 2, 2);
}
