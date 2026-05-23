using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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
/// Tests for Searing Blaze (Worldwake / Modern Horizons, {R}{R}, Instant).
///
/// Covers:
///   - Card identity (Instant, {R}{R}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve with landfall inactive → 1 damage to player + 1 damage to creature.
///   - Resolve with landfall active → 3 damage to player + 3 damage to creature.
///   - Resolve against a planeswalker (loyalty removal path).
///   - Landfall gate flips after a land enters under the controller.
///   - Landfall gate ignores lands entering under an opponent's control.
///
/// Landfall (CR 702.142 / Searing Blaze's text) is sampled at resolution
/// via <see cref="TurnState.LandEnteredThisTurn(Player)"/>.
/// </summary>
public class SearingBlazeTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Majik.Core.Game.TurnState _turnState = new();

    public SearingBlazeTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SearingBlaze_IsInstant_AtCostRR()
    {
        var sb = SearingBlazeFactory.Create(_alice);

        sb.Name.Should().Be("Searing Blaze");
        sb.ManaCost.Should().Be("{R}{R}");
        sb.HasType(CardType.Instant).Should().BeTrue();
        sb.Owner.Should().BeSameAs(_alice);
        sb.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SearingBlaze()
    {
        var card = NamedCardFactory.Create("Searing Blaze", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Searing Blaze");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — landfall gate
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SearingBlaze_NoLandfall_Deals1To_PlayerAndCreature()
    {
        // No land has entered under Alice's control this turn → base damage.
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobBear.SetOwner(_bob);
        bobBear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var bobStarting = _bob.LifeTotal;

        await CastAndResolveTargeting(_bob, bobBear);

        _bob.LifeTotal.Should().Be(bobStarting - 1, "landfall inactive → 1 damage to player");
        bobBear.Damage.Should().Be(1, "landfall inactive → 1 damage to creature");
    }

    [Fact]
    public async Task SearingBlaze_LandfallActive_Deals3To_PlayerAndCreature()
    {
        // Drop a land under Alice's control this turn to flip the gate.
        var aliceMountain = new Land(
            "Mountain",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Mountain });
        aliceMountain.SetOwner(_alice);
        aliceMountain.SetController(_alice);
        _turnState.RecordLandEnteredBattlefield(_alice);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobBear.SetOwner(_bob);
        bobBear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var bobStarting = _bob.LifeTotal;

        await CastAndResolveTargeting(_bob, bobBear);

        _bob.LifeTotal.Should().Be(bobStarting - 3, "landfall active → 3 damage to player");
        bobBear.Damage.Should().Be(3, "landfall active → 3 damage to creature");
    }

    [Fact]
    public async Task SearingBlaze_LandfallActive_TargetingPlaneswalker_RemovesLoyalty()
    {
        // Landfall active → 3 loyalty removed from target planeswalker
        // (CR 119.3 — damage to a planeswalker removes that much loyalty).
        _turnState.RecordLandEnteredBattlefield(_alice);

        var pw = new Planeswalker(
            "Chandra, Torch of Defiance",
            "{2}{R}{R}",
            startingLoyalty: 5,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Chandra });
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(pw);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobBear.SetOwner(_bob);
        bobBear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobBear);

        await CastAndResolveTargeting(pw, bobBear);

        pw.Loyalty.Should().Be(5 - 3, "landfall active → 3 loyalty removed");
        bobBear.Damage.Should().Be(3);
    }

    [Fact]
    public void IsLandfallActive_TracksOnlyControllersLands()
    {
        // Lands entering under Bob's control should not flip Alice's
        // landfall gate.
        _turnState.RecordLandEnteredBattlefield(_bob);

        SearingBlazeFactory
            .IsLandfallActive(_alice, () => _turnState)
            .Should().BeFalse("opponent land does not enable Alice's landfall");

        _turnState.RecordLandEnteredBattlefield(_alice);

        SearingBlazeFactory
            .IsLandfallActive(_alice, () => _turnState)
            .Should().BeTrue("a land entering under Alice's control flips the gate");
    }

    [Fact]
    public void IsLandfallActive_NoTurnStateWired_ReturnsFalse()
    {
        // When the caller can't supply a TurnState (test / dispatcher path)
        // the gate is treated as inactive — base damage applies.
        SearingBlazeFactory
            .IsLandfallActive(_alice, () => null)
            .Should().BeFalse();
    }

    [Fact]
    public void TurnState_Reset_ClearsLandfallTally()
    {
        _turnState.RecordLandEnteredBattlefield(_alice);
        _turnState.LandEnteredThisTurn(_alice).Should().BeTrue();

        _turnState.Reset();
        _turnState.LandEnteredThisTurn(_alice).Should().BeFalse();
        _turnState.LandsEnteredByController(_alice).Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Cast Searing Blaze from Alice's hand at <paramref name="playerOrPw"/>
    /// and <paramref name="creature"/> and resolve the resulting stack
    /// object. Mirrors the UnholyHeatTests cast harness — direct cast/resolve,
    /// no priority loop. Two target requests → two QueueTargets calls.
    /// </summary>
    private async Task CastAndResolveTargeting(object playerOrPw, object creature)
    {
        var sb = SearingBlazeFactory.Create(_alice);
        sb.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sb);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { playerOrPw });
        agent.QueueTargets(new object[] { creature });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.Main, _stack);

        var spell = await _flow.CastAsync(
            _alice, sb,
            SearingBlazeFactory.BuildSpellDefinition(
                _alice,
                turnStateResolver: () => _turnState,
                resolver: t => t),
            agent, ctx);

        sb.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();
    }
}
