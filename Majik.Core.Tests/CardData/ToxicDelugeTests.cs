using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ToxicDelugeFactory"/>.
///
/// Card: Toxic Deluge — Sorcery {2}{B} (Commander 2013).
///   "As an additional cost to cast this spell, pay X life.
///    All creatures get -X/-X until end of turn."
///
/// v1 model (see <see cref="ToxicDelugeFactory"/> doc): X is chosen by
/// the caller at resolve-build time (the engine doesn't yet expose a
/// spell-time additional-cost hook for sorceries). Life payment is
/// folded into the resolve effect; the bot / future spell-cost pipeline
/// can drive X.
///
/// Covers:
///   - Identity (name, type, mana cost, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve registers a -X/-X PumpUntilEndOfTurnEffect per creature
///     on every supplied player's battlefield (symmetric — CR 109.5).
///   - Caster's life total drops by X when caster is supplied.
///   - Caster=null skips the payment but still sweeps.
///   - Default X (5) wipes creatures with toughness ≤ 5.
///   - Negative X is rejected (CR 107.1b).
/// </summary>
public class ToxicDelugeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ToxicDeluge_Identity()
    {
        var c = ToxicDelugeFactory.Create(_alice);

        c.Name.Should().Be("Toxic Deluge");
        c.ManaCost.Should().Be("{2}{B}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ToxicDeluge()
    {
        var card = NamedCardFactory.Create("Toxic Deluge", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Toxic Deluge");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — life payment
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_PaysX_LifeFromCaster()
    {
        var effects = ToxicDelugeFactory.BuildResolveEffect(
            new[] { _alice, _bob }, caster: _alice, x: 3);
        foreach (var e in effects) e.Execute();

        _alice.LifeTotal.Should().Be(17, "20 - 3 = 17");
        _bob.LifeTotal.Should().Be(20, "Bob isn't the caster");
    }

    [Fact]
    public void Resolve_WithoutCaster_SkipsLifePayment()
    {
        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        var effects = ToxicDelugeFactory.BuildResolveEffect(
            new[] { _alice, _bob }, caster: null, x: 4);
        foreach (var e in effects) e.Execute();

        _alice.LifeTotal.Should().Be(20, "no caster → no payment");
        bear.IsDead().Should().BeTrue("sweep still applies");
    }

    [Fact]
    public void Resolve_CapsLifePayment_AtCurrentLifeTotal()
    {
        // Toxic Deluge can technically be paid for X > LifeTotal in proper
        // rules (CR 119.4 — can't pay life you don't have, so it can't be
        // cast); the v1 fold-into-resolve guards by clamping to LifeTotal
        // so the test doesn't ArgumentOutOfRange on a Player.LoseLife.
        _alice.LoseLife(15); // alice = 5 life

        var effects = ToxicDelugeFactory.BuildResolveEffect(
            new[] { _alice, _bob }, caster: _alice, x: 10);
        foreach (var e in effects) e.Execute();

        _alice.LifeTotal.Should().Be(0, "payment clamped to remaining life");
    }

    // -----------------------------------------------------------------------
    // Resolve — -X/-X sweep
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_Applies_MinusX_Symmetrically_AcrossBothPlayers()
    {
        var aliceBear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var bobBig = NewCreatureOnBattlefield(_bob, "Serra Angel", "{3}{W}{W}", 4, 4);

        var effects = ToxicDelugeFactory.BuildResolveEffect(
            new[] { _alice, _bob }, caster: _alice, x: 3);
        foreach (var e in effects) e.Execute();

        aliceBear.Toughness.Should().Be(-1, "2 - 3 = -1");
        bobBig.Toughness.Should().Be(1, "4 - 3 = 1");
        aliceBear.IsDead().Should().BeTrue();
        bobBig.IsDead().Should().BeFalse("toughness 1 > 0");
    }

    [Fact]
    public void Resolve_DefaultX_Is_Five_AndWipesToughnessFiveOrLess()
    {
        var four = NewCreatureOnBattlefield(_alice, "Air Elemental", "{3}{U}{U}", 4, 4);
        var five = NewCreatureOnBattlefield(_bob, "Sengir Vampire", "{3}{B}{B}", 4, 4); // 4/4
        var six = NewCreatureOnBattlefield(_bob, "Mahamoti Djinn", "{4}{U}{U}", 5, 6);

        var effects = ToxicDelugeFactory.BuildResolveEffect(
            new[] { _alice, _bob }, caster: _alice);
        foreach (var e in effects) e.Execute();

        ToxicDelugeFactory.DefaultX.Should().Be(5);
        _alice.LifeTotal.Should().Be(15, "default X = 5");
        four.IsDead().Should().BeTrue();
        five.IsDead().Should().BeTrue();
        six.IsDead().Should().BeFalse("6 - 5 = 1 toughness, alive");
        six.Toughness.Should().Be(1);
    }

    [Fact]
    public void Resolve_XZero_IsNoOp()
    {
        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        var effects = ToxicDelugeFactory.BuildResolveEffect(
            new[] { _alice, _bob }, caster: _alice, x: 0);
        foreach (var e in effects) e.Execute();

        _alice.LifeTotal.Should().Be(20, "0 life paid");
        bear.IsDead().Should().BeFalse("-0/-0 leaves the bear at 2/2");
        bear.Toughness.Should().Be(2);
    }

    [Fact]
    public void BuildResolveEffect_NegativeX_Throws()
    {
        var act = () => ToxicDelugeFactory.BuildResolveEffect(
            new[] { _alice, _bob }, caster: _alice, x: -1);

        act.Should().Throw<ArgumentOutOfRangeException>(
            "X must be non-negative — CR 107.1b");
    }

    // -----------------------------------------------------------------------
    // End-to-end via SpellCastFlow — PayLifeAdditionalCost retrofit
    // (X paid at cast time per CR 601.2f, not folded into resolve)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SpellCastFlow_ToxicDeluge_X5_DeductsFiveLifeAtCast_SweepsMinus5OnResolve()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var sengir = NewCreatureOnBattlefield(_bob, "Sengir Vampire", "{3}{B}{B}", 4, 4);
        var djinn = NewCreatureOnBattlefield(_bob, "Mahamoti Djinn", "{4}{U}{U}", 5, 6);

        var deluge = ToxicDelugeFactory.Create(_alice);
        deluge.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(deluge);

        var agent = new ScriptedAgent();
        agent.QueueX(5);                         // agent picks X = 5
        agent.QueueMana(ManaPayment.Empty);      // mana skipped — test focuses on life

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, stack);

        var def = ToxicDelugeFactory.BuildSpellDefinition(deluge);
        var spell = await flow.CastAsync(_alice, deluge, def, agent, ctx);

        // Cast-time: X = 5 deducted from Alice BEFORE the spell hit the stack.
        _alice.LifeTotal.Should().Be(15, "20 - 5 = 15, paid at cast time (CR 601.2f)");
        stack.Count.Should().Be(1);

        // Resolve: -5/-5 sweep, no second life deduction (back-compat path
        // gated on caster: null inside BuildSpellDefinition's EffectFactory).
        spell.Resolve();
        _alice.LifeTotal.Should().Be(15, "no double-charge on resolve");
        bear.IsDead().Should().BeTrue("2 - 5 = -3, toughness 0 SBA");
        sengir.IsDead().Should().BeTrue("4 - 5 = -1, toughness 0 SBA");
        djinn.IsDead().Should().BeFalse("6 - 5 = 1, alive");
        djinn.Toughness.Should().Be(1);
    }

    [Fact]
    public async Task SpellCastFlow_ToxicDeluge_RejectsCast_WhenCasterShortOfLife()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        _alice.LoseLife(17); // Alice = 3 life

        var deluge = ToxicDelugeFactory.Create(_alice);
        deluge.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(deluge);

        var agent = new ScriptedAgent();
        agent.QueueX(7); // X = 7, but caster only has 3 life
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, stack);

        var def = ToxicDelugeFactory.BuildSpellDefinition(deluge);
        var act = async () => await flow.CastAsync(_alice, deluge, def, agent, ctx);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "CR 119.4 — Alice has 3 life, can't pay X = 7");
        _alice.LifeTotal.Should().Be(3, "no partial payment (CR 601.2g)");
        deluge.Zone.Should().Be(ZoneType.Hand, "cast rewound — card stays in hand");
        stack.Count.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewCreatureOnBattlefield(
        Player owner, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        c.ActiveEffects = new ContinuousEffectsService();
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
