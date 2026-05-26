using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="AetherworksMarvelFactory"/> (Kaladesh).
///
/// v1 oracle (per factory — energy-trigger only; activated ability
/// deferred pending cast-from-library-without-paying primitive):
///   "Whenever a permanent you control is put into a graveyard, you
///    get {E}. {T}, Pay {E}{E}{E}{E}{E}{E}: Look at the top six cards
///    of your library. You may cast a spell from among them without
///    paying its mana cost. Put the rest on the bottom of your library
///    in a random order."
///
/// Covers:
/// - Identity (Legendary Artifact {4}).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Death trigger fires when a controlled permanent dies → controller
///   gains {E}.
/// - Trigger filters: ignores stack→graveyard moves (instants /
///   sorceries) and opponent-controlled deaths.
/// - Marvel's own death also fires the trigger (no "another" rider).
/// </summary>
public class AetherworksMarvelFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherworksMarvel_Identity_LegendaryArtifact()
    {
        var marvel = AetherworksMarvelFactory.Create(_alice);

        marvel.Name.Should().Be("Aetherworks Marvel");
        marvel.HasType(CardType.Artifact).Should().BeTrue();
        marvel.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "CR 205.4a — Aetherworks Marvel is Legendary");
        marvel.ManaCost.ToString().Should().Be("{4}");
        marvel.Owner.Should().BeSameAs(_alice);
        marvel.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AetherworksMarvel_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Aetherworks Marvel", _alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Aetherworks Marvel");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Death trigger — gain {E} when a controlled permanent dies
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherworksMarvel_HasExactlyOneTriggeredAbility()
    {
        var marvel = AetherworksMarvelFactory.Create(_alice);

        marvel.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the death-of-a-permanent-you-control energy trigger");
    }

    [Fact]
    public void AetherworksMarvel_DeathTrigger_FiresOnControlledCreatureDeath()
    {
        var alice = new Player("Alice", 20);
        var marvel = AetherworksMarvelFactory.Create(alice);
        var trigger = marvel.Abilities.OfType<TriggeredAbility>().Single();

        // Construct a controlled creature and synthesize its death event.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        bear.SetController(alice);
        var ev = new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.Condition.Matches(ev, trigger).Should().BeTrue(
            "permanent you control was put into a graveyard from the battlefield");
    }

    [Fact]
    public void AetherworksMarvel_DeathTrigger_DoesNotFireForInstantToGraveyard()
    {
        // Instant going Stack→Graveyard does NOT fire — the printed
        // wording is "permanent", and the FromZone check rejects
        // anything but Battlefield→Graveyard.
        var alice = new Player("Alice", 20);
        var marvel = AetherworksMarvelFactory.Create(alice);
        var trigger = marvel.Abilities.OfType<TriggeredAbility>().Single();

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(alice);
        bolt.SetController(alice);
        var ev = new CardMovedEvent(bolt, ZoneType.Stack, ZoneType.Graveyard);

        trigger.Condition.Matches(ev, trigger).Should().BeFalse(
            "instants resolve from the stack to graveyard — not a permanent death");
    }

    [Fact]
    public void AetherworksMarvel_DeathTrigger_DoesNotFireForOpponentDeath()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var marvel = AetherworksMarvelFactory.Create(alice);
        var trigger = marvel.Abilities.OfType<TriggeredAbility>().Single();

        // Bob's creature dies — Alice's Marvel does NOT trigger
        // (printed "permanent YOU control").
        var bobCreature = new Creature("Bob's Bear", "{1}{G}", 2, 2);
        bobCreature.SetOwner(bob);
        bobCreature.SetController(bob);
        var ev = new CardMovedEvent(
            bobCreature, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.Condition.Matches(ev, trigger).Should().BeFalse(
            "opponent's permanent — not a controller match");
    }

    [Fact]
    public void AetherworksMarvel_DeathTrigger_FiresOnMarvelsOwnDeath()
    {
        // Printed wording has no "another" rider — Marvel's own death
        // satisfies "a permanent you control was put into a graveyard".
        var alice = new Player("Alice", 20);
        var marvel = AetherworksMarvelFactory.Create(alice);
        var trigger = marvel.Abilities.OfType<TriggeredAbility>().Single();

        var ev = new CardMovedEvent(
            marvel, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.Condition.Matches(ev, trigger).Should().BeTrue(
            "Marvel's own death feeds its own trigger (no \"another\" rider)");
    }

    [Fact]
    public void AetherworksMarvel_DeathTriggerResolution_GrantsOneEnergy()
    {
        var alice = new Player("Alice", 20);
        var marvel = AetherworksMarvelFactory.Create(alice);
        var trigger = marvel.Abilities.OfType<TriggeredAbility>().Single();

        alice.EnergyCounters.Should().Be(0);

        foreach (var effect in trigger.Effects) effect.Execute();

        alice.EnergyCounters.Should().Be(1,
            "death trigger grants one energy on resolve (CR 106.13b)");
    }

    [Fact]
    public void AetherworksMarvel_DeathTrigger_ActiveZones_IncludeGraveyard()
    {
        // The trigger must remain live after Marvel itself moves to the
        // graveyard so the self-death case fires (mirrors Nihil
        // Spellbomb's sacrifice trigger reading from the graveyard).
        var marvel = AetherworksMarvelFactory.Create(_alice);
        var trigger = marvel.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().Contain(ZoneType.Graveyard);
    }
}
