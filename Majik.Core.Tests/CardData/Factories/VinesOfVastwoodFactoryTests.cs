using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
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
/// Tests for Vines of Vastwood (Zendikar, {G}, Instant — Kicker {G},
/// grants Hexproof EOT, and +4/+4 EOT when kicked).
///
/// Coverage:
/// - Card identity (Instant, green, {G}, owner/controller wired).
/// - Kicker keyword marker attached for shape inspection.
/// - NamedCardFactory dispatcher returns the correct shape.
/// - KickerAltCostProbe recognises Vines as a {G}-kicker card.
/// - Cast NOT kicked + resolve: target gains Hexproof EOT, no pump.
/// - Cast kicked + resolve: target gains Hexproof EOT + +4/+4 EOT.
/// - Granted Hexproof actually blocks opponent targeting via TargetLegality
///   (CR 702.11b) — the printed "can't be target of opponents' spells or
///   abilities" clause comes through.
/// - EOT cleanup expires both the keyword grant and the pump (CR 514.2).
/// - Fizzle: target not on battlefield → no-op (CR 608.2b).
/// </summary>
public class VinesOfVastwoodFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public VinesOfVastwoodFactoryTests()
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
        var v = VinesOfVastwoodFactory.Create(_alice);

        v.Name.Should().Be("Vines of Vastwood");
        v.ManaCost.Should().Be("{G}");
        v.HasType(CardType.Instant).Should().BeTrue();
        v.Owner.Should().BeSameAs(_alice);
        v.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(v).Should().Contain(ManaColor.Green);
    }

    [Fact]
    public void Create_AttachesKickerKeywordMarker()
    {
        var v = VinesOfVastwoodFactory.Create(_alice);

        var keywords = v.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Kicker");
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsVinesShape()
    {
        var dispatched = NamedCardFactory.Create("Vines of Vastwood", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Vines of Vastwood");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void KickerProbe_RecognisesVinesAsGKicker()
    {
        var v = VinesOfVastwoodFactory.Create(_alice);
        v.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(v);

        var probe = new KickerAltCostProbe();
        var cost = probe.KickerCostFor(v, _alice);

        cost.Should().NotBeNull();
        cost!.ToString().Should().Be(ManaCost.Parse("{G}").ToString());
    }

    // ── Resolve (unkicked) ────────────────────────────────────────────────────

    [Fact]
    public async Task NotKicked_TargetGainsHexproofEOT_NoPump()
    {
        // Bob's bear is the target — "creature an opponent doesn't control".
        var bear = BuildBear(_bob);

        await CastAndResolve(bear, kicked: false);

        bear.ActiveEffects!.Compute(bear).Keywords
            .Contains("Hexproof").Should().BeTrue(
                "unkicked Vines grants Hexproof until end of turn");
        bear.GetPower().Should().Be(2, because: "no pump without kicker");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public async Task NotKicked_OpponentCannotTargetThatCreature()
    {
        // Alice protects Bob's bear with Vines; Alice's own spells (the opp
        // from the bear-controller's perspective) can no longer target it
        // via TargetLegality (CR 702.11b).
        var bear = BuildBear(_bob);

        await CastAndResolve(bear, kicked: false);

        // Alice is the bear's opponent. Hexproof blocks Alice from
        // targeting the bear.
        var spec = new TargetSpec("target creature").Creatures();
        TargetLegality.IsLegal(spec, bear, caster: _alice).Should().BeFalse(
            because: "Hexproof grants the printed 'can't be target of opponents' spells' clause");

        // Bob (the bear's controller) can still target his own bear.
        TargetLegality.IsLegal(spec, bear, caster: _bob).Should().BeTrue(
            because: "Hexproof only blocks opponents; controller's own spells are fine");
    }

    // ── Resolve (kicked) ──────────────────────────────────────────────────────

    [Fact]
    public async Task Kicked_TargetGainsHexproofAndPlusFourPlusFour()
    {
        var bear = BuildBear(_bob);

        await CastAndResolve(bear, kicked: true);

        bear.ActiveEffects!.Compute(bear).Keywords
            .Contains("Hexproof").Should().BeTrue();
        bear.GetPower().Should().Be(6, because: "kicked Vines is +4/+4 EOT on top of 2/2");
        bear.GetToughness().Should().Be(6);
    }

    [Fact]
    public async Task Kicked_EffectsExpireAtEndOfTurn()
    {
        var bear = BuildBear(_bob);
        var svc = bear.ActiveEffects!;

        await CastAndResolve(bear, kicked: true);

        bear.GetPower().Should().Be(6);
        svc.Compute(bear).Keywords.Contains("Hexproof").Should().BeTrue();

        svc.ExpireEndOfTurn();

        bear.GetPower().Should().Be(2);
        bear.GetToughness().Should().Be(2);
        svc.Compute(bear).Keywords.Contains("Hexproof").Should().BeFalse(
            "GrantKeywordUntilEndOfTurnEffect expires at cleanup (CR 514.2)");
    }

    // ── Fizzle ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TargetNotOnBattlefield_IsNoOp()
    {
        var dead = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob, ActiveEffects = new ContinuousEffectsService() };
        dead.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(dead);

        await CastAndResolve(dead, kicked: false);

        dead.ActiveEffects!.Compute(dead).Keywords.Contains("Hexproof").Should().BeFalse();
        dead.GetPower().Should().Be(2);
        dead.GetToughness().Should().Be(2);
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

    private async Task CastAndResolve(object target, bool kicked)
    {
        var v = VinesOfVastwoodFactory.Create(_alice);
        v.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(v);

        IReadOnlyList<IAdditionalCost>? additional = null;
        if (kicked)
        {
            // Pre-fund the kicker so KickerAdditionalCost.Pay succeeds.
            _alice.AddManaToPool(ManaCost.Parse("{G}"));
            additional = new[] { VinesOfVastwoodFactory.BuildAdditionalCost(v) };
        }

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { target });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, v,
            VinesOfVastwoodFactory.BuildDefinition(v),
            agent, ctx,
            additionalCosts: additional);

        _resolver.ResolveTop(_stack);
    }
}
