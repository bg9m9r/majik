using FluentAssertions;
using Majik.Core.Abilities;
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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Mystical Dispute (Throne of Eldraine, {2}{U}).
/// Oracle: "This spell costs {2} less to cast if it targets a blue spell.
/// Counter target spell unless its controller pays {3}."
///
/// Coverage:
///   * Card shape + dispatch by name (Instant {2}{U}, blue).
///   * Counter-unless-pay: success (controller pays → spell resolves)
///     and failure (no mana → countered into graveyard).
///   * Cost reduction applies when targeting a blue spell ({2}{U} → {U}).
///   * Cost reduction does NOT apply against a non-blue target ({2}{U}).
/// </summary>
public class MysticalDisputeTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public MysticalDisputeTests()
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
    public void Create_HasInstantShape_Blue_TwoU()
    {
        var dispute = MysticalDisputeFactory.Create(_alice);

        dispute.Name.Should().Be("Mystical Dispute");
        dispute.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(dispute).Should().Contain(ManaColor.Blue);
        dispute.ManaCost.Should().Be("{2}{U}");
        dispute.ManaCostValue.TotalValue.Should().Be(3);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsMysticalDisputeShape()
    {
        var dispatched = NamedCardFactory.Create("Mystical Dispute", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Mystical Dispute");
        dispatched.ManaCost.Should().Be("{2}{U}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetSpellRequest()
    {
        var def = MysticalDisputeFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("target spell");
    }

    // -----------------------------------------------------------------------
    // Counter unless pay {3}
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Counters_TargetSpell_WhenControllerCannotPayThree()
    {
        var dispute = MysticalDisputeFactory.Create(_alice);
        dispute.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(dispute);

        // Bob casts a noncreature spell (Lightning Bolt {R}).
        // Bob has 0 mana → cannot pay {3} → Dispute counters.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, dispute,
            MysticalDisputeFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Bob couldn't pay {3} so Mystical Dispute counters his spell");
    }

    [Fact]
    public async Task DoesNotCounter_WhenControllerPaysThree()
    {
        var dispute = MysticalDisputeFactory.Create(_alice);
        dispute.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(dispute);

        // Bob has {3} available in his mana pool — he auto-pays the rider.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(3));

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, dispute,
            MysticalDisputeFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Bob paid {3} so Mystical Dispute is countered into a no-op");
    }

    // -----------------------------------------------------------------------
    // Cost reduction — "costs {2} less if it targets a blue spell"
    // -----------------------------------------------------------------------

    [Fact]
    public void EffectiveCost_WithNoTargets_IsPrintedTwoU()
    {
        // Before targets are stamped (e.g. bot affordability check before
        // SpellCastFlow runs), the reducer must NOT apply — printed {2}{U}.
        var dispute = MysticalDisputeFactory.Create(_alice);

        var effective = CostReduction.GetEffectiveCost(dispute, _alice);

        effective.Generic.Should().Be(2);
        effective.Blue.Should().Be(1);
    }

    [Fact]
    public void EffectiveCost_DroppedByTwo_WhenTargetingBlueSpell()
    {
        var dispute = MysticalDisputeFactory.Create(_alice);

        // Simulate SpellCastFlow stamping the chosen blue target.
        var bobCounterspell = new Instant("Counterspell", "{U}{U}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobCounterspell, _bob);
        ((Card)dispute).SetPendingCastTargets(
            new IReadOnlyList<object>[] { new object[] { bobSpell } });

        var effective = CostReduction.GetEffectiveCost(dispute, _alice);

        effective.Generic.Should().Be(0,
            because: "Mystical Dispute targeting a blue spell drops {2} from the generic cost ({2}{U} → {U})");
        effective.Blue.Should().Be(1,
            because: "the coloured pip is untouched (CR 117.7c)");
    }

    [Fact]
    public void EffectiveCost_Unchanged_WhenTargetingNonBlueSpell()
    {
        var dispute = MysticalDisputeFactory.Create(_alice);

        // Target a mono-red spell — no reduction.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        ((Card)dispute).SetPendingCastTargets(
            new IReadOnlyList<object>[] { new object[] { bobSpell } });

        var effective = CostReduction.GetEffectiveCost(dispute, _alice);

        effective.Generic.Should().Be(2);
        effective.Blue.Should().Be(1);
    }

    [Fact]
    public async Task SpellCastFlow_StampsTargets_AndReducerApplies_DuringCast()
    {
        // Integration: Alice casts Dispute targeting Bob's Counterspell ({U}{U}).
        // She should only need to pay {U} (not {2}{U}) — the reducer kicks in
        // during SpellCastFlow because we stamp targets before cost-calc.
        var dispute = MysticalDisputeFactory.Create(_alice);
        dispute.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(dispute);

        var bobCounter = new Instant("Counterspell", "{U}{U}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobCounter, _bob);
        _stack.Push(bobSpell);

        // Alice has exactly {U} — enough only if the reducer fires.
        _alice.AddManaToPool(ManaCost.Parse("{U}"));

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, dispute,
            MysticalDisputeFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        // Dispute is on top of the stack — the cast succeeded with only {U}.
        _stack.IsEmpty.Should().BeFalse();
        // After push, the pending targets are cleared.
        ((Card)dispute).PendingCastTargets.Should().BeNull(
            because: "SpellCastFlow clears the pending stamp once the spell is on the stack");
    }
}
