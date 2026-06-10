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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Distemper of the Blood (Torment, {R}, Sorcery).
///
/// Oracle text (verified against Scryfall):
///   "Target creature gets +2/+2 and gains trample until end of turn.
///    Madness {R}"
///
/// Coverage (NON-madness body only — madness is intrinsic, covered by
/// MadnessCatalog + MadnessDiscardFunnelTests):
/// - Card identity (Sorcery, red, {R}, owner/controller wired) loaded from
///   the embedded JSON def via <see cref="CardDefinitionLoader"/>.
/// - Spell-definition shape: single 1..1 "target creature" request, no X.
/// - Cast + resolve: target gets +2/+2 (CR 613.1g) and gains Trample EOT
///   (CR 702.19).
/// - EOT cleanup expires both the pump and the keyword grant (CR 514.2).
/// - Fizzle: target not on battlefield at resolution → no-op (CR 608.2b).
/// </summary>
[Trait("Color", "R")]
public class DistemperOfTheBloodFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public DistemperOfTheBloodFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Identity ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasSorceryShape_Red_AtCostR()
    {
        var d = DistemperOfTheBloodFactory.Create(_alice);

        d.Name.Should().Be("Distemper of the Blood");
        d.ManaCost.Should().Be("{R}");
        d.HasType(CardType.Sorcery).Should().BeTrue();
        d.Owner.Should().BeSameAs(_alice);
        d.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(d).Should().Contain(ManaColor.Red);
    }

    [Fact]
    public void SpellDefinition_HasSingleTargetCreatureRequest_NoX()
    {
        var def = DistemperOfTheBloodFactory.BuildDefinition();

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target creature");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    // ── Resolve ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cast_TargetGetsPlusTwoPlusTwoAndGainsTrample()
    {
        var bear = BuildBear(_alice);

        await CastAndResolve(bear);

        bear.GetPower().Should().Be(4, because: "Distemper of the Blood is +2/+2 on top of a 2/2");
        bear.GetToughness().Should().Be(4);
        bear.ActiveEffects!.Compute(bear).Keywords
            .Contains("Trample").Should().BeTrue(
                because: "Distemper of the Blood grants Trample until end of turn (CR 702.19)");
    }

    [Fact]
    public async Task EffectsExpireAtEndOfTurn()
    {
        var bear = BuildBear(_alice);
        var svc = bear.ActiveEffects!;

        await CastAndResolve(bear);

        bear.GetPower().Should().Be(4);
        svc.Compute(bear).Keywords.Contains("Trample").Should().BeTrue();

        // CR 514.2 — until-end-of-turn effects expire in the cleanup step.
        svc.ExpireEndOfTurn();

        bear.GetPower().Should().Be(2, because: "the +2/+2 expires at cleanup");
        bear.GetToughness().Should().Be(2);
        svc.Compute(bear).Keywords.Contains("Trample").Should().BeFalse(
            because: "GrantKeywordUntilEndOfTurnEffect expires at cleanup (CR 514.2)");
    }

    [Fact]
    public async Task TargetNotOnBattlefield_IsNoOp()
    {
        // CR 608.2b — illegal target at resolution → no-op.
        var dead = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob, ActiveEffects = new ContinuousEffectsService() };
        dead.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(dead);

        await CastAndResolve(dead);

        dead.GetPower().Should().Be(2);
        dead.GetToughness().Should().Be(2);
        dead.ActiveEffects!.Compute(dead).Keywords.Contains("Trample").Should().BeFalse();
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
        var d = DistemperOfTheBloodFactory.Create(_alice);
        d.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(d);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { target });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, d,
            DistemperOfTheBloodFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);
    }
}
