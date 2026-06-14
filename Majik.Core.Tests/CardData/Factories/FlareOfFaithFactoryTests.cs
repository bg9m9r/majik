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
/// Tests for Flare of Faith (MH3, {1}{W}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Target creature gets +2/+2 until end of turn. If it's a Human, instead
///    it gets +3/+3 and gains indestructible until end of turn."
///
/// Covers ONLY the card's unique behaviour plus a single identity assert:
/// - Identity: Instant, white, {1}{W} (MV 2).
/// - Non-Human target: +2/+2 EOT, NO indestructible (base mode, CR 613.1g).
/// - Human target: "instead" +3/+3 AND gains indestructible EOT (CR 702.12).
/// - Mutual exclusion: the +3/+3 branch replaces the +2/+2 branch (not stacked).
/// - EOT cleanup expires both the pump and the keyword grant (CR 514.2).
/// - Fizzle: target not on the battlefield at resolution → no-op (CR 608.2b).
///
/// Dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests — not re-tested here.
/// </summary>
[Trait("Color", "W")]
public class FlareOfFaithFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public FlareOfFaithFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Identity ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasInstantShape_White_AtCost1W()
    {
        var fof = FlareOfFaithFactory.Create(_alice);

        fof.Name.Should().Be("Flare of Faith");
        fof.ManaCost.Should().Be("{1}{W}");
        fof.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(fof).Should().Contain(ManaColor.White);
        fof.ManaCostValue.TotalValue.Should().Be(2);
    }

    // ── Non-Human branch: base +2/+2, no indestructible ───────────────────────

    [Fact]
    public async Task Cast_NonHumanTarget_GetsPlusTwoPlusTwo_NoIndestructible()
    {
        var bear = BuildCreature("Grizzly Bears", _alice, 2, 2, CardSubtype.Bear);

        await CastAndResolve(bear);

        bear.GetPower().Should().Be(4, because: "non-Human base mode is +2/+2 on a 2/2");
        bear.GetToughness().Should().Be(4);
        bear.ActiveEffects!.Compute(bear).Keywords
            .Contains("Indestructible").Should().BeFalse(
                because: "the indestructible grant only applies to Humans");
    }

    // ── Human branch: "instead" +3/+3 and indestructible ──────────────────────

    [Fact]
    public async Task Cast_HumanTarget_GetsPlusThreePlusThree_AndIndestructible()
    {
        var soldier = BuildCreature("Human Soldier", _alice, 2, 2, CardSubtype.Human, CardSubtype.Soldier);

        await CastAndResolve(soldier);

        // "instead it gets +3/+3" — the base +2/+2 is replaced, not stacked.
        soldier.GetPower().Should().Be(5, because: "Human branch is +3/+3 on a 2/2 (instead of +2/+2)");
        soldier.GetToughness().Should().Be(5);
        soldier.ActiveEffects!.Compute(soldier).Keywords
            .Contains("Indestructible").Should().BeTrue(
                because: "a Human target gains indestructible until end of turn (CR 702.12)");
    }

    // ── EOT expiry ────────────────────────────────────────────────────────────

    [Fact]
    public async Task HumanBranch_EffectsExpireAtEndOfTurn()
    {
        var soldier = BuildCreature("Human Soldier", _alice, 2, 2, CardSubtype.Human);
        var svc = soldier.ActiveEffects!;

        await CastAndResolve(soldier);

        soldier.GetPower().Should().Be(5);
        svc.Compute(soldier).Keywords.Contains("Indestructible").Should().BeTrue();

        // CR 514.2 — until-end-of-turn effects expire in the cleanup step.
        svc.ExpireEndOfTurn();

        soldier.GetPower().Should().Be(2, because: "the +3/+3 expires at cleanup");
        soldier.GetToughness().Should().Be(2);
        svc.Compute(soldier).Keywords.Contains("Indestructible").Should().BeFalse(
            because: "the granted indestructible expires at cleanup (CR 514.2)");
    }

    // ── Fizzle ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TargetNotOnBattlefield_IsNoOp()
    {
        // CR 608.2b — illegal target at resolution → no-op.
        var dead = new Creature("Human Soldier", "{W}", 2, 2, subtypes: new[] { CardSubtype.Human })
        { Owner = _alice, Controller = _alice, ActiveEffects = new ContinuousEffectsService() };
        dead.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(dead);

        await CastAndResolve(dead);

        dead.GetPower().Should().Be(2);
        dead.GetToughness().Should().Be(2);
        dead.ActiveEffects!.Compute(dead).Keywords.Contains("Indestructible").Should().BeFalse();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Creature BuildCreature(string name, Player owner, int power, int toughness, params CardSubtype[] subtypes)
    {
        var c = new Creature(name, "{1}{W}", power, toughness, subtypes: subtypes)
        { Owner = owner, Controller = owner, ActiveEffects = new ContinuousEffectsService() };
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private async Task CastAndResolve(object target)
    {
        var fof = FlareOfFaithFactory.Create(_alice);
        fof.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(fof);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { target });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, fof,
            FlareOfFaithFactory.BuildDefinition(),
            agent, ctx);

        _resolver.ResolveTop(_stack);
    }
}
