using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Flare of Duplication (MH3, {1}{R}{R}). Exercises the
/// card's UNIQUE behaviour:
///   * Shape: Instant + red + MV 3.
///   * Alternative cost: sacrifice a nontoken red creature you control instead
///     of paying {1}{R}{R} (red sibling of Flare of Denial).
///   * Filters: token / non-red / opponent-controlled creatures are illegal.
///   * Resolve: "copy target instant or sorcery spell" puts a distinct copy on
///     the stack (CR 707.10 / 706.10a); the sacrificed creature moves
///     battlefield → graveyard.
///   * Bot probe surfaces eligible nontoken red creature candidates only.
/// </summary>
[Trait("Color", "R")]
public class FlareOfDuplicationFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public FlareOfDuplicationFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Shape ────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasInstantShape_Red_ManaValue3()
    {
        var flare = FlareOfDuplicationFactory.Create(_alice);

        flare.Name.Should().Be("Flare of Duplication");
        flare.HasType(CardType.Instant).Should().BeTrue();
        flare.ManaCost.Should().Be("{1}{R}{R}");
        CardColors.GetColors(flare).Should().Contain(ManaColor.Red);
        flare.ManaCostValue.TotalValue.Should().Be(3);
    }

    // ── Alternative cost — CanCastFor ────────────────────────────────────────

    [Fact]
    public void AltCost_CanCastFor_NontokenRedCreature_ControlledByCaster_IsLegal()
    {
        var flare = FlareOfDuplicationFactory.Create(_alice);
        var goblin = MakeRedCreature("Goblin Guide", _alice, isToken: false);

        var altCost = new SacrificeNontokenRedCreatureAlternativeCost(goblin);

        altCost.CanCastFor(flare, _alice).Should().BeTrue();
    }

    [Fact]
    public void AltCost_CanCastFor_TokenRedCreature_IsIllegal()
    {
        var flare = FlareOfDuplicationFactory.Create(_alice);
        var elemental = MakeRedCreature("Elemental Token", _alice, isToken: true);

        var altCost = new SacrificeNontokenRedCreatureAlternativeCost(elemental);

        altCost.CanCastFor(flare, _alice).Should().BeFalse(
            because: "tokens are excluded per oracle text");
    }

    [Fact]
    public void AltCost_CanCastFor_NontokenBlueCreature_IsIllegal()
    {
        var flare = FlareOfDuplicationFactory.Create(_alice);
        var merfolk = MakeBlueCreature("Merfolk", _alice);

        var altCost = new SacrificeNontokenRedCreatureAlternativeCost(merfolk);

        altCost.CanCastFor(flare, _alice).Should().BeFalse(
            because: "the creature must be red");
    }

    [Fact]
    public void AltCost_CanCastFor_RedCreatureControlledByOpponent_IsIllegal()
    {
        var flare = FlareOfDuplicationFactory.Create(_alice);
        var bobGoblin = MakeRedCreature("Goblin Spy", _bob, isToken: false);

        var altCost = new SacrificeNontokenRedCreatureAlternativeCost(bobGoblin);

        altCost.CanCastFor(flare, _alice).Should().BeFalse(
            because: "the sacrificed creature must be controlled by the caster");
    }

    // ── Alternative cost — resolve (sacrifice + copy path) ───────────────────

    [Fact]
    public async System.Threading.Tasks.Task CastViaSacrifice_CopiesTargetSpell_AndSacrificesCreature()
    {
        var flare = FlareOfDuplicationFactory.Create(_alice);
        flare.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(flare);

        var goblin = MakeRedCreature("Goblin Guide", _alice, isToken: false);

        // Bob's instant on the stack — Flare of Duplication's copy target.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        bobBolt.SetZone(ZoneType.Stack);
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var altCost = new SacrificeNontokenRedCreatureAlternativeCost(goblin);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2,
            StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, flare,
            FlareOfDuplicationFactory.BuildSpellDefinition(o => o, _stack, _alice),
            agent, ctx,
            alternativeCost: altCost);

        // Resolve Flare of Duplication — it pushes a distinct copy of Bob's
        // Bolt onto the stack above the original (CR 707.10 / 706.10a).
        _resolver.ResolveTop(_stack);

        var copy = _stack.Top.Should().BeOfType<Majik.Core.Spells.Spell>().Subject;
        copy.IsCopy.Should().BeTrue("CR 707.10 — a distinct copy is on the stack");
        copy.Should().NotBeSameAs(bobSpell, "the copy is its own stack object");
        copy.Controller.Should().BeSameAs(_alice,
            "CR 707.10 — the copy is controlled by Flare of Duplication's controller");

        // Sacrifice resolved: creature moved to Alice's graveyard (CR 701.18).
        goblin.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(goblin);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(goblin);
    }

    [Fact]
    public async System.Threading.Tasks.Task CastViaSacrifice_OnOwnTurn_IsLegal_NoTimingGate()
    {
        // Like Flare of Denial, Flare of Duplication has no timing restriction;
        // the sac alt cost should be castable on Alice's own turn.
        var flare = FlareOfDuplicationFactory.Create(_alice);
        flare.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(flare);

        var goblin = MakeRedCreature("Goblin Guide", _alice, isToken: false);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        bobBolt.SetZone(ZoneType.Stack);
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var altCost = new SacrificeNontokenRedCreatureAlternativeCost(goblin);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);

        // Alice's own turn as active player.
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            StepStateType.PreCombatMain, _stack);

        var act = async () => await _flow.CastAsync(
            _alice, flare,
            FlareOfDuplicationFactory.BuildSpellDefinition(o => o, _stack, _alice),
            agent, ctx,
            alternativeCost: altCost);

        await act.Should().NotThrowAsync(
            because: "Flare of Duplication's sac alt cost has no timing restriction (CR 118.9)");
    }

    // ── Bot probe ────────────────────────────────────────────────────────────

    [Fact]
    public void BotProbe_YieldsNontokenRedCandidates_SkipsTokensAndNonRed()
    {
        var flare = FlareOfDuplicationFactory.Create(_alice);
        flare.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(flare);

        var goblin = MakeRedCreature("Goblin", _alice, isToken: false);   // eligible
        MakeRedCreature("Elemental Token", _alice, isToken: true);        // token — skip
        MakeBlueCreature("Merfolk", _alice);                              // blue — skip

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2,
            StepStateType.PreCombatMain, _stack);

        var probe = new FlareOfDuplicationAltCostProbe();
        var candidates = probe.CandidatesFor(flare, _alice, ctx).ToList();

        candidates.Should().HaveCount(1);
        var picked = candidates[0].Should()
            .BeOfType<SacrificeNontokenRedCreatureAlternativeCost>().Subject;
        picked.SacrificedCreature.Should().BeSameAs(goblin);
    }

    [Fact]
    public void BotProbe_WrongCard_YieldsNothing()
    {
        var reverberate = ReverberateFactory.Create(_alice);
        reverberate.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(reverberate);

        MakeRedCreature("Goblin", _alice, isToken: false);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2,
            StepStateType.PreCombatMain, _stack);

        var probe = new FlareOfDuplicationAltCostProbe();
        var candidates = probe.CandidatesFor(reverberate, _alice, ctx).ToList();

        candidates.Should().BeEmpty(because: "probe only matches Flare of Duplication");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Creature MakeRedCreature(string name, Player controller, bool isToken)
    {
        var creature = new Creature(name, "{R}", 1, 1)
        {
            Owner = controller,
            Controller = controller,
        };
        creature.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(creature);
        if (isToken) creature.MarkAsToken();
        return creature;
    }

    private Creature MakeBlueCreature(string name, Player controller)
    {
        var creature = new Creature(name, "{U}", 1, 1)
        {
            Owner = controller,
            Controller = controller,
        };
        creature.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(creature);
        return creature;
    }
}
