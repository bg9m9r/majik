using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Blossoming Defense (Kaladesh, {G}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Target creature you control gets +2/+2 and gains hexproof until end of
///    turn. (It can't be the target of spells or abilities your opponents
///    control.)"
///
/// Coverage:
/// - Card identity (Instant, green, {G}, owner/controller wired) loaded from
///   the embedded JSON def via <see cref="CardDefinitionLoader"/>.
/// - <see cref="NamedCardFactory"/> dispatcher returns the correct shape.
/// - Spell-definition shape: single 1..1 "target creature you control"
///   request, no X.
/// - Cast + resolve: target gets +2/+2 (CR 613.1g) and gains Hexproof EOT
///   (CR 702.11b).
/// - Granted Hexproof actually blocks opponent targeting via TargetLegality
///   (CR 702.11b) — the printed parenthetical clause comes through.
/// - EOT cleanup expires both the pump and the keyword grant (CR 514.2).
/// - Fizzle: target not on battlefield at resolution → no-op (CR 608.2b).
/// </summary>
public class BlossomingDefenseFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public BlossomingDefenseFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Identity ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasInstantShape_Green_AtCostG()
    {
        var bd = BlossomingDefenseFactory.Create(_alice);

        bd.Name.Should().Be("Blossoming Defense");
        bd.ManaCost.Should().Be("{G}");
        bd.HasType(CardType.Instant).Should().BeTrue();
        bd.Owner.Should().BeSameAs(_alice);
        bd.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(bd).Should().Contain(ManaColor.Green);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsBlossomingDefenseShape()
    {
        var dispatched = NamedCardFactory.Create("Blossoming Defense", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Blossoming Defense");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void SpellDefinition_HasSingleTargetCreatureYouControlRequest_NoX()
    {
        var def = BlossomingDefenseFactory.BuildDefinition();

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target creature you control");
        def.HasVariableX.Should().BeFalse();
    }

    // ── Resolve ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cast_TargetGetsPlusTwoPlusTwoAndGainsHexproof()
    {
        var bear = BuildBear(_alice);

        await CastAndResolve(bear);

        bear.GetPower().Should().Be(4, because: "Blossoming Defense is +2/+2 on top of a 2/2");
        bear.GetToughness().Should().Be(4);
        bear.ActiveEffects!.Compute(bear).Keywords
            .Contains("Hexproof").Should().BeTrue(
                because: "Blossoming Defense grants Hexproof until end of turn");
    }

    [Fact]
    public async Task GrantedHexproof_BlocksOpponentTargeting()
    {
        // Alice protects her own bear; Bob (the opponent) can no longer target
        // it via TargetLegality (CR 702.11b — the printed parenthetical).
        var bear = BuildBear(_alice);

        await CastAndResolve(bear);

        var spec = new TargetSpec("target creature").Creatures();
        TargetLegality.IsLegal(spec, bear, caster: _bob).Should().BeFalse(
            because: "Hexproof grants the printed 'can't be target of opponents' spells or abilities' clause");

        // Alice (the controller) can still target her own bear.
        TargetLegality.IsLegal(spec, bear, caster: _alice).Should().BeTrue(
            because: "Hexproof only blocks opponents; the controller's own spells are fine");
    }

    [Fact]
    public async Task EffectsExpireAtEndOfTurn()
    {
        var bear = BuildBear(_alice);
        var svc = bear.ActiveEffects!;

        await CastAndResolve(bear);

        bear.GetPower().Should().Be(4);
        svc.Compute(bear).Keywords.Contains("Hexproof").Should().BeTrue();

        // Simulate end-of-turn cleanup (CR 514.2 — EOT effects expire).
        svc.ExpireEndOfTurn();

        bear.GetPower().Should().Be(2, because: "the +2/+2 expires at cleanup");
        bear.GetToughness().Should().Be(2);
        svc.Compute(bear).Keywords.Contains("Hexproof").Should().BeFalse(
            because: "GrantKeywordUntilEndOfTurnEffect expires at cleanup (CR 514.2)");
    }

    [Fact]
    public async Task TargetNotOnBattlefield_IsNoOp()
    {
        // CR 608.2b — illegal target at resolution → no-op.
        var dead = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _alice, Controller = _alice, ActiveEffects = new ContinuousEffectsService() };
        dead.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(dead);

        await CastAndResolve(dead);

        dead.GetPower().Should().Be(2);
        dead.GetToughness().Should().Be(2);
        dead.ActiveEffects!.Compute(dead).Keywords.Contains("Hexproof").Should().BeFalse();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Creature BuildBear(Player owner)
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = owner, Controller = owner, ActiveEffects = new ContinuousEffectsService() };
        bear.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(bear);
        return bear;
    }

    private async Task CastAndResolve(object target)
    {
        var bd = BlossomingDefenseFactory.Create(_alice);
        bd.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bd);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { target });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, bd,
            BlossomingDefenseFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);
    }
}
