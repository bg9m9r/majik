using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Reckoner Bankbuster (The Brothers' War, {2}, Artifact —
/// Vehicle).
///
/// Coverage:
///   - Identity (Artifact + Creature shell, Vehicle subtype, 0/4, {2}).
///   - NamedCardFactory dispatch.
///   - ETB places three charge counters on Bankbuster (CR 122).
///   - Attack trigger adds a charge counter (CR 508.1f).
///   - Activated ability draws a card per activation.
///   - Activated ability creates a Powerstone token only when no charge
///     counters remain after the draw (CR 605 "then if" tail-clause).
/// </summary>
public class ReckonerBankbusterTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ReckonerBankbuster_Identity()
    {
        var c = ReckonerBankbusterFactory.Create(_alice);

        c.Name.Should().Be("Reckoner Bankbuster");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Reckoner Bankbuster is an Artifact (Vehicle)");
        c.HasType(CardType.Creature).Should().BeTrue(
            "v1 vehicle shell is a Creature so CrewAction flows P/T through " +
            "VehicleCrewEffect");
        c.HasSubtype(CardSubtype.Vehicle).Should().BeTrue();
        c.BasePower.Should().Be(0);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ReckonerBankbuster()
    {
        var c = NamedCardFactory.Create("Reckoner Bankbuster", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Reckoner Bankbuster");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vehicle).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB — three charge counters
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_PlacesThreeChargeCounters()
    {
        var card = ReckonerBankbusterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var etb = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(
                new CardMovedEvent(card, ZoneType.Hand, ZoneType.Battlefield)));

        foreach (var e in etb.Effects) e.Execute();

        card.Counters.Count(CounterType.Charge).Should().Be(3,
            "Bankbuster enters with three charge counters (CR 122)");
    }

    // -----------------------------------------------------------------------
    // Attack trigger — +1 charge counter
    // -----------------------------------------------------------------------

    [Fact]
    public void Attack_AddsOneChargeCounter()
    {
        var card = ReckonerBankbusterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        card.Counters.Add(CounterType.Charge, 3); // post-ETB state

        var attack = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(new CreatureAttacksEvent(card, _alice)));

        foreach (var e in attack.Effects) e.Execute();

        card.Counters.Count(CounterType.Charge).Should().Be(4,
            "attack trigger adds one charge counter (CR 508.1f)");
    }

    // -----------------------------------------------------------------------
    // Activated ability — draw a card; Powerstone on zero-charge tail
    // -----------------------------------------------------------------------

    [Fact]
    public void Activated_Draws1_NoPowerstoneWhileChargesRemain()
    {
        var card = ReckonerBankbusterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        card.Counters.Add(CounterType.Charge, 3);

        // Seed the library so the draw has something to grab.
        SeedLibrary(_alice, "Top1", "Top2", "Top3");

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();

        // Pay the cost manually (tap + remove charge); then execute the effect.
        // Mirrors the test posture used by Umezawa's Jitte / other
        // counter-cost activated abilities — cost.Pay handles the bookkeeping
        // for both the tap and the counter removal.
        foreach (var cost in ability.Costs) cost.Pay(_alice);
        foreach (var effect in ability.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Select(c => c.Name).Should().ContainSingle()
            .Which.Should().Be("Top1");
        card.Counters.Count(CounterType.Charge).Should().Be(2,
            "one charge counter removed by the activation cost");
        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Should().NotContain(t => t.HasSubtype(CardSubtype.Powerstone),
                "Powerstone only spawns when no charge counters remain");
    }

    [Fact]
    public void Activated_LastChargeRemoval_SpawnsPowerstone()
    {
        var card = ReckonerBankbusterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        // Start with exactly one charge — this activation will remove the
        // last one and trigger the "create a Powerstone token" tail clause.
        card.Counters.Add(CounterType.Charge, 1);

        SeedLibrary(_alice, "Top1");

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in ability.Costs) cost.Pay(_alice);
        foreach (var effect in ability.Effects) effect.Execute();

        card.Counters.Count(CounterType.Charge).Should().Be(0);

        var powerstones = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(t => t.HasSubtype(CardSubtype.Powerstone))
            .ToList();
        powerstones.Should().ContainSingle(
            "the zero-charge tail clause creates one Powerstone token");

        var stone = powerstones[0];
        stone.IsToken.Should().BeTrue();
        stone.HasType(CardType.Artifact).Should().BeTrue();
        stone.Controller.Should().BeSameAs(_alice);

        stone.Abilities.OfType<ManaAbility>().Should().ContainSingle(
            "Powerstone produces a single mana — '{T}: Add {C}.'");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void SeedLibrary(Player player, params string[] names)
    {
        foreach (var n in names)
        {
            var c = new Card(n, "");
            c.SetOwner(player);
            c.SetController(player);
            c.SetZone(ZoneType.Library);
            player.Zones.Library.AddCard(c);
        }
    }
}
