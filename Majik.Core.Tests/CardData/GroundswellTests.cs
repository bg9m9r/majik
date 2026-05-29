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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Groundswell (Worldwake, {G}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Target creature gets +2/+2 until end of turn.
///    Landfall — If you had a land enter the battlefield under your control
///    this turn, that creature gets +4/+4 until end of turn instead."
///
/// Covers:
///   - Card identity (Instant, {G}, green, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve with landfall inactive → target creature gets +2/+2.
///   - Resolve with landfall active → target creature gets +4/+4 instead.
///   - Landfall gate ignores lands entering under an opponent's control.
///   - The pump expires in the cleanup step (CR 514.2).
///
/// Landfall (CR 702.142 / Groundswell's text) is a resolution-time state
/// check, not a printed trigger — sampled via
/// <see cref="TurnState.LandEnteredThisTurn(Player)"/>, the same gate
/// Searing Blaze uses. The pump is a self-on-target
/// <see cref="Majik.Core.Effects.PumpUntilEndOfTurnEffect"/> (Layer 7c
/// CR 613.1g) like Giant Growth.
/// </summary>
public class GroundswellTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Majik.Core.Game.TurnState _turnState = new();

    public GroundswellTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Groundswell_IsInstant_AtCostG()
    {
        var gs = GroundswellFactory.Create(_alice);

        gs.Name.Should().Be("Groundswell");
        gs.ManaCost.Should().Be("{G}");
        gs.ManaCostValue.TotalValue.Should().Be(1);
        gs.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(gs).Should().Contain(ManaColor.Green);
        gs.Owner.Should().BeSameAs(_alice);
        gs.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Groundswell()
    {
        var card = NamedCardFactory.Create("Groundswell", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Groundswell");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — landfall gate
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Groundswell_NoLandfall_PumpsPlusTwoPlusTwo()
    {
        var bear = MakeCreature();

        await CastAndResolveTargeting(bear);

        bear.GetPower().Should().Be(2 + GroundswellFactory.BasePump);
        bear.GetToughness().Should().Be(2 + GroundswellFactory.BasePump);
    }

    [Fact]
    public async Task Groundswell_LandfallActive_PumpsPlusFourPlusFour_Instead()
    {
        // Drop a land under Alice's control this turn to flip the gate.
        _turnState.RecordLandEnteredBattlefield(_alice);

        var bear = MakeCreature();

        await CastAndResolveTargeting(bear);

        bear.GetPower().Should().Be(2 + GroundswellFactory.LandfallPump);
        bear.GetToughness().Should().Be(2 + GroundswellFactory.LandfallPump);
    }

    [Fact]
    public async Task Groundswell_OpponentLand_DoesNotEnableLandfall()
    {
        // A land entering under Bob's control must NOT flip Alice's gate.
        _turnState.RecordLandEnteredBattlefield(_bob);

        var bear = MakeCreature();

        await CastAndResolveTargeting(bear);

        bear.GetPower().Should().Be(2 + GroundswellFactory.BasePump,
            "landfall only counts lands entering under YOUR control");
    }

    [Fact]
    public async Task Groundswell_Pump_ExpiresAtEndOfTurn()
    {
        var bear = MakeCreature();

        await CastAndResolveTargeting(bear);

        bear.GetPower().Should().Be(2 + GroundswellFactory.BasePump);

        // CR 514.2 — "until end of turn" effects expire in the cleanup step.
        bear.ActiveEffects!.ExpireEndOfTurn();

        bear.GetPower().Should().Be(2);
        bear.GetToughness().Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Creature MakeCreature()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        bear.ActiveEffects = new ContinuousEffectsService();
        _bob.Zones.Battlefield.AddCard(bear);
        return bear;
    }

    /// <summary>
    /// Cast Groundswell from Alice's hand at <paramref name="creature"/> and
    /// resolve the resulting stack object. Mirrors the SearingBlazeTests cast
    /// harness — direct cast/resolve, no priority loop.
    /// </summary>
    private async Task CastAndResolveTargeting(Creature creature)
    {
        var gs = GroundswellFactory.Create(_alice);
        gs.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(gs);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { creature });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, gs,
            GroundswellFactory.BuildSpellDefinition(
                _alice,
                turnStateResolver: () => _turnState,
                resolver: t => t),
            agent, ctx);

        gs.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();
    }
}
