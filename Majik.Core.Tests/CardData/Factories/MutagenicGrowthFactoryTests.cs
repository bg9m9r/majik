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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Mutagenic Growth (New Phyrexia, {G/P}).
///
/// Coverage:
/// - Card identity (Instant, green, {G} printed cost, owner/controller wired).
/// - Phyrexian keyword marker attached for shape inspection.
/// - NamedCardFactory dispatcher returns the correct shape.
/// - Phyrexian alt-cost shape: 2 life, zero mana remainder.
/// - Cast paying {G} (no alt-cost): target gets +2/+2; controller life unchanged.
/// - Cast paying 2 life via PhyrexianManaAlternativeCost: target gets +2/+2;
///   controller's life -2.
/// - +2/+2 expires at end of turn (CR 514.2).
/// - Fizzle: target not on battlefield at resolution → no-op (CR 608.2b).
/// </summary>
[Trait("Color", "G")]
public class MutagenicGrowthFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public MutagenicGrowthFactoryTests()
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
        var mg = MutagenicGrowthFactory.Create(_alice);

        mg.Name.Should().Be("Mutagenic Growth");
        mg.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(mg).Should().Contain(ManaColor.Green);
        mg.Owner.Should().Be(_alice);
        mg.Controller.Should().Be(_alice);
        mg.ManaCostValue.TotalValue.Should().Be(1, because: "single {G} pip");
    }

    [Fact]
    public void Create_AttachesPhyrexianKeywordMarker()
    {
        var mg = MutagenicGrowthFactory.Create(_alice);

        var keywords = mg.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Phyrexian",
            because: "{G/P} shape is preserved structurally for visibility/search");
    }
    // ── Phyrexian alt-cost shape ──────────────────────────────────────────────

    [Fact]
    public void PhyrexianAlternativeCost_TwoLifeZeroMana()
    {
        var alt = MutagenicGrowthFactory.PhyrexianAlternativeCost();

        alt.LifeCost.Should().Be(2,
            because: "{G/P} contributes one phyrexian pip = 2 life");
        alt.AlternativeManaCost.Should().Be(ManaCost.Zero,
            because: "{G/P} has no non-phyrexian component after stripping the pip");
    }

    // ── Resolve effect ────────────────────────────────────────────────────────

    [Fact]
    public async Task CastPayingMana_TargetGetsPlusTwoPlusTwo_LifeUnchanged()
    {
        // Alice controls a 2/2 bear; she'll cast Mutagenic Growth on it
        // paying {G} (no alt-cost), expecting +2/+2 with no life loss.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var svc = new ContinuousEffectsService();
        bear.ActiveEffects = svc;

        var mg = MutagenicGrowthFactory.Create(_alice);
        mg.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mg);

        var startingLife = _alice.LifeTotal;

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, mg,
            MutagenicGrowthFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bear.GetPower().Should().Be(4,
            because: "Mutagenic Growth registers +2/+2 via PumpUntilEndOfTurnEffect");
        bear.GetToughness().Should().Be(4);
        _alice.LifeTotal.Should().Be(startingLife,
            because: "Mutagenic Growth was cast paying mana, not life");
    }

    [Fact]
    public async Task CastPayingTwoLife_TargetGetsPlusTwoPlusTwo_ControllerLosesTwoLife()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var svc = new ContinuousEffectsService();
        bear.ActiveEffects = svc;

        var mg = MutagenicGrowthFactory.Create(_alice);
        mg.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mg);

        var startingLife = _alice.LifeTotal;
        var phyrexian = MutagenicGrowthFactory.PhyrexianAlternativeCost();

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, mg,
            MutagenicGrowthFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: phyrexian);

        _resolver.ResolveTop(_stack);

        bear.GetPower().Should().Be(4);
        bear.GetToughness().Should().Be(4);
        _alice.LifeTotal.Should().Be(startingLife - 2,
            because: "phyrexian alt cost charges 2 life per {G/P} pip");
    }

    [Fact]
    public async Task PumpEffect_ExpiresAtEndOfTurn()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var svc = new ContinuousEffectsService();
        bear.ActiveEffects = svc;

        var mg = MutagenicGrowthFactory.Create(_alice);
        mg.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mg);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, mg,
            MutagenicGrowthFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // Effect active mid-turn.
        bear.GetPower().Should().Be(4, because: "+2/+2 is active during the turn");

        // Simulate end-of-turn cleanup (CR 514.2 — EOT effects expire).
        svc.ExpireEndOfTurn();

        bear.GetPower().Should().Be(2,
            because: "PumpUntilEndOfTurnEffect.ExpiresAtEndOfTurn = true; effect removed at EOT");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public async Task TargetNotOnBattlefield_IsNoOp()
    {
        // Bob's creature is already in the graveyard at resolution time
        // (CR 608.2b — illegal target → no-op).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var svc = new ContinuousEffectsService();
        bear.ActiveEffects = svc;

        var mg = MutagenicGrowthFactory.Create(_alice);
        mg.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mg);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, mg,
            MutagenicGrowthFactory.BuildDefinition(),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bear.GetPower().Should().Be(2,
            because: "Mutagenic Growth does nothing when target is not on the battlefield");
        bear.GetToughness().Should().Be(2);
    }
}
