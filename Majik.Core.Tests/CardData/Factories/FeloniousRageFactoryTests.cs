using FluentAssertions;
using Majik.Core.Abilities;
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
/// Tests for Felonious Rage (Murders at Karlov Manor, {R}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Target creature you control gets +2/+0 and gains haste until end of
///    turn. When that creature dies this turn, create a 2/2 white and blue
///    Detective creature token."
///
/// Covers the card's UNIQUE behaviour:
///   - Resolve grants +2/+0 and Haste until end of turn (CR 613.1g / 702.10).
///   - End-of-turn cleanup lifts the pump + haste (CR 514.2).
///   - The pumped creature dying this turn → a 2/2 W/U Detective token is
///     created under the spell's controller (delayed triggered ability,
///     CR 603.7 / 700.4).
///   - The targeted creature surviving the turn → no token.
///   - A DIFFERENT creature dying → no token (the delayed trigger keys off
///     the exact targeted creature reference).
///   - Illegal target at resolution → no-op (CR 608.2b).
///
/// Plumbing (dispatch + well-formedness) is owned by CardFactoryContractTests.
/// </summary>
[Trait("Color", "R")]
public class FeloniousRageFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Pump + Haste grant ────────────────────────────────────────────────

    [Fact]
    public void Resolve_GrantsPlusTwoPlusZero_AndHaste()
    {
        var continuous = new ContinuousEffectsService();
        var bear = BuildCreature(continuous, _alice, power: 2, toughness: 2);

        bear.HasEffectiveKeyword(FeloniousRageFactory.GrantedHaste).Should().BeFalse();

        ExecuteResolve(bear);

        bear.GetPower().Should().Be(4, "CR 613.1g — Felonious Rage grants +2/+0");
        bear.GetToughness().Should().Be(2, "Felonious Rage's pump is power-only (+2/+0)");
        bear.HasEffectiveKeyword(FeloniousRageFactory.GrantedHaste).Should().BeTrue(
            "CR 702.10 — Felonious Rage grants Haste until end of turn");
    }

    [Fact]
    public void Resolve_EndOfTurnCleanup_LiftsPumpAndHaste()
    {
        var continuous = new ContinuousEffectsService();
        var bear = BuildCreature(continuous, _alice, power: 3, toughness: 3);

        ExecuteResolve(bear);
        bear.GetPower().Should().Be(5);
        bear.HasEffectiveKeyword(FeloniousRageFactory.GrantedHaste).Should().BeTrue();

        // CR 514.2 — EOT-flagged effects expire at cleanup.
        continuous.ExpireEndOfTurn();

        bear.GetPower().Should().Be(3);
        bear.GetToughness().Should().Be(3);
        bear.HasEffectiveKeyword(FeloniousRageFactory.GrantedHaste).Should().BeFalse();
    }

    [Fact]
    public void Resolve_TargetNotControlledByCaster_IsNoOp()
    {
        // Bob's creature — not "you control" from Alice's perspective.
        var continuous = new ContinuousEffectsService();
        var bobBear = BuildCreature(continuous, _bob, power: 2, toughness: 2);

        ExecuteResolve(bobBear);

        bobBear.GetPower().Should().Be(2, "CR 109.5 / 608.2b — not controlled by the caster → no-op");
        bobBear.HasEffectiveKeyword(FeloniousRageFactory.GrantedHaste).Should().BeFalse();
    }

    // ── Delayed "when that creature dies this turn" → Detective token ──────

    [Fact]
    public void TargetDiesThisTurn_CreatesTwoTwoWhiteBlueDetectiveToken()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var continuous = new ContinuousEffectsService();
        var bear = BuildCreature(continuous, _alice, power: 2, toughness: 2);

        ExecuteResolve(bear, triggers, zones);

        // The creature dies this turn. Route through ZoneService so the
        // CardMovedEvent(Battlefield→Graveyard) publishes (CR 700.4 — a
        // creature put into a graveyard from the battlefield = dies).
        zones.MoveCard(bear, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        // Fire the delayed trigger onto the stack and resolve it.
        triggers.PutPendingTriggersOnStack(_alice);
        var resolver = new StackResolver(_bus, zones);
        while (!stack.IsEmpty) resolver.ResolveTop(stack);

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Detective")
            .ToList();

        tokens.Should().HaveCount(1,
            "CR 603.7 — when the targeted creature dies this turn, create one Detective token");
        var token = tokens[0];
        token.BasePower.Should().Be(2);
        token.BaseToughness.Should().Be(2);
        token.HasSubtype(CardSubtype.Detective).Should().BeTrue();
        token.Controller.Should().BeSameAs(_alice, "the token is created under the spell's controller");

        var colors = CardColors.GetColors(token);
        colors.Should().Contain(ManaColor.White);
        colors.Should().Contain(ManaColor.Blue);
        colors.Should().HaveCount(2, "a 2/2 white and blue token — no other colours");
    }

    [Fact]
    public void TargetSurvives_NoToken()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var continuous = new ContinuousEffectsService();
        var bear = BuildCreature(continuous, _alice, power: 2, toughness: 2);

        ExecuteResolve(bear, triggers, zones);

        // The creature never dies this turn.
        triggers.PutPendingTriggersOnStack(_alice);
        var resolver = new StackResolver(_bus, zones);
        while (!stack.IsEmpty) resolver.ResolveTop(stack);

        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .Should().BeEmpty("the targeted creature did not die this turn → no token");
    }

    [Fact]
    public void DifferentCreatureDies_NoToken()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var target = BuildCreature(new ContinuousEffectsService(), _alice, power: 2, toughness: 2);
        var bystander = BuildCreature(new ContinuousEffectsService(), _alice, power: 1, toughness: 1);

        ExecuteResolve(target, triggers, zones);

        // A DIFFERENT creature dies — the delayed trigger keys off the exact
        // targeted creature reference, so it must not fire.
        zones.MoveCard(bystander, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        triggers.PutPendingTriggersOnStack(_alice);
        var resolver = new StackResolver(_bus, zones);
        while (!stack.IsEmpty) resolver.ResolveTop(stack);

        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .Should().BeEmpty("only the targeted creature's death creates the token");
    }

    // ── Identity (non-vanilla cost) ───────────────────────────────────────

    [Fact]
    public void FeloniousRage_Identity()
    {
        var c = FeloniousRageFactory.Create(_alice);

        c.Name.Should().Be("Felonious Rage");
        c.ManaCost.Should().Be("{R}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void ExecuteResolve(
        Creature target,
        TriggerManager? triggers = null,
        ZoneService? zones = null)
    {
        var def = FeloniousRageFactory.BuildSpellDefinition(
            _alice, resolver: t => t, triggers: triggers, zones: zones);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private Creature BuildCreature(
        ContinuousEffectsService continuous,
        Player controller,
        int power,
        int toughness)
    {
        var c = new Creature($"{power}/{toughness} Bear", "{G}", power, toughness)
        {
            Owner = controller,
            Controller = controller,
            Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }
}
