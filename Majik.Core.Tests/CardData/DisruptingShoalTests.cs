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
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Disrupting Shoal (Betrayers of Kamigawa, {X}{U}{U}).
///
/// Oracle:
///   "You may exile a blue card with mana value X from your hand rather
///    than pay this spell's mana cost.
///    Counter target spell if its mana value is X."
///
/// Coverage:
/// - Identity / dispatch.
/// - SpellDefinition shape (HasVariableX = true, 1 target spell).
/// - Resolve: counter when target mv == X.
/// - Resolve: no-op when target mv != X.
/// - Pitch alt cost: builds via convenience helper; mv mismatch rejects.
/// - Pitch alt cost: OverrideX surfaces the pitched card's mv.
/// - Bot probe: DefaultLookup recognizes Disrupting Shoal.
/// - End-to-end pitched cast: blue mv-2 pitch counters mv-2 target.
/// </summary>
public class DisruptingShoalTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public DisruptingShoalTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Identity / dispatch ───────────────────────────────────────────

    [Fact]
    public void Create_HasInstantShape_Blue_XCost()
    {
        var ds = DisruptingShoalFactory.Create(_alice);

        ds.Name.Should().Be("Disrupting Shoal");
        ds.HasType(CardType.Instant).Should().BeTrue();
        ds.ManaCost.Should().Be("{X}{U}{U}");
        CardColors.GetColors(ds).Should().Contain(ManaColor.Blue);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsDisruptingShoalShape()
    {
        var dispatched = NamedCardFactory.Create("Disrupting Shoal", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Disrupting Shoal");
        dispatched.ManaCost.Should().Be("{X}{U}{U}");
    }

    [Fact]
    public void SpellDefinition_HasVariableX_SingleTargetSpellRequest()
    {
        var def = DisruptingShoalFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeTrue();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Be("target spell");
    }

    // ── Resolve-time mv-match ─────────────────────────────────────────

    [Fact]
    public void Counters_WhenTargetManaValueEqualsX()
    {
        var stack = new Majik.Core.Stack.Stack();

        // Bob casts a 2-mv instant.
        var bobCounter = new Instant("Counterspell", "{U}{U}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobCounter, _bob);
        stack.Push(bobSpell);

        var def = DisruptingShoalFactory.BuildSpellDefinition(raw => raw, stack);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: 2,
            Targets: new IReadOnlyList<object>[] { new object[] { bobSpell } },
            Mana: ManaPayment.Empty));
        foreach (var e in effects) e.Execute();

        bobCounter.Zone.Should().Be(ZoneType.Graveyard,
            because: "Target mv (2) == X (2); counter resolves.");
    }

    [Fact]
    public void DoesNotCounter_WhenTargetManaValueDiffersFromX()
    {
        var stack = new Majik.Core.Stack.Stack();

        // Bob casts a 1-mv instant; X = 2.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        stack.Push(bobSpell);

        var def = DisruptingShoalFactory.BuildSpellDefinition(raw => raw, stack);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: 2,
            Targets: new IReadOnlyList<object>[] { new object[] { bobSpell } },
            Mana: ManaPayment.Empty));
        foreach (var e in effects) e.Execute();

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Target mv (1) != X (2); the effect does nothing.");
    }

    // ── Pitch alt cost helpers ────────────────────────────────────────

    [Fact]
    public void BuildPitchAltCost_BluePitchOfMvX_IsLegal()
    {
        var ds = DisruptingShoalFactory.Create(_alice);
        ds.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(ds);

        var blueTwo = new Instant("Counterspell", "{U}{U}") { Owner = _alice };
        blueTwo.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(blueTwo);

        var alt = DisruptingShoalFactory.BuildPitchAltCost(blueTwo, x: 2);

        alt.RequiredColor.Should().Be(ManaColor.Blue);
        alt.RequiredManaValue.Should().Be(2);
        alt.OverrideX.Should().Be(2);
        alt.AlternativeManaCost.Should().Be(ManaCost.Zero);
        alt.CanCastFor(ds, _alice).Should().BeTrue();
    }

    [Fact]
    public void BuildPitchAltCost_PitchMvMismatch_Rejects()
    {
        var ds = DisruptingShoalFactory.Create(_alice);
        ds.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(ds);

        // Pitch has mv 1, declared X = 2 → invalid.
        var blueOne = new Instant("Brainstorm", "{U}") { Owner = _alice };
        blueOne.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(blueOne);

        var alt = DisruptingShoalFactory.BuildPitchAltCost(blueOne, x: 2);

        alt.CanCastFor(ds, _alice).Should().BeFalse();
    }

    [Fact]
    public void PitchAltCost_HasNoTurnRestriction_OnOwnerTurn()
    {
        var ds = DisruptingShoalFactory.Create(_alice);
        ds.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(ds);

        var blueTwo = new Instant("Counterspell", "{U}{U}") { Owner = _alice };
        blueTwo.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(blueTwo);

        var alt = DisruptingShoalFactory.BuildPitchAltCost(blueTwo, x: 2);

        // CR 107.3b — Shoal-cycle pitch has no "not your turn" gate.
        alt.IsLegalInContext(_alice).Should().BeTrue();
        alt.IsLegalInContext(_bob).Should().BeTrue();
    }

    [Fact]
    public void PitchAltCostProbe_DefaultLookup_RecognizesDisruptingShoal()
    {
        var ds = DisruptingShoalFactory.Create(_alice);
        var desc = PitchAltCostProbe.DefaultLookup(ds);

        desc.Should().NotBeNull();
        desc!.Value.RequiredColor.Should().Be(ManaColor.Blue);
        desc.Value.LifeCost.Should().Be(0);
    }

    // ── End-to-end pitched cast ───────────────────────────────────────

    [Fact]
    public async Task PitchedCast_BluePitchMv2_CountersMv2Target()
    {
        // Alice has Disrupting Shoal + a blue mv-2 card in hand.
        var ds = DisruptingShoalFactory.Create(_alice);
        ds.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(ds);

        var pitchCard = new Instant("Counterspell", "{U}{U}") { Owner = _alice };
        pitchCard.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitchCard);

        // Bob casts a mv-2 spell on his turn.
        var bobNegate = new Instant("Negate", "{1}{U}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobNegate, _bob);
        _stack.Push(bobSpell);

        var altCost = DisruptingShoalFactory.BuildPitchAltCost(pitchCard, x: 2);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        // NB: no agent.QueueX — SpellCastFlow uses altCost.OverrideX.

        // It's Bob's turn — Disrupting Shoal can still be cast (no turn gate).
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, ds,
            DisruptingShoalFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: altCost);

        _resolver.ResolveTop(_stack);

        bobNegate.Zone.Should().Be(ZoneType.Graveyard,
            because: "Pitch supplied X=2; target mv 2 matches; counter resolves.");
        pitchCard.Zone.Should().Be(ZoneType.Exile,
            because: "Pitch alt cost exiles the pitched card on resolve (CR 118.9).");
        _alice.LifeTotal.Should().Be(20,
            because: "Shoal-cycle pitch has no life rider.");
    }
}
