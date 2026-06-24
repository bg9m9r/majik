using FluentAssertions;
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
/// Tests for Might of the Meek (Bloomburrow, {R}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Target creature gains trample until end of turn. It also gets +1/+0 until
///    end of turn if you control a Mouse.
///    Draw a card."
///
/// Covers ONLY the card's unique behaviour plus a single identity assert:
/// - Identity: Instant, red, {R} (MV 1).
/// - Always: target gains Trample until end of turn (CR 702.19, CR 514.2 expiry).
/// - Conditional +1/+0: applied only when the caster controls a Mouse
///   (CR 205.3m subtype check at resolution); absent otherwise.
/// - Cantrip tail: the caster draws a card (CR 121.1), independent of the
///   pump conditional.
/// - EOT cleanup expires the trample grant and the conditional pump (CR 514.2).
/// - Fizzle: target not on the battlefield at resolution → no buff, but the
///   independent "Draw a card." sentence still fires (CR 608.2b).
///
/// Dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests — not re-tested here.
/// </summary>
[Trait("Color", "R")]
public class MightOfTheMeekFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public MightOfTheMeekFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Identity ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasInstantShape_Red_AtCostR()
    {
        var motm = MightOfTheMeekFactory.Create(_alice);

        motm.Name.Should().Be("Might of the Meek");
        motm.ManaCost.Should().Be("{R}");
        motm.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(motm).Should().Contain(ManaColor.Red);
        motm.ManaCostValue.TotalValue.Should().Be(1);
    }

    // ── Trample is always granted; no Mouse → no +1/+0 ────────────────────────

    [Fact]
    public async Task Cast_NoMouseControlled_GrantsTrampleOnly_AndCantrips()
    {
        var bear = BuildCreature("Grizzly Bears", _alice, 2, 2, CardSubtype.Bear);
        FillLibrary(_alice, 3);
        var handBefore = _alice.Zones.Hand.GetCards().Count();

        await CastAndResolve(bear);

        // "Target creature gains trample until end of turn" — always (CR 702.19).
        bear.ActiveEffects!.Compute(bear).Keywords
            .Contains("Trample").Should().BeTrue(
                because: "trample is granted unconditionally");
        // "+1/+0 … if you control a Mouse" — caster controls no Mouse → no pump.
        bear.GetPower().Should().Be(2, because: "no Mouse controlled → no +1/+0");
        bear.GetToughness().Should().Be(2);
        // "Draw a card." — independent cantrip tail (CR 121.1).
        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1,
            because: "Might of the Meek draws a card on resolution");
    }

    // ── Mouse controlled → +1/+0 also applies ─────────────────────────────────

    [Fact]
    public async Task Cast_MouseControlled_GrantsTrampleAndPlusOnePlusZero()
    {
        var mouse = BuildCreature("Heartfire Hero", _alice, 1, 1, CardSubtype.Mouse);
        var target = BuildCreature("Grizzly Bears", _alice, 2, 2, CardSubtype.Bear);
        FillLibrary(_alice, 3);

        await CastAndResolve(target);

        target.ActiveEffects!.Compute(target).Keywords
            .Contains("Trample").Should().BeTrue();
        // "It also gets +1/+0 … if you control a Mouse" (CR 205.3m).
        target.GetPower().Should().Be(3, because: "controller has a Mouse → +1/+0 on a 2/2");
        target.GetToughness().Should().Be(2, because: "the pump is +1/+0, toughness unchanged");

        // Touch `mouse` so the controlled-Mouse permanent is unmistakably live.
        mouse.HasSubtype(CardSubtype.Mouse).Should().BeTrue();
    }

    // ── Opponent's Mouse does NOT count ("you control a Mouse") ───────────────

    [Fact]
    public async Task Cast_OpponentControlsMouse_NoPump()
    {
        // The Mouse belongs to Bob; "you control a Mouse" reads the CASTER's
        // battlefield (CR 109.5 "you" = the spell's controller).
        BuildCreature("Heartfire Hero", _bob, 1, 1, CardSubtype.Mouse);
        var target = BuildCreature("Grizzly Bears", _alice, 2, 2, CardSubtype.Bear);
        FillLibrary(_alice, 3);

        await CastAndResolve(target);

        target.ActiveEffects!.Compute(target).Keywords.Contains("Trample").Should().BeTrue();
        target.GetPower().Should().Be(2, because: "only a Mouse YOU control grants the +1/+0");
    }

    // ── EOT expiry ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GrantedEffectsExpireAtEndOfTurn()
    {
        BuildCreature("Heartfire Hero", _alice, 1, 1, CardSubtype.Mouse);
        var target = BuildCreature("Grizzly Bears", _alice, 2, 2, CardSubtype.Bear);
        var svc = target.ActiveEffects!;
        FillLibrary(_alice, 3);

        await CastAndResolve(target);

        target.GetPower().Should().Be(3);
        svc.Compute(target).Keywords.Contains("Trample").Should().BeTrue();

        // CR 514.2 — until-end-of-turn effects expire in the cleanup step.
        svc.ExpireEndOfTurn();

        target.GetPower().Should().Be(2, because: "the +1/+0 expires at cleanup");
        svc.Compute(target).Keywords.Contains("Trample").Should().BeFalse(
            because: "the granted trample expires at cleanup (CR 514.2)");
    }

    // ── Fizzle: illegal target still draws ────────────────────────────────────

    [Fact]
    public async Task TargetNotOnBattlefield_NoBuff_StillDraws()
    {
        // CR 608.2b — illegal target at resolution → the buff clause no-ops, but
        // the independent "Draw a card." sentence still fires.
        var dead = new Creature("Grizzly Bears", "{1}{G}", 2, 2, subtypes: new[] { CardSubtype.Bear })
        { Owner = _alice, Controller = _alice, ActiveEffects = new ContinuousEffectsService() };
        dead.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(dead);
        FillLibrary(_alice, 3);
        var handBefore = _alice.Zones.Hand.GetCards().Count();

        await CastAndResolve(dead);

        dead.GetPower().Should().Be(2);
        dead.ActiveEffects!.Compute(dead).Keywords.Contains("Trample").Should().BeFalse();
        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1,
            because: "the cantrip is an independent sentence and fires even on a fizzled target");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Creature BuildCreature(string name, Player owner, int power, int toughness, params CardSubtype[] subtypes)
    {
        var c = new Creature(name, "{1}{G}", power, toughness, subtypes: subtypes)
        { Owner = owner, Controller = owner, ActiveEffects = new ContinuousEffectsService() };
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static void FillLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var card = new Instant($"Filler {i}", "{R}") { Owner = p, Controller = p };
            card.SetZone(ZoneType.Library);
            p.Zones.Library.AddCard(card);
        }
    }

    private async Task CastAndResolve(object target)
    {
        var motm = MightOfTheMeekFactory.Create(_alice);
        motm.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(motm);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { target });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, motm,
            MightOfTheMeekFactory.BuildDefinition(_alice),
            agent, ctx);

        _resolver.ResolveTop(_stack);
    }
}
