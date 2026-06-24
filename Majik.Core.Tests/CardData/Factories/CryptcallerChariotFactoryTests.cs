using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.Vehicles;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CryptcallerChariotFactory"/> (Duskmourn).
///
/// Covers the card's UNIQUE behaviour:
/// - Identity ({3}{B} Artifact — Vehicle 5/5 with Menace).
/// - Discard trigger (CR 603.1): discarding one card mints one tapped 2/2
///   black Zombie token; discarding N cards mints N ("that many").
/// - Tokens enter tapped (CR 111.6) and are black 2/2 Zombies (CR 105 / 111.4).
/// - Lands count too (no nonland gate — CR 701.8).
/// - Opponent discards do NOT mint tokens ("you discard" — CR 109.5).
/// - Crew 2 (CR 702.122) promotes the vehicle to a 5/5 creature.
///
/// (Dispatch + well-formedness are covered for every implemented card by
/// CardFactoryContractTests — no dispatch test here.)
/// </summary>
[Trait("Color", "B")]
public class CryptcallerChariotFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity — CR 301.1 / 302.1 / 702.111
    // -----------------------------------------------------------------------

    [Fact]
    public void CryptcallerChariot_Identity_ArtifactVehicle55_Menace()
    {
        var card = CryptcallerChariotFactory.Create(_alice);

        card.Name.Should().Be("Cryptcaller Chariot");
        card.ManaCost.Should().Be("{3}{B}");
        card.BasePower.Should().Be(5);
        card.BaseToughness.Should().Be(5);
        card.HasType(CardType.Artifact).Should().BeTrue(
            "Cryptcaller Chariot is an Artifact (CR 301.1 / 302.1 — Artifact Vehicle)");
        card.HasType(CardType.Creature).Should().BeTrue(
            "v1 vehicle shell is a Creature so CrewAction can flow P/T through " +
            "VehicleCrewEffect");
        card.HasSubtype(CardSubtype.Vehicle).Should().BeTrue(
            "Vehicle subtype required for CR 702.122 crew");
        CombatAbilities.HasMenace(card).Should().BeTrue(
            "Cryptcaller Chariot has Menace (CR 702.111)");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Discard trigger — CR 603.1 / 701.8 / 111.6
    // -----------------------------------------------------------------------

    private Creature WiredChariotOnBattlefield(IEventBus bus)
    {
        var chariot = CryptcallerChariotFactory.Create(_alice, bus);
        _alice.Zones.Battlefield.AddCard(chariot);
        chariot.SetZone(ZoneType.Battlefield);
        return chariot;
    }

    private void DiscardCard(Card card, Player owner, IEventBus bus)
    {
        card.SetOwner(owner);
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        owner.Zones.Hand.RemoveCard(card);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
        bus.Publish(new CardMovedEvent(card, ZoneType.Hand, ZoneType.Graveyard));
    }

    private System.Collections.Generic.List<Creature> Zombies(Player p) =>
        p.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Zombie")
            .ToList();

    [Fact]
    public void CryptcallerChariot_YouDiscardOneCard_CreatesOneTappedBlackZombie()
    {
        var bus = new EventBus();
        WiredChariotOnBattlefield(bus);

        DiscardCard(new Instant("Lightning Bolt", "{R}"), _alice, bus);

        var zombies = Zombies(_alice);
        zombies.Should().HaveCount(1,
            "CR 603.1 — discarding one card creates one Zombie token");
        var z = zombies[0];
        z.BasePower.Should().Be(2);
        z.BaseToughness.Should().Be(2);
        z.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        z.IsTapped.Should().BeTrue("CR 111.6 — the token is created tapped");
        CardColors.GetColors(z).Should().Equal(new[] { ManaColor.Black },
            "CR 105 / 111.4 — the token is black");
        z.Controller.Should().BeSameAs(_alice,
            "tokens enter under the chariot's controller (CR 111.6)");
    }

    [Fact]
    public void CryptcallerChariot_YouDiscardThreeCards_CreatesThreeZombies()
    {
        var bus = new EventBus();
        WiredChariotOnBattlefield(bus);

        DiscardCard(new Instant("Bolt 1", "{R}"), _alice, bus);
        DiscardCard(new Instant("Bolt 2", "{R}"), _alice, bus);
        DiscardCard(new Instant("Bolt 3", "{R}"), _alice, bus);

        Zombies(_alice).Should().HaveCount(3,
            "CR 603.1 — 'that many' tokens: three discards => three Zombies");
    }

    [Fact]
    public void CryptcallerChariot_DiscardLand_StillCreatesZombie_NoNonlandGate()
    {
        var bus = new EventBus();
        WiredChariotOnBattlefield(bus);

        DiscardCard(new Land("Swamp"), _alice, bus);

        Zombies(_alice).Should().HaveCount(1,
            "CR 701.8 — 'discard one or more cards' counts every card type, lands included");
    }

    [Fact]
    public void CryptcallerChariot_OpponentDiscards_DoesNotCreateZombie()
    {
        var bus = new EventBus();
        WiredChariotOnBattlefield(bus);

        DiscardCard(new Instant("Bob's Bolt", "{R}"), _bob, bus);

        Zombies(_alice).Should().BeEmpty(
            "'you discard' (CR 109.5) — an opponent's discard does not create tokens");
        Zombies(_bob).Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Crew 2 (CR 702.122)
    // -----------------------------------------------------------------------

    [Fact]
    public void CryptcallerChariot_Crew2_PromotesToCreatureUntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var chariot = CryptcallerChariotFactory.Create(_alice);
        chariot.ActiveEffects = effects;
        chariot.HasSummoningSickness = false;

        var crew = new Creature("Zombie", "", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            HasSummoningSickness = false,
        };

        var result = CrewAction.Crew(
            chariot,
            crewCost: CryptcallerChariotFactory.CrewCost,
            vehiclePower: CryptcallerChariotFactory.VehiclePower,
            vehicleToughness: CryptcallerChariotFactory.VehicleToughness,
            new[] { crew },
            effects);

        result.Success.Should().BeTrue("2 power is enough to crew 2 (CR 702.122)");
        crew.IsTapped.Should().BeTrue("crewmates tap to crew (CR 702.122)");
        chariot.Power.Should().Be(5,
            "VehicleCrewEffect ships base power 5 through Layer 7b");
        chariot.Toughness.Should().Be(5,
            "VehicleCrewEffect ships base toughness 5 through Layer 7b");
    }
}
