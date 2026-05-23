using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
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
/// Tests for Galvanic Discharge (Modern Horizons 3, {R}, Instant).
///
/// Covers:
///   - Card identity (Instant, {R}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve damage = 1 + charge counters on controller's artifacts/lands:
///     * empty battlefield → 1 damage (the printed +1).
///     * Aether Vial with 2 charge counters → 3 damage.
///     * two artifacts each with 1 charge counter → 3 damage.
///     * opponent's charge-counter permanents do NOT contribute.
///     * charge counters on a non-artifact creature do NOT contribute.
/// </summary>
public class GalvanicDischargeTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public GalvanicDischargeTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GalvanicDischarge_IsInstant_AtCostR()
    {
        var gd = GalvanicDischargeFactory.Create(_alice);

        gd.Name.Should().Be("Galvanic Discharge");
        gd.ManaCost.Should().Be("{R}");
        gd.HasType(CardType.Instant).Should().BeTrue();
        gd.Owner.Should().BeSameAs(_alice);
        gd.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GalvanicDischarge()
    {
        var card = NamedCardFactory.Create("Galvanic Discharge", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Galvanic Discharge");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — 1 + charge-counter total
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GalvanicDischarge_NoChargeCounters_Deals1Damage()
    {
        // Empty battlefield → X = 1 + 0 = 1.
        var bobStarting = _bob.LifeTotal;
        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(bobStarting - 1);
    }

    [Fact]
    public async Task GalvanicDischarge_AetherVialWith2ChargeCounters_Deals3Damage()
    {
        // Aether Vial (artifact) with 2 charge counters → X = 1 + 2 = 3.
        var vial = AetherVialFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vial);
        vial.SetZone(ZoneType.Battlefield);
        vial.Counters.Add(CounterType.Charge, 2);

        var bobStarting = _bob.LifeTotal;
        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(bobStarting - 3);
    }

    [Fact]
    public async Task GalvanicDischarge_TwoArtifactsEachOneCharge_Deals3Damage()
    {
        // Two artifacts each with 1 charge counter → totals collapse across
        // permanents → X = 1 + (1 + 1) = 3.
        var a1 = new Artifact("Coretapper", "{2}");
        a1.SetOwner(_alice);
        a1.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(a1);
        a1.SetZone(ZoneType.Battlefield);
        a1.Counters.Add(CounterType.Charge, 1);

        var a2 = new Artifact("Energy Chamber", "{2}");
        a2.SetOwner(_alice);
        a2.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(a2);
        a2.SetZone(ZoneType.Battlefield);
        a2.Counters.Add(CounterType.Charge, 1);

        var bobStarting = _bob.LifeTotal;
        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(bobStarting - 3);
    }

    [Fact]
    public async Task GalvanicDischarge_OpponentChargeCounters_DoNotContribute()
    {
        // Bob (opponent) controls an artifact with 5 charge counters.
        // Alice controls nothing. X = 1 + 0 = 1.
        var opponentArtifact = new Artifact("Bob's Vial", "{1}");
        opponentArtifact.SetOwner(_bob);
        opponentArtifact.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(opponentArtifact);
        opponentArtifact.SetZone(ZoneType.Battlefield);
        opponentArtifact.Counters.Add(CounterType.Charge, 5);

        var bobStarting = _bob.LifeTotal;
        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(bobStarting - 1,
            "charge counters on Bob's artifacts don't count — only Alice's do");
    }

    [Fact]
    public async Task GalvanicDischarge_ChargeCountersOnNonArtifactCreature_DoNotContribute()
    {
        // A non-artifact creature with charge counters does not count
        // (the card is not an artifact unless it's an artifact creature).
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        bear.Counters.Add(CounterType.Charge, 3);

        var bobStarting = _bob.LifeTotal;
        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(bobStarting - 1,
            "Grizzly Bears is a creature only — not an artifact or land — so its " +
            "charge counters do not contribute");
    }

    [Fact]
    public async Task GalvanicDischarge_ChargeCountersOnLand_DoContribute()
    {
        // Charge counters on a land the controller controls DO contribute
        // (per "artifacts and/or lands you control").
        var land = new Land(
            "Mountain",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Mountain });
        land.SetOwner(_alice);
        land.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.Counters.Add(CounterType.Charge, 2);

        var bobStarting = _bob.LifeTotal;
        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(bobStarting - 3,
            "lands count too — X = 1 + 2 charge counters on the land = 3");
    }

    [Fact]
    public void CountChargeCounters_MixesArtifactsAndLandsButExcludesCreatures()
    {
        // Programmatic check of the helper:
        // - 1 charge on artifact: counts.
        // - 4 charges on land: counts.
        // - 7 charges on creature (not artifact): excluded.
        // Total = 5.
        var artifact = new Artifact("A", "{0}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);
        artifact.Counters.Add(CounterType.Charge, 1);

        var land = new Land(
            "Charged Land",
            supertypes: null,
            subtypes: new[] { CardSubtype.Mountain });
        land.SetOwner(_alice);
        land.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.Counters.Add(CounterType.Charge, 4);

        var creature = new Creature("Charged Bear", "1G", 2, 2);
        creature.SetOwner(_alice);
        creature.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(creature);
        creature.SetZone(ZoneType.Battlefield);
        creature.Counters.Add(CounterType.Charge, 7);

        GalvanicDischargeFactory.CountChargeCountersOnArtifactsAndLands(_alice)
            .Should().Be(5);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Cast Galvanic Discharge from Alice's hand at <paramref name="target"/>
    /// and resolve the resulting stack object. Mirrors the
    /// UnholyHeatTests / TribalFlamesTests cast harness — direct
    /// cast/resolve, no priority loop.
    /// </summary>
    private async Task CastAndResolveTargeting(object target)
    {
        var gd = GalvanicDischargeFactory.Create(_alice);
        gd.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(gd);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.Main, _stack);

        var spell = await _flow.CastAsync(
            _alice, gd,
            GalvanicDischargeFactory.BuildSpellDefinition(_alice, t => t),
            agent, ctx);

        gd.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();
    }
}
