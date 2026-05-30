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
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for This Town Ain't Big Enough (Outlaws of Thunder
/// Junction, {4}{U}).
///
/// Oracle (verified against Scryfall):
///   "This spell costs {3} less to cast if it targets a permanent you control.
///    Return up to two target nonland permanents to their owners' hands."
///
/// Coverage:
///   * Card shape + dispatch by name (Instant {4}{U}, blue).
///   * Cost reduction (CR 117.7): {4}{U} → {1}{U} when a chosen target is a
///     permanent the caster controls; unchanged when only opponents'
///     permanents are targeted; unchanged before targets are stamped.
///   * Bounce: returns one OR two nonland permanents to owners' hands; lands
///     are not legal targets; not-on-battlefield-at-resolution → no-op
///     (CR 608.2b); "up to" allows zero targets.
/// </summary>
public class ThisTownAintBigEnoughTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ThisTownAintBigEnoughTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Identity + dispatch ───────────────────────────────────────────────

    [Fact]
    public void Create_HasInstantShape_Blue_FourU()
    {
        var card = ThisTownAintBigEnoughFactory.Create(_alice);

        card.Name.Should().Be("This Town Ain't Big Enough");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCost.Should().Be("{4}{U}");
        card.ManaCostValue.TotalValue.Should().Be(5);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsShape()
    {
        var dispatched = NamedCardFactory.Create("This Town Ain't Big Enough", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("This Town Ain't Big Enough");
        dispatched.ManaCost.Should().Be("{4}{U}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleUpToTwoNonlandRequest()
    {
        var def = ThisTownAintBigEnoughFactory.BuildDefinition();

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(0);
        def.TargetRequests[0].MaxTargets.Should().Be(2);
        def.TargetRequests[0].Description.Should().Contain("nonland permanent");
    }

    // ── Cost reduction — "costs {3} less if it targets a permanent you control" ──

    [Fact]
    public void EffectiveCost_WithNoTargets_IsPrintedFourU()
    {
        // Before targets are stamped (e.g. bot affordability check before
        // SpellCastFlow runs), the reducer must NOT apply — printed {4}{U}.
        var card = ThisTownAintBigEnoughFactory.Create(_alice);

        var effective = CostReduction.GetEffectiveCost(card, _alice);

        effective.Generic.Should().Be(4);
        effective.Blue.Should().Be(1);
    }

    [Fact]
    public void EffectiveCost_DroppedByThree_WhenTargetingOwnPermanent()
    {
        var card = ThisTownAintBigEnoughFactory.Create(_alice);

        // Alice targets her own creature → {3} reduction. {4}{U} → {1}{U}.
        var myBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        myBear.SetZone(ZoneType.Battlefield);
        ((Card)card).SetPendingCastTargets(
            new IReadOnlyList<object>[] { new object[] { myBear } });

        var effective = CostReduction.GetEffectiveCost(card, _alice);

        effective.Generic.Should().Be(1,
            because: "targeting a permanent you control drops {3} from the generic cost ({4}{U} → {1}{U})");
        effective.Blue.Should().Be(1,
            because: "the coloured pip is untouched (CR 117.7c)");
    }

    [Fact]
    public void EffectiveCost_Unchanged_WhenTargetingOnlyOpponentPermanents()
    {
        var card = ThisTownAintBigEnoughFactory.Create(_alice);

        // Both targets are Bob's permanents — no reduction.
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bobBear.SetZone(ZoneType.Battlefield);
        var bobEnch = new Enchantment("Blood Moon", "{2}{R}") { Owner = _bob, Controller = _bob };
        bobEnch.SetZone(ZoneType.Battlefield);
        ((Card)card).SetPendingCastTargets(
            new IReadOnlyList<object>[] { new object[] { bobBear, bobEnch } });

        var effective = CostReduction.GetEffectiveCost(card, _alice);

        effective.Generic.Should().Be(4);
        effective.Blue.Should().Be(1);
    }

    [Fact]
    public async Task SpellCastFlow_StampsTargets_AndReducerApplies_DuringCast()
    {
        // Integration: Alice casts targeting her own creature. She should only
        // need {1}{U} (not {4}{U}) — the reducer kicks in during SpellCastFlow
        // because targets are stamped before cost-calc.
        var myBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        myBear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(myBear);

        var card = ThisTownAintBigEnoughFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        // Alice has exactly {1}{U} — enough only if the reducer fires.
        _alice.AddManaToPool(ManaCost.Parse("{1}{U}"));

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)myBear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card, ThisTownAintBigEnoughFactory.BuildDefinition(_zones), agent, ctx, alternativeCost: null);

        _stack.IsEmpty.Should().BeFalse(because: "the cast succeeded with only {1}{U}");
    }

    // ── Bounce: single target ─────────────────────────────────────────────

    [Fact]
    public async Task ReturnsOneTargetNonlandPermanentToOwnersHand()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var card = ThisTownAintBigEnoughFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card, ThisTownAintBigEnoughFactory.BuildDefinition(_zones), agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bear.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    // ── Bounce: two targets ───────────────────────────────────────────────

    [Fact]
    public async Task ReturnsTwoTargetNonlandPermanentsToOwnersHands()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var ench = new Enchantment("Blood Moon", "{2}{R}") { Owner = _bob, Controller = _bob };
        ench.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(ench);

        var card = ThisTownAintBigEnoughFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear, (object)ench });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card, ThisTownAintBigEnoughFactory.BuildDefinition(_zones), agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bear.Zone.Should().Be(ZoneType.Hand);
        ench.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(new ICard[] { bear, ench });
        _bob.Zones.Battlefield.GetCards().Should().NotContain(new ICard[] { bear, ench });
    }

    // ── No-op: target not on battlefield at resolution (CR 608.2b) ─────────

    [Fact]
    public async Task NoOp_WhenTargetNotOnBattlefieldAtResolution()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var card = ThisTownAintBigEnoughFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card, ThisTownAintBigEnoughFactory.BuildDefinition(_zones), agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bear.Zone.Should().Be(ZoneType.Graveyard,
            because: "target not on battlefield at resolution → no-op (CR 608.2b)");
        _bob.Zones.Hand.GetCards().Should().NotContain(bear);
    }
}
