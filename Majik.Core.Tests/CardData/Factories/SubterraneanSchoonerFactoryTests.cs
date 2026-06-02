using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.Vehicles;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SubterraneanSchoonerFactory"/> (Lost Caverns of
/// Ixalan, {1}{U}). Artifact — Vehicle 3/4.
///
/// Covers:
/// - Identity (name, types Artifact + Creature, P/T 3/4, Vehicle subtype,
///   not legendary, mana cost, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch (Creature shell with Artifact
///   stamped — same multi-type Vehicle MVP shape as Esika's Chariot).
/// - Attack-explore trigger (CR 508.1f / 603.1 / 701.40): the chosen
///   creature (the injected crewmate) explores — land → hand (no counter),
///   non-land → +1/+1 counter on the CHOSEN creature (not the Vehicle).
/// - Crew 1 (CR 702.122) drives the existing VehicleCrewEffect machinery.
/// </summary>
public class SubterraneanSchoonerFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose()
    {
        AgentRegistry.Clear();
        ZoneServiceRegistry.Clear();
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SubterraneanSchooner_Identity()
    {
        var c = SubterraneanSchoonerFactory.Create(_alice);

        c.Name.Should().Be("Subterranean Schooner");
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Subterranean Schooner is an Artifact (CR 301.1 / 302.1 — Artifact Vehicle)");
        c.HasType(CardType.Creature).Should().BeTrue(
            "v1 vehicle shell is a Creature so CrewAction can flow P/T through " +
            "VehicleCrewEffect");
        c.HasSupertype(CardSupertype.Legendary).Should().BeFalse(
            "Subterranean Schooner is not legendary");
        c.HasSubtype(CardSubtype.Vehicle).Should().BeTrue(
            "Vehicle subtype required for CR 702.122 crew");
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(4);
        c.ManaCost.Should().Be("{1}{U}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the attack-explore trigger");
    }

    [Fact]
    public void SubterraneanSchooner_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Subterranean Schooner", _alice);

        c.Should().BeOfType<Creature>(
            "Subterranean Schooner ships as a Creature shell with Artifact " +
            "stamped on top (Vehicle MVP convention)");
        c.Name.Should().Be("Subterranean Schooner");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vehicle).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Attack-explore trigger — land reveal → chosen creature's controller's
    // hand, no counter (CR 701.40b).
    // -----------------------------------------------------------------------

    [Fact]
    public void SubterraneanSchooner_Attack_LandOnTop_GoesToHand_NoCounter()
    {
        var land = new Land("Island");
        _alice.Zones.Library.AddCard(land);

        var crewmate = new Creature("Bear", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };

        var schooner = SubterraneanSchoonerFactory.Create(
            _alice, eventBus: null, triggers: null, explorerPicker: _ => crewmate);
        _alice.Zones.Battlefield.AddCard(schooner);
        schooner.SetZone(ZoneType.Battlefield);

        ExecuteAttack(schooner);

        _alice.Zones.Hand.GetCards().Should().Contain(land,
            "CR 701.40b — a revealed land goes to the controller's hand");
        crewmate.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "CR 701.40b — a revealed land places no +1/+1 counter");
        schooner.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the Vehicle is not the exploring permanent");
    }

    // -----------------------------------------------------------------------
    // Attack-explore trigger — non-land reveal lands the +1/+1 counter on the
    // CHOSEN creature (CR 701.40c), not the Vehicle.
    // -----------------------------------------------------------------------

    [Fact]
    public void SubterraneanSchooner_Attack_NonLandOnTop_CounterOnChosenCreature_KeepOnTop()
    {
        var spell = new Creature("Spell", "{U}", 3, 3);
        _alice.Zones.Library.AddCard(spell);

        var agent = new ScriptedAgent();
        agent.QueueExploreKeepOnTop(true);
        AgentRegistry.Set(_alice, agent);

        var crewmate = new Creature("Bear", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };

        var schooner = SubterraneanSchoonerFactory.Create(
            _alice, eventBus: null, triggers: null, explorerPicker: _ => crewmate);
        _alice.Zones.Battlefield.AddCard(schooner);
        schooner.SetZone(ZoneType.Battlefield);

        ExecuteAttack(schooner);

        crewmate.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 701.40c — the +1/+1 counter goes on the chosen exploring creature");
        schooner.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the Vehicle does not explore, so no counter lands on it");
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(spell,
            "the agent kept the revealed card on top");
    }

    [Fact]
    public void SubterraneanSchooner_Attack_NonLandOnTop_Graveyard()
    {
        var spell = new Creature("Spell", "{U}", 3, 3);
        _alice.Zones.Library.AddCard(spell);

        var agent = new ScriptedAgent();
        agent.QueueExploreKeepOnTop(false);
        AgentRegistry.Set(_alice, agent);

        var crewmate = new Creature("Bear", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };

        var schooner = SubterraneanSchoonerFactory.Create(
            _alice, eventBus: null, triggers: null, explorerPicker: _ => crewmate);
        _alice.Zones.Battlefield.AddCard(schooner);
        schooner.SetZone(ZoneType.Battlefield);

        ExecuteAttack(schooner);

        crewmate.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        _alice.Zones.Graveyard.GetCards().Should().Contain(spell,
            "the agent put the revealed card into the graveyard (CR 701.40c)");
    }

    // -----------------------------------------------------------------------
    // Crew 1 (CR 702.122) — drives the existing VehicleCrewEffect machinery.
    // -----------------------------------------------------------------------

    [Fact]
    public void SubterraneanSchooner_Crew1_PromotesToCreatureUntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var schooner = SubterraneanSchoonerFactory.Create(_alice);
        schooner.ActiveEffects = effects;
        schooner.HasSummoningSickness = false;

        var crewmate = new Creature("Bird", "{U}", 1, 1)
        {
            Owner = _alice,
            Controller = _alice,
            HasSummoningSickness = false,
        };

        var result = CrewAction.Crew(
            schooner,
            crewCost: SubterraneanSchoonerFactory.CrewCost,
            vehiclePower: SubterraneanSchoonerFactory.VehiclePower,
            vehicleToughness: SubterraneanSchoonerFactory.VehicleToughness,
            new[] { crewmate },
            effects);

        result.Success.Should().BeTrue(
            "1 power is enough to crew 1 (CR 702.122)");
        crewmate.IsTapped.Should().BeTrue("crewmates tap to crew (CR 702.122)");
        schooner.Power.Should().Be(3,
            "VehicleCrewEffect ships base power 3 through Layer 7b");
        schooner.Toughness.Should().Be(4,
            "VehicleCrewEffect ships base toughness 4 through Layer 7b");
    }

    [Fact]
    public void SubterraneanSchooner_Crew1_InsufficientPower_Fails()
    {
        var effects = new ContinuousEffectsService();
        var schooner = SubterraneanSchoonerFactory.Create(_alice);
        schooner.ActiveEffects = effects;

        var crewmate = new Creature("Wall", "{U}", 0, 4)
        {
            Owner = _alice,
            Controller = _alice,
            HasSummoningSickness = false,
        };

        var result = CrewAction.Crew(
            schooner,
            crewCost: SubterraneanSchoonerFactory.CrewCost,
            vehiclePower: SubterraneanSchoonerFactory.VehiclePower,
            vehicleToughness: SubterraneanSchoonerFactory.VehicleToughness,
            new[] { crewmate },
            effects);

        result.Success.Should().BeFalse(
            "0 < 1 — crew cost not met (CR 702.122)");
        crewmate.IsTapped.Should().BeFalse(
            "failed crew does not tap any creature (atomic cost — CR 117.7a)");
    }

    private static void ExecuteAttack(Creature schooner)
    {
        var attack = schooner.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(
                new Majik.Core.Domain.DomainEvents.CreatureAttacksEvent(
                    schooner, schooner.Controller!)));

        foreach (var effect in attack.Effects) effect.Execute();
    }
}
