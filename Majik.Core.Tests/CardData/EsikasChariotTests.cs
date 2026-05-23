using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.Vehicles;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="EsikasChariotFactory"/>.
///
/// Covers:
/// - Identity (name, types Artifact + Creature, P/T 4/4, Vehicle subtype,
///   Legendary supertype, mana cost, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch (returns a Creature with the
///   Artifact card type also stamped — same multi-type shape as Wurmcoil
///   Engine, distinguishing it from a plain artifact).
/// - ETB trigger creates exactly two 2/2 Cat creature tokens under the
///   chariot's controller (CR 603.1 / 603.6a).
/// - Attack trigger creates a token copy of the chosen token creature
///   the controller controls (CR 508.1f / 706 — snapshotted copiable
///   values).
/// - Crew 4 — total tap-power ≥ 4 promotes the chariot to a 4/4 creature
///   until end of turn via <see cref="VehicleCrewEffect"/>
///   (CR 702.122).
/// </summary>
public class EsikasChariotTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void EsikasChariot_Identity()
    {
        var c = EsikasChariotFactory.Create(_alice);

        c.Name.Should().Be("Esika's Chariot");
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Esika's Chariot is an Artifact (CR 301.1 / 302.1 — Artifact Vehicle)");
        c.HasType(CardType.Creature).Should().BeTrue(
            "v1 vehicle shell is a Creature so CrewAction can flow P/T through " +
            "VehicleCrewEffect");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Esika's Chariot is Legendary");
        c.HasSubtype(CardSubtype.Vehicle).Should().BeTrue(
            "Vehicle subtype required for CR 702.122 crew");
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(4);
        c.ManaCost.Should().Be("{3}{G}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EsikasChariot_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Esika's Chariot", _alice);

        c.Should().BeOfType<Creature>(
            "Esika's Chariot ships as a Creature shell with Artifact stamped " +
            "on top (Vehicle MVP convention — mirrors Wurmcoil Engine's " +
            "multi-type pattern)");
        c.Name.Should().Be("Esika's Chariot");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vehicle).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB trigger — two 2/2 Cat tokens (CR 603.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void EsikasChariot_Etb_CreatesTwoTwoTwoCatTokens()
    {
        var alice = new Player("Alice", 20);
        var chariot = EsikasChariotFactory.Create(alice);

        // Place on battlefield so the trigger's active-zone guard is satisfied
        // and the ETB effect runs against the live controller's zones.
        alice.Zones.Battlefield.AddCard(chariot);
        chariot.SetZone(ZoneType.Battlefield);

        // Esika's Chariot has two triggered abilities (ETB + attack). Pick
        // the ETB trigger by matching against the printed condition shape.
        var etb = chariot.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(new Majik.Core.Events.CardMovedEvent(
                chariot, ZoneType.Hand, ZoneType.Battlefield)));

        foreach (var effect in etb.Effects) effect.Execute();

        var cats = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Cat")
            .ToList();

        cats.Should().HaveCount(2,
            "ETB trigger creates exactly two Cat creature tokens (CR 603.1)");
        cats.Should().AllSatisfy(t =>
        {
            t.BasePower.Should().Be(2);
            t.BaseToughness.Should().Be(2);
            t.HasType(CardType.Creature).Should().BeTrue();
            t.HasSubtype(CardSubtype.Cat).Should().BeTrue();
            t.Controller.Should().BeSameAs(alice,
                "tokens enter under Esika's Chariot's controller (CR 111.6)");
        });
    }

    // -----------------------------------------------------------------------
    // Attack trigger — token copy (CR 508.1f / 706)
    // -----------------------------------------------------------------------

    [Fact]
    public void EsikasChariot_Attack_CreatesCopyOfTargetToken()
    {
        var alice = new Player("Alice", 20);

        // Pre-seed a token creature on the battlefield to be the copy target —
        // a 2/2 Cat (mirrors the ETB output), so the attack copy looks just
        // like a Cat from the ETB.
        var spec = new Majik.Core.Tokens.TokenFactory.TokenSpec(
            Name: "Cat", Power: 2, Toughness: 2,
            Subtypes: new[] { CardSubtype.Cat });
        var existingCat = Majik.Core.Tokens.TokenFactory.CreateOnBattlefield(
            spec, alice, zones: null);

        var chariot = EsikasChariotFactory.Create(
            alice,
            zoneService: null,
            eventBus: null,
            triggers: null,
            copyTargetPicker: _ => existingCat);
        alice.Zones.Battlefield.AddCard(chariot);
        chariot.SetZone(ZoneType.Battlefield);

        var attack = chariot.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(
                new Majik.Core.Domain.DomainEvents.CreatureAttacksEvent(
                    chariot, alice)));

        foreach (var effect in attack.Effects) effect.Execute();

        // The battlefield now contains: chariot + existingCat + 1 new copy.
        var cats = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Cat")
            .ToList();
        cats.Should().HaveCount(2,
            "attack trigger spawns exactly one Cat copy of the existing token");

        // The new copy is a distinct instance with the same copiable values.
        var copy = cats.Single(c => !ReferenceEquals(c, existingCat));
        copy.BasePower.Should().Be(2, "copy snapshots target's printed power");
        copy.BaseToughness.Should().Be(2, "copy snapshots target's printed toughness");
        copy.HasSubtype(CardSubtype.Cat).Should().BeTrue(
            "copy snapshots target's subtypes (CR 706.2)");
        copy.Controller.Should().BeSameAs(alice,
            "the copy enters under the chariot's controller, not the target's");
    }

    // -----------------------------------------------------------------------
    // Crew 4 (CR 702.122) — drives the existing VehicleCrewEffect machinery.
    // -----------------------------------------------------------------------

    [Fact]
    public void EsikasChariot_Crew4_PromotesToCreatureUntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var chariot = EsikasChariotFactory.Create(_alice);
        chariot.ActiveEffects = effects;
        chariot.HasSummoningSickness = false;

        // Two creatures with combined power exactly 4 — the crew cost.
        var crew1 = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            HasSummoningSickness = false,
        };
        var crew2 = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            HasSummoningSickness = false,
        };

        var result = CrewAction.Crew(
            chariot,
            crewCost: EsikasChariotFactory.CrewCost,
            vehiclePower: EsikasChariotFactory.VehiclePower,
            vehicleToughness: EsikasChariotFactory.VehicleToughness,
            new[] { crew1, crew2 },
            effects);

        result.Success.Should().BeTrue(
            "2 + 2 power is enough to crew 4 (CR 702.122)");
        crew1.IsTapped.Should().BeTrue("crewmates tap to crew (CR 702.122)");
        crew2.IsTapped.Should().BeTrue();
        chariot.Power.Should().Be(4,
            "VehicleCrewEffect ships base power 4 through Layer 7b");
        chariot.Toughness.Should().Be(4,
            "VehicleCrewEffect ships base toughness 4 through Layer 7b");
    }

    [Fact]
    public void EsikasChariot_Crew4_InsufficientPower_Fails()
    {
        var effects = new ContinuousEffectsService();
        var chariot = EsikasChariotFactory.Create(_alice);
        chariot.ActiveEffects = effects;

        // 3 total power < 4 crew cost.
        var crew1 = new Creature("Bear", "1G", 3, 3)
        {
            Owner = _alice,
            Controller = _alice,
            HasSummoningSickness = false,
        };

        var result = CrewAction.Crew(
            chariot,
            crewCost: EsikasChariotFactory.CrewCost,
            vehiclePower: EsikasChariotFactory.VehiclePower,
            vehicleToughness: EsikasChariotFactory.VehicleToughness,
            new[] { crew1 },
            effects);

        result.Success.Should().BeFalse(
            "3 < 4 — crew cost not met (CR 702.122)");
        crew1.IsTapped.Should().BeFalse(
            "failed crew does not tap any creature (atomic cost — CR 117.7a)");
    }
}
