using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Misdirection (Mercadian Masques, {2}{U}{U}).
/// Mirrors the Snapback / Force-of-Negation test shape:
///   * Card shape + dispatch.
///   * Pitch cast — exiles a blue card, redirects top single-target spell.
///   * Resolve with empty stack → redirect no-ops cleanly.
///   * Resolve with multi-target spell on top → redirect skips it.
/// </summary>
public class MisdirectionFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public MisdirectionFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue()
    {
        var mis = MisdirectionFactory.Create(_alice);

        mis.Name.Should().Be("Misdirection");
        mis.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(mis).Should().Contain(ManaColor.Blue);
        mis.ManaCostValue.TotalValue.Should().Be(4);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsMisdirectionShape()
    {
        var dispatched = NamedCardFactory.Create("Misdirection", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Misdirection");
    }

    [Fact]
    public async Task CastViaPitch_ExilesBlueCard_RedirectsTopSingleTargetSpell()
    {
        var mis = MisdirectionFactory.Create(_alice);
        mis.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mis);

        var blueFuel = new Instant("Counterspell", "{U}{U}") { Owner = _alice };
        blueFuel.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(blueFuel);

        // Bob casts Lightning Bolt targeting Alice. Spell sits on the stack
        // with one chosen target (Alice). Misdirection redirects to Bob.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        bobSpell.ChosenTargets.Add(_alice);
        _stack.Push(bobSpell);

        var pitchCost = new ExileColoredCardAlternativeCost(ManaColor.Blue, blueFuel);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)_bob });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, mis,
            MisdirectionFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: pitchCost);

        _resolver.ResolveTop(_stack);

        blueFuel.Zone.Should().Be(ZoneType.Exile,
            because: "pitched blue card is exiled (CR 117.11 + CR 701.21)");
        bobSpell.ChosenTargets.Should().HaveCount(1);
        bobSpell.ChosenTargets[0].Should().BeSameAs(_bob,
            because: "Misdirection rewrites the top single-target spell's pick (v1 stub)");
    }

    [Fact]
    public async Task CastWithPrintedMana_RedirectsTopSingleTargetSpell()
    {
        var mis = MisdirectionFactory.Create(_alice);
        mis.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mis);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        bobSpell.ChosenTargets.Add(_alice);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)_bob });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, mis,
            MisdirectionFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobSpell.ChosenTargets[0].Should().BeSameAs(_bob);
    }

    [Fact]
    public void Resolve_WithMultiTargetSpellOnStack_DoesNotRewrite()
    {
        // SpellRedirector only rewrites spells with exactly one ChosenTarget
        // — multi-target spells are skipped.
        var bobIncinerate = new Instant("Boros Charm", "{R}{W}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobIncinerate, _bob);
        bobSpell.ChosenTargets.Add(_alice);
        bobSpell.ChosenTargets.Add(_bob);
        _stack.Push(bobSpell);

        var def = MisdirectionFactory.BuildDefinition(o => o, _stack);
        var picks = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new object[] { _alice } },
            ManaPayment.Empty);
        var effects = def.EffectFactory(picks);
        foreach (var e in effects) e.Execute();

        // Multi-target spell — picks remain (no rewrite).
        bobSpell.ChosenTargets.Should().HaveCount(2);
        bobSpell.ChosenTargets[0].Should().BeSameAs(_alice);
        bobSpell.ChosenTargets[1].Should().BeSameAs(_bob);
    }

    [Fact]
    public void Resolve_EmptyStack_IsNoOp()
    {
        // No spells on the stack → redirector no-ops cleanly.
        var def = MisdirectionFactory.BuildDefinition(o => o, _stack);
        var picks = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new object[] { _bob } },
            ManaPayment.Empty);
        var effects = def.EffectFactory(picks);
        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();
    }
}
