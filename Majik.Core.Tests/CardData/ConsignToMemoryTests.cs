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
/// End-to-end tests for Consign to Memory (Modern Horizons 3, {U}).
/// Oracle (Scryfall, MH3):
///   "Replicate {1} (...)
///    Counter target triggered ability or colorless spell."
///
/// Coverage:
///   * Identity (Instant {U}, blue).
///   * Dispatcher entry returns the correct shape.
///   * SpellDefinition shape (1 target triggered-ability-or-colorless-spell
///     request).
///   * Counter a colorless spell → lands in graveyard (CR 701.5a).
///   * Counter a triggered ability on the stack → removed (CR 701.5b).
///   * Coloured spell target → no-op at resolution (CR 608.2b).
///   * Activated ability target → no-op at resolution (CR 608.2b — not in
///     the printed oracle predicate).
/// </summary>
public class ConsignToMemoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ConsignToMemoryTests()
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
    public void Create_HasInstantShape_Blue()
    {
        var consign = ConsignToMemoryFactory.Create(_alice);

        consign.Name.Should().Be("Consign to Memory");
        consign.HasType(CardType.Instant).Should().BeTrue();
        consign.ManaCost.Should().Be("{U}");
        CardColors.GetColors(consign).Should().Contain(ManaColor.Blue);
        consign.ManaCostValue.TotalValue.Should().Be(1);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsConsignShape()
    {
        var dispatched = NamedCardFactory.Create("Consign to Memory", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Consign to Memory");
        dispatched.ManaCost.Should().Be("{U}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTriggeredOrColorlessTargetRequest()
    {
        var def = ConsignToMemoryFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("triggered ability");
        def.TargetRequests[0].Description.Should().Contain("colorless");
    }

    // -----------------------------------------------------------------------
    // Counter a colorless spell
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CountersColorlessSpell()
    {
        var consign = ConsignToMemoryFactory.Create(_alice);
        consign.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(consign);

        // Bob casts a colorless spell (a generic-cost Artifact, e.g. Sol
        // Ring stand-in {1}).
        var bobRock = new Instant("Generic Rock", "{1}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobRock, _bob);
        _stack.Push(bobSpell);

        // Sanity-check: spell is colourless.
        CardColors.GetColors(bobRock).Should().BeEmpty();

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, consign,
            ConsignToMemoryFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobRock.Zone.Should().Be(ZoneType.Graveyard,
            because: "Consign to Memory counters the colorless spell (CR 701.5a)");
    }

    // -----------------------------------------------------------------------
    // Counter a triggered ability on the stack
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CountersTriggeredAbilityOnStack()
    {
        var consign = ConsignToMemoryFactory.Create(_alice);
        consign.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(consign);

        // Bob has a triggered ability on the stack (e.g. some ETB
        // trigger). Source is a battlefield card he controls.
        var bobSource = new Creature("Bob's Bear", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };
        var ranEffect = false;
        var trigger = new TriggeredAbility(
            bobSource,
            _bob,
            Triggers.OnEnterBattlefieldSelf(bobSource),
            effects: new IEffect[] { new Effect("eff", () => ranEffect = true) });
        _stack.Push(trigger);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)trigger });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, consign,
            ConsignToMemoryFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        // After Consign resolves, the triggered ability should no longer be
        // on the stack. Consign itself is on top of the stack now; resolve
        // it, then verify the trigger has been removed entirely (and never
        // resolved).
        _resolver.ResolveTop(_stack);

        _stack.GetAll().Should().NotContain(trigger,
            because: "Consign to Memory removes the targeted triggered ability from the stack (CR 701.5b)");
        ranEffect.Should().BeFalse(
            because: "the countered ability's effects never run");
    }

    // -----------------------------------------------------------------------
    // Coloured spell — illegal target
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DoesNotCounterColouredSpell()
    {
        var consign = ConsignToMemoryFactory.Create(_alice);
        consign.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(consign);

        // Bob casts a coloured spell (Lightning Bolt {R}).
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        CardColors.GetColors(bobBolt).Should().Contain(ManaColor.Red);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, consign,
            ConsignToMemoryFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — coloured spell is not in the printed predicate.
        // Consign's effect does nothing; the spell is NOT sent to the
        // graveyard by Consign (it may resolve normally on its own).
        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Consign to Memory does not counter coloured spells");
    }

    // -----------------------------------------------------------------------
    // Activated ability — illegal target (not in printed predicate)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DoesNotCounterActivatedAbility()
    {
        var consign = ConsignToMemoryFactory.Create(_alice);
        consign.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(consign);

        // Bob has an activated ability on the stack.
        var bobSource = new Creature("Bob's Pinger", "{1}{U}", 1, 1)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };
        var ranEffect = false;
        var ability = new ActivatedAbility(
            bobSource,
            _bob,
            effects: new IEffect[] { new Effect("eff", () => ranEffect = true) });
        _stack.Push(ability);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)ability });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, consign,
            ConsignToMemoryFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — activated abilities are not in Consign's printed
        // predicate. The ability remains on the stack (Consign's effect
        // is a clean no-op).
        _stack.GetAll().Should().Contain(ability,
            because: "Consign to Memory does not counter activated abilities — only triggered abilities");
        ranEffect.Should().BeFalse(
            because: "the activated ability hasn't resolved yet (still on stack)");
    }
}
