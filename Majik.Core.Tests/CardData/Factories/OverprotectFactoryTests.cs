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
/// Tests for Overprotect (Streets of New Capenna, {1}{G}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Target creature you control gets +3/+3 and gains trample, hexproof, and
///    indestructible until end of turn."
///
/// Coverage (the card's UNIQUE behaviour + a single identity assert):
/// - Identity: Instant, green, {1}{G}, owner/controller wired (loaded from the
///   embedded JSON def via <see cref="CardDefinitionLoader"/>).
/// - Spell-definition shape: single 1..1 "target creature you control"
///   request, no X.
/// - Cast + resolve: target gets +3/+3 (CR 613.1g) and gains Trample
///   (CR 702.19), Hexproof (CR 702.11b), and Indestructible (CR 702.12b) EOT.
/// - Granted Hexproof actually blocks opponent targeting via TargetLegality.
/// - EOT cleanup expires the pump and all three keyword grants (CR 514.2).
/// - Fizzle: target not on battlefield at resolution → no-op (CR 608.2b).
/// </summary>
[Trait("Color", "G")]
public class OverprotectFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public OverprotectFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Identity ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasInstantShape_Green_AtCost1G()
    {
        var op = OverprotectFactory.Create(_alice);

        op.Name.Should().Be("Overprotect");
        op.ManaCost.Should().Be("{1}{G}");
        op.HasType(CardType.Instant).Should().BeTrue();
        op.Owner.Should().BeSameAs(_alice);
        op.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(op).Should().Contain(ManaColor.Green);
    }

    [Fact]
    public void SpellDefinition_HasSingleTargetCreatureYouControlRequest_NoX()
    {
        var def = OverprotectFactory.BuildDefinition();

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target creature you control");
        def.HasVariableX.Should().BeFalse();
    }

    // ── Resolve ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cast_TargetGetsPlusThreePlusThreeAndAllThreeKeywords()
    {
        var bear = BuildBear(_alice);

        await CastAndResolve(bear);

        bear.GetPower().Should().Be(5, because: "Overprotect is +3/+3 on top of a 2/2");
        bear.GetToughness().Should().Be(5);
        var kws = bear.ActiveEffects!.Compute(bear).Keywords;
        kws.Contains("Trample").Should().BeTrue(because: "Overprotect grants Trample until end of turn");
        kws.Contains("Hexproof").Should().BeTrue(because: "Overprotect grants Hexproof until end of turn");
        kws.Contains("Indestructible").Should().BeTrue(because: "Overprotect grants Indestructible until end of turn");
    }

    [Fact]
    public async Task GrantedHexproof_BlocksOpponentTargeting()
    {
        var bear = BuildBear(_alice);

        await CastAndResolve(bear);

        var spec = new TargetSpec("target creature").Creatures();
        TargetLegality.IsLegal(spec, bear, caster: _bob).Should().BeFalse(
            because: "Hexproof grants the 'can't be target of opponents' spells or abilities' clause (CR 702.11b)");
        TargetLegality.IsLegal(spec, bear, caster: _alice).Should().BeTrue(
            because: "Hexproof only blocks opponents; the controller's own spells are fine");
    }

    [Fact]
    public async Task AllEffectsExpireAtEndOfTurn()
    {
        var bear = BuildBear(_alice);
        var svc = bear.ActiveEffects!;

        await CastAndResolve(bear);

        bear.GetPower().Should().Be(5);
        svc.Compute(bear).Keywords.Contains("Indestructible").Should().BeTrue();

        // CR 514.2 — until-end-of-turn effects expire in the cleanup step.
        svc.ExpireEndOfTurn();

        bear.GetPower().Should().Be(2, because: "the +3/+3 expires at cleanup");
        bear.GetToughness().Should().Be(2);
        var kws = svc.Compute(bear).Keywords;
        kws.Contains("Trample").Should().BeFalse();
        kws.Contains("Hexproof").Should().BeFalse();
        kws.Contains("Indestructible").Should().BeFalse(
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
        dead.ActiveEffects!.Compute(dead).Keywords.Contains("Indestructible").Should().BeFalse();
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
        var op = OverprotectFactory.Create(_alice);
        op.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(op);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { target });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, op,
            OverprotectFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);
    }
}
