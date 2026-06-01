using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
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
/// Tests for Tamiyo's Safekeeping (Streets of New Capenna, {G}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Target permanent you control gains hexproof and indestructible until end
///    of turn. You gain 2 life. (A permanent with hexproof and indestructible
///    can't be the target of spells or abilities your opponents control. Damage
///    and effects that say "destroy" don't destroy it.)"
///
/// Coverage:
/// - Card identity (Instant, green, {G}, owner/controller wired) loaded from
///   the embedded JSON def via <see cref="CardDefinitionLoader"/>.
/// - <see cref="NamedCardFactory"/> dispatcher returns the correct shape.
/// - Spell-definition shape: single 1..1 "target permanent you control"
///   request, no X.
/// - Cast + resolve: target gains Hexproof (CR 702.11b) and Indestructible
///   (CR 702.12b) until end of turn; the caster gains 2 life (CR 119.3).
/// - Granted Hexproof actually blocks opponent targeting via TargetLegality.
/// - EOT cleanup expires both keyword grants (CR 514.2).
/// - Fizzle: target not on battlefield at resolution → no life gain either,
///   because the spell does not resolve (CR 608.2b). (Per the printed text the
///   life gain is part of the same resolution.)
/// </summary>
public class TamiyosSafekeepingFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public TamiyosSafekeepingFactoryTests()
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
        var ts = TamiyosSafekeepingFactory.Create(_alice);

        ts.Name.Should().Be("Tamiyo's Safekeeping");
        ts.ManaCost.Should().Be("{G}");
        ts.HasType(CardType.Instant).Should().BeTrue();
        ts.Owner.Should().BeSameAs(_alice);
        ts.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(ts).Should().Contain(ManaColor.Green);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsTamiyosSafekeepingShape()
    {
        var dispatched = NamedCardFactory.Create("Tamiyo's Safekeeping", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Tamiyo's Safekeeping");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void SpellDefinition_HasSingleTargetPermanentYouControlRequest_NoX()
    {
        var def = TamiyosSafekeepingFactory.BuildDefinition(_alice);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target permanent you control");
        def.HasVariableX.Should().BeFalse();
    }

    // ── Resolve ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cast_TargetGainsHexproofAndIndestructible_CasterGains2Life()
    {
        var bear = BuildBear(_alice);

        await CastAndResolve(bear);

        bear.ActiveEffects!.Compute(bear).Keywords
            .Contains("Hexproof").Should().BeTrue(
                because: "Tamiyo's Safekeeping grants Hexproof until end of turn (CR 702.11b)");
        bear.ActiveEffects!.Compute(bear).Keywords
            .Contains("Indestructible").Should().BeTrue(
                because: "Tamiyo's Safekeeping grants Indestructible until end of turn (CR 702.12b)");
        CombatAbilities.HasIndestructible(bear).Should().BeTrue();

        _alice.LifeTotal.Should().Be(22, because: "the caster gains 2 life on resolution (CR 119.3)");
    }

    [Fact]
    public async Task GrantedHexproof_BlocksOpponentTargeting()
    {
        var bear = BuildBear(_alice);

        await CastAndResolve(bear);

        var spec = new TargetSpec("target creature").Creatures();
        TargetLegality.IsLegal(spec, bear, caster: _bob).Should().BeFalse(
            because: "Hexproof grants the printed 'can't be target of opponents' spells or abilities' clause");

        TargetLegality.IsLegal(spec, bear, caster: _alice).Should().BeTrue(
            because: "Hexproof only blocks opponents; the controller's own spells are fine");
    }

    [Fact]
    public async Task KeywordGrantsExpireAtEndOfTurn()
    {
        var bear = BuildBear(_alice);
        var svc = bear.ActiveEffects!;

        await CastAndResolve(bear);

        svc.Compute(bear).Keywords.Contains("Hexproof").Should().BeTrue();
        svc.Compute(bear).Keywords.Contains("Indestructible").Should().BeTrue();

        // CR 514.2 — EOT cleanup expires the grants.
        svc.ExpireEndOfTurn();

        svc.Compute(bear).Keywords.Contains("Hexproof").Should().BeFalse(
            because: "GrantKeywordUntilEndOfTurnEffect expires at cleanup (CR 514.2)");
        svc.Compute(bear).Keywords.Contains("Indestructible").Should().BeFalse(
            because: "GrantKeywordUntilEndOfTurnEffect expires at cleanup (CR 514.2)");
    }

    [Fact]
    public async Task TargetNotOnBattlefield_IsNoOp()
    {
        // CR 608.2b — illegal target at resolution → the spell does nothing,
        // including no life gain (the whole spell fizzles since its only target
        // is illegal).
        var dead = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _alice, Controller = _alice, ActiveEffects = new ContinuousEffectsService() };
        dead.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(dead);

        await CastAndResolve(dead);

        dead.ActiveEffects!.Compute(dead).Keywords.Contains("Hexproof").Should().BeFalse();
        dead.ActiveEffects!.Compute(dead).Keywords.Contains("Indestructible").Should().BeFalse();
        _alice.LifeTotal.Should().Be(20, because: "the spell fizzles on an illegal-only target (CR 608.2b)");
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
        var ts = TamiyosSafekeepingFactory.Create(_alice);
        ts.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(ts);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { target });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, ts,
            TamiyosSafekeepingFactory.BuildDefinition(_alice),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);
    }
}
