using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Dovin's Veto (War of the Spark, {W}{U}).
/// Oracle: "This spell can't be countered. Counter target noncreature spell."
///
/// Coverage:
///   * Card identity ({W}{U} Instant, white+blue, dispatch by name).
///   * "Uncounterable" keyword marker present on card shape (CR 701.5b).
///   * SpellCastFlow stamps Spell.CannotBeCountered when cast → counter attempt
///     is vetoed by OracleSpellBinder.RemoveFromStack (CR 701.5b).
///   * SpellDefinition shape (1 target noncreature spell request).
///   * Counters a noncreature spell → lands in graveyard (CR 701.5).
///   * Target is a creature spell → no-op at resolution (CR 608.2b).
/// </summary>
public class DovinsVetoTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public DovinsVetoTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_WhiteBlue()
    {
        var veto = DovinsVetoFactory.Create(_alice);

        veto.Name.Should().Be("Dovin's Veto");
        veto.HasType(CardType.Instant).Should().BeTrue();
        veto.ManaCost.Should().Be("{W}{U}");
        CardColors.GetColors(veto).Should().Contain(ManaColor.White,
            "Dovin's Veto has white in its cost {W}{U}");
        CardColors.GetColors(veto).Should().Contain(ManaColor.Blue,
            "Dovin's Veto has blue in its cost {W}{U}");
        veto.ManaCostValue.TotalValue.Should().Be(2);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsDovinsVetoShape()
    {
        var dispatched = NamedCardFactory.Create("Dovin's Veto", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Dovin's Veto");
        dispatched.ManaCost.Should().Be("{W}{U}");
    }

    // -----------------------------------------------------------------------
    // Can't be countered — structural marker + enforcement
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasUncounterableKeywordMarker()
    {
        var veto = DovinsVetoFactory.Create(_alice);

        veto.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Uncounterable",
                "CR 701.5b — Dovin's Veto carries the 'Uncounterable' marker so " +
                "SpellCastFlow stamps Spell.CannotBeCountered at cast time");
    }

    [Fact]
    public async Task Cast_StampsCannotBeCountered_OnSpell()
    {
        var veto = DovinsVetoFactory.Create(_alice);
        veto.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(veto);

        // Build a trivial SpellDefinition (no targets needed for this assertion).
        var def = DovinsVetoFactory.BuildSpellDefinition(o => o, null);
        // Override with no target request so we don't need to queue a target.
        def = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => Array.Empty<IEffect>());

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(_alice, veto, def, agent, ctx, alternativeCost: null);

        spell.CannotBeCountered.Should().BeTrue(
            "SpellCastFlow reads the 'Uncounterable' keyword and stamps CannotBeCountered");
    }

    [Fact]
    public async Task DovinsVeto_RemoveFromStack_VetoedWhenCastUncounterable()
    {
        // Dovin's Veto is on the stack — SpellCastFlow should stamp
        // CannotBeCountered = true, and OracleSpellBinder.RemoveFromStack
        // should return false (CR 701.5b veto).
        var veto = DovinsVetoFactory.Create(_alice);
        veto.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(veto);

        var vetoDef = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => Array.Empty<IEffect>());

        var vetoAgent = new ScriptedAgent();
        vetoAgent.QueueMana(ManaPayment.Empty);
        var vetoCtx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);
        var vetoSpell = await _flow.CastAsync(_alice, veto, vetoDef, vetoAgent, vetoCtx, alternativeCost: null);

        // Verify CannotBeCountered was stamped.
        vetoSpell.CannotBeCountered.Should().BeTrue(
            "SpellCastFlow stamps CannotBeCountered for the 'Uncounterable' keyword marker");

        // Simulate a counter-attempt at the stack level — RemoveFromStack
        // must veto the pop (CR 701.5b).
        var removed = OracleSpellBinder.RemoveFromStack(_stack, vetoSpell);

        removed.Should().BeFalse(
            "CR 701.5b — OracleSpellBinder vetoes counter-attempts against uncounterable spells");
        _stack.GetAll().Should().Contain(vetoSpell,
            "Dovin's Veto stays on the stack — it can't be countered");
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetNoncreatureSpellRequest()
    {
        var def = DovinsVetoFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("noncreature");
    }

    // -----------------------------------------------------------------------
    // Counter a noncreature spell
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CountersNoncreatureSpell()
    {
        var veto = DovinsVetoFactory.Create(_alice);
        veto.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(veto);

        // Bob casts a noncreature spell (Lightning Bolt {R}).
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, veto,
            DovinsVetoFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            "Dovin's Veto counters the noncreature spell");
    }

    // -----------------------------------------------------------------------
    // Creature spell — no-op (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DoesNotCounterCreatureSpell()
    {
        var veto = DovinsVetoFactory.Create(_alice);
        veto.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(veto);

        // Bob casts a creature spell (Grizzly Bears {1}{G}).
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, veto,
            DovinsVetoFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — creature spell is an illegal target at resolution.
        bobBear.Zone.Should().NotBe(ZoneType.Graveyard,
            "Dovin's Veto does not counter creature spells");
    }
}
