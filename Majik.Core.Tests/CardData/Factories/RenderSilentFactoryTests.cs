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
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Render Silent (Dragon's Maze, {W}{U}{U}).
/// Oracle: "Counter target spell. Its controller can't cast spells this turn."
///
/// Coverage:
///   * Card shape + dispatch by name ({W}{U}{U} white/blue Instant).
///   * SpellDefinition shape (1 target spell request, no type filter).
///   * Counters a noncreature spell → graveyard (CR 701.5).
///   * Counters a creature spell → graveyard (no noncreature filter).
///   * Countered spell's controller acquires a total-cast block (CR 601.3).
///   * The caster / untargeted players are NOT restricted.
///   * The lockout clears when the card token is removed (CR 514.2).
/// </summary>
[Trait("Color", "WU")]
public class RenderSilentFactoryTests : IDisposable
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public RenderSilentFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
        CastingRestrictions.Clear();
    }

    public void Dispose() => CastingRestrictions.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_WhiteBlue_AtWUU()
    {
        var rs = RenderSilentFactory.Create(_alice);

        rs.Name.Should().Be("Render Silent");
        rs.HasType(CardType.Instant).Should().BeTrue();
        rs.ManaCost.ToString().Should().Be("{W}{U}{U}");
        CardColors.GetColors(rs).Should().Contain(ManaColor.White);
        CardColors.GetColors(rs).Should().Contain(ManaColor.Blue);
        ManaCost.Parse(rs.ManaCost).TotalValue.Should().Be(3,
            "{W}{U}{U} has mana value 3 (CR 202.3)");
        rs.Owner.Should().BeSameAs(_alice);
        rs.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetSpellRequest_NoTypeFilter()
    {
        var card = RenderSilentFactory.Create(_alice);
        var def = RenderSilentFactory.BuildSpellDefinition(card, o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Be("target spell");
    }

    // -----------------------------------------------------------------------
    // Counter body (CR 701.5)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CountersNoncreatureSpell_AndLocksOutController()
    {
        var rs = RenderSilentFactory.Create(_alice);
        rs.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(rs);

        // Bob casts Lightning Bolt — a noncreature spell.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, rs,
            RenderSilentFactory.BuildSpellDefinition(rs, o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Render Silent counters the noncreature spell (CR 701.5)");
        CastingRestrictions.CannotCastAnySpell(_bob).Should().BeTrue(
            because: "the countered spell's controller can't cast spells this turn (CR 601.3)");
    }

    [Fact]
    public async Task CountersCreatureSpell_NoNoncreatureFilter()
    {
        var rs = RenderSilentFactory.Create(_alice);
        rs.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(rs);

        // Bob casts Grizzly Bears — a creature spell. Render Silent has no
        // noncreature filter, so it counters this too.
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, rs,
            RenderSilentFactory.BuildSpellDefinition(rs, o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBear.Zone.Should().Be(ZoneType.Graveyard,
            because: "Render Silent has no noncreature filter — creature spells are countered too");
        CastingRestrictions.CannotCastAnySpell(_bob).Should().BeTrue(
            because: "the countered spell's controller can't cast spells this turn (CR 601.3)");
    }

    // -----------------------------------------------------------------------
    // Lockout rider (CR 601.3 / 514.2)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_OnlyControllerOfCounteredSpellIsRestricted()
    {
        var rs = RenderSilentFactory.Create(_alice);
        var charlie = new Player("Charlie", 20);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var def = RenderSilentFactory.BuildSpellDefinition(rs, o => o, _stack);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { bobSpell } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        CastingRestrictions.CannotCastAnySpell(_bob).Should().BeTrue(
            "the countered spell's controller is locked out (CR 601.3)");
        CastingRestrictions.CannotCastAnySpell(_alice).Should().BeFalse(
            "the caster of Render Silent is not restricted");
        CastingRestrictions.CannotCastAnySpell(charlie).Should().BeFalse(
            "untargeted players are not restricted");
    }

    [Fact]
    public void Resolve_Lockout_ClearedByRemoveToken()
    {
        var rs = RenderSilentFactory.Create(_alice);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var def = RenderSilentFactory.BuildSpellDefinition(rs, o => o, _stack);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { bobSpell } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        CastingRestrictions.CannotCastAnySpell(_bob).Should().BeTrue("restriction active");

        // Simulate end-of-turn cleanup using the card as the source token
        // (CR 514.2 — "this turn" effects expire at cleanup).
        CastingRestrictions.RemoveCannotCastAnySpell(rs);

        CastingRestrictions.CannotCastAnySpell(_bob).Should().BeFalse(
            "restriction cleared after RemoveCannotCastAnySpell(card)");
    }
}
