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
/// End-to-end tests for Flare of Denial (MH3, {1}{U}{U}). Exercises:
///   * Card shape (Instant + blue + MV 3).
///   * NamedCardFactory dispatch.
///   * Alternative cost: sacrifice a nontoken blue creature you control
///     instead of paying {1}{U}{U}.
///   * Sac path: chosen creature moves battlefield → graveyard on resolve.
///   * Filter: non-blue creature NOT a legal sacrifice candidate.
///   * Filter: token blue creature NOT a legal sacrifice candidate.
///   * Filter: opponent-controlled creature NOT a legal sacrifice candidate.
///   * Counter resolve: target spell leaves the stack.
///   * No timing restriction: alt cost legal on caster's own turn.
///   * Bot probe surfaces eligible nontoken blue creature candidates only.
/// </summary>
public class FlareOfDenialFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public FlareOfDenialFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Shape ────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasInstantShape_Blue_ManaValue3()
    {
        var flare = FlareOfDenialFactory.Create(_alice);

        flare.Name.Should().Be("Flare of Denial");
        flare.HasType(CardType.Instant).Should().BeTrue();
        flare.ManaCost.Should().Be("{1}{U}{U}");
        CardColors.GetColors(flare).Should().Contain(ManaColor.Blue);
        flare.ManaCostValue.TotalValue.Should().Be(3);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsFlareOfDenialShape()
    {
        var dispatched = NamedCardFactory.Create("Flare of Denial", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Flare of Denial");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    // ── Alternative cost — CanCastFor ────────────────────────────────────────

    [Fact]
    public void AltCost_CanCastFor_NontokenBlueCreature_ControlledByCaster_IsLegal()
    {
        var flare = FlareOfDenialFactory.Create(_alice);
        var merfolk = MakeBlueCreature("Merfolk Scout", _alice, isToken: false);

        var altCost = new SacrificeNontokenBlueCreatureAlternativeCost(merfolk);

        altCost.CanCastFor(flare, _alice).Should().BeTrue();
    }

    [Fact]
    public void AltCost_CanCastFor_TokenBlueCreature_IsIllegal()
    {
        var flare = FlareOfDenialFactory.Create(_alice);
        var fishToken = MakeBlueCreature("Fish Token", _alice, isToken: true);

        var altCost = new SacrificeNontokenBlueCreatureAlternativeCost(fishToken);

        altCost.CanCastFor(flare, _alice).Should().BeFalse(
            because: "tokens are excluded per oracle text");
    }

    [Fact]
    public void AltCost_CanCastFor_NontokenRedCreature_IsIllegal()
    {
        var flare = FlareOfDenialFactory.Create(_alice);
        var goblin = MakeRedCreature("Goblin", _alice);

        var altCost = new SacrificeNontokenBlueCreatureAlternativeCost(goblin);

        altCost.CanCastFor(flare, _alice).Should().BeFalse(
            because: "the creature must be blue");
    }

    [Fact]
    public void AltCost_CanCastFor_BlueCratureControlledByOpponent_IsIllegal()
    {
        var flare = FlareOfDenialFactory.Create(_alice);
        // Bob controls this creature — Alice is the caster.
        var bobMerfolk = MakeBlueCreature("Merfolk Spy", _bob, isToken: false);

        var altCost = new SacrificeNontokenBlueCreatureAlternativeCost(bobMerfolk);

        altCost.CanCastFor(flare, _alice).Should().BeFalse(
            because: "the sacrificed creature must be controlled by the caster");
    }

    // ── Alternative cost — resolve (sacrifice path) ──────────────────────────

    [Fact]
    public async Task CastViaSacrifice_CountersTargetSpell_AndSacrificesCreature()
    {
        var flare = FlareOfDenialFactory.Create(_alice);
        flare.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(flare);

        var merfolk = MakeBlueCreature("Merfolk Looter", _alice, isToken: false);

        // Bob's spell on the stack — Flare of Denial's target.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var altCost = new SacrificeNontokenBlueCreatureAlternativeCost(merfolk);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2,
            PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, flare,
            FlareOfDenialFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: altCost);

        // Resolve the spell.
        _resolver.ResolveTop(_stack);

        // Counter resolved: Bob's bolt is gone from the stack.
        _stack.GetAll().Should().NotContain(s => ReferenceEquals(s, bobSpell));
        bobBolt.Zone.Should().Be(ZoneType.Graveyard);

        // Sacrifice resolved: creature moved to Alice's graveyard.
        merfolk.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(merfolk);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(merfolk);
    }

    [Fact]
    public async Task CastViaSacrifice_OnOwnTurn_IsLegal_NoTimingGate()
    {
        // Unlike Force of Will's pitch, Flare of Denial has no timing restriction.
        // It should be castable on Alice's own turn via the alt cost.
        var flare = FlareOfDenialFactory.Create(_alice);
        flare.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(flare);

        var merfolk = MakeBlueCreature("Merfolk Scout", _alice, isToken: false);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var altCost = new SacrificeNontokenBlueCreatureAlternativeCost(merfolk);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);

        // Alice's own turn as the active player.
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.PreCombatMain, _stack);

        var act = async () => await _flow.CastAsync(
            _alice, flare,
            FlareOfDenialFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: altCost);

        // Should NOT throw — Flare of Denial has no "not your turn" timing gate.
        await act.Should().NotThrowAsync(
            because: "Flare of Denial's sac alt cost has no timing restriction (CR 118.9)");
    }

    // ── Bot probe ────────────────────────────────────────────────────────────

    [Fact]
    public void BotProbe_YieldsNontokenBlueCandidates_SkipsTokensAndNonBlue()
    {
        var flare = FlareOfDenialFactory.Create(_alice);
        flare.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(flare);

        var merfolk = MakeBlueCreature("Merfolk", _alice, isToken: false);   // eligible
        MakeBlueCreature("Fish Token", _alice, isToken: true);               // token — skip
        MakeRedCreature("Goblin", _alice);                                    // red — skip

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2,
            PhaseStateType.PreCombatMain, _stack);

        var probe = new FlareOfDenialAltCostProbe();
        var candidates = probe.CandidatesFor(flare, _alice, ctx).ToList();

        candidates.Should().HaveCount(1);
        var picked = candidates[0].Should().BeOfType<SacrificeNontokenBlueCreatureAlternativeCost>().Subject;
        picked.SacrificedCreature.Should().BeSameAs(merfolk);
    }

    [Fact]
    public void BotProbe_WrongCard_YieldsNothing()
    {
        var counterspell = CounterspellFactory.Create(_alice);
        counterspell.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(counterspell);

        MakeBlueCreature("Merfolk", _alice, isToken: false);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2,
            PhaseStateType.PreCombatMain, _stack);

        var probe = new FlareOfDenialAltCostProbe();
        var candidates = probe.CandidatesFor(counterspell, _alice, ctx).ToList();

        candidates.Should().BeEmpty(because: "probe only matches Flare of Denial");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Create a blue creature (mana cost {U}) on Alice's battlefield.</summary>
    private Creature MakeBlueCreature(string name, Player controller, bool isToken)
    {
        // CardColors.GetColors derives blue from the {U} in the mana cost.
        var creature = new Creature(name, "{U}", 1, 1)
        {
            Owner = controller,
            Controller = controller,
        };
        creature.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(creature);
        if (isToken) creature.MarkAsToken();
        return creature;
    }

    /// <summary>Create a red creature (mana cost {R}) on a player's battlefield.</summary>
    private Creature MakeRedCreature(string name, Player controller)
    {
        var creature = new Creature(name, "{R}", 1, 1)
        {
            Owner = controller,
            Controller = controller,
        };
        creature.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(creature);
        return creature;
    }
}
