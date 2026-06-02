using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RamunapExcavatorFactory"/>.
///
/// Card: Ramunap Excavator — Creature — Naga Cleric {1}{G}{G} 2/3
/// (Hour of Devastation). "You may play lands from your graveyard."
///
/// Covers:
///   - Identity (Creature, Naga Cleric, 2/3, {1}{G}{G}, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Static-ability marker (description, battlefield gate).
///   - Per-card permission stamp: lands currently in the controller's
///     graveyard at construction time get
///     <see cref="Card.MayPlayFromGraveyard"/> = true.
///   - Non-land cards in graveyard are NOT stamped.
///   - Opponent's graveyard lands are NOT stamped (Excavator is
///     "your"-scoped).
///   - Bus-aware overload: lands entering the controller's graveyard
///     after construction are stamped via <see cref="CardMovedEvent"/>.
/// </summary>
[Trait("Color", "G")]
public class RamunapExcavatorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public RamunapExcavatorFactoryTests()
    {
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Excavator_Identity()
    {
        var excavator = RamunapExcavatorFactory.Create(_alice);

        excavator.Name.Should().Be("Ramunap Excavator");
        excavator.ManaCost.Should().Be("{1}{G}{G}");
        excavator.HasType(CardType.Creature).Should().BeTrue();
        excavator.HasSubtype(CardSubtype.Naga).Should().BeTrue();
        excavator.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        excavator.Power.Should().Be(2);
        excavator.Toughness.Should().Be(3);
        excavator.Owner.Should().BeSameAs(_alice);
        excavator.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Static-ability marker
    // -----------------------------------------------------------------------

    [Fact]
    public void Excavator_HasStaticAbility_WithPrintedDescription()
    {
        var excavator = RamunapExcavatorFactory.Create(_alice);

        var statics = excavator.Abilities.OfType<StaticAbility>().ToList();
        statics.Should().HaveCount(1);
        statics[0].Description.Should().Contain("play lands from your graveyard");
    }

    [Fact]
    public void Excavator_StaticAbility_GatedOnBattlefield()
    {
        var excavator = RamunapExcavatorFactory.Create(_alice);
        var staticAbility = excavator.Abilities.OfType<StaticAbility>().Single();

        // Excavator starts in nowhere; static ability should be inactive.
        staticAbility.IsActive().Should().BeFalse(
            "static abilities don't function off-battlefield (CR 603.6e)");

        // Move to battlefield — now active.
        _alice.Zones.Battlefield.AddCard(excavator);
        excavator.SetZone(ZoneType.Battlefield);

        staticAbility.IsActive().Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Snapshot per-card permission stamp
    // -----------------------------------------------------------------------

    [Fact]
    public void Excavator_Snapshot_StampsLandsAlreadyInGraveyard()
    {
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        var wasteland = new Land("Wasteland");
        wasteland.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(forest);
        _alice.Zones.Graveyard.AddCard(wasteland);

        forest.MayPlayFromGraveyard.Should().BeFalse("not stamped yet");
        wasteland.MayPlayFromGraveyard.Should().BeFalse();

        // Construct Excavator — snapshot path stamps current graveyard lands.
        var _ = RamunapExcavatorFactory.Create(_alice);

        forest.MayPlayFromGraveyard.Should().BeTrue();
        wasteland.MayPlayFromGraveyard.Should().BeTrue();
    }

    [Fact]
    public void Excavator_Snapshot_DoesNotStampNonLandCards()
    {
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(bolt);

        var _ = RamunapExcavatorFactory.Create(_alice);

        bolt.MayPlayFromGraveyard.Should().BeFalse(
            "Ramunap Excavator only applies to land cards");
    }

    [Fact]
    public void Excavator_Snapshot_DoesNotStampOpponentsGraveyardLands()
    {
        var bobForest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        bobForest.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(bobForest);

        // Alice's Excavator doesn't stamp Bob's graveyard lands.
        var _ = RamunapExcavatorFactory.Create(_alice);

        bobForest.MayPlayFromGraveyard.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Bus-aware permission stamp (lands entering graveyard after ETB)
    // -----------------------------------------------------------------------

    [Fact]
    public void Excavator_BusAware_StampsLandsThatEnterGraveyardAfterEtb()
    {
        var excavator = RamunapExcavatorFactory.Create(_alice, _bus);
        // Put excavator on the battlefield so the lifecycle gate accepts.
        _alice.Zones.Battlefield.AddCard(excavator);
        excavator.SetZone(ZoneType.Battlefield);

        // Alice mills a land into her graveyard.
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        forest.MayPlayFromGraveyard.Should().BeFalse("before move");

        _zones.MoveCard(forest, ZoneType.Library, ZoneType.Graveyard, _alice);

        forest.MayPlayFromGraveyard.Should().BeTrue(
            "bus-aware lifecycle stamps lands entering controller's graveyard");
    }

    [Fact]
    public void Excavator_BusAware_DoesNotStampOpponentLandsEnteringGraveyard()
    {
        var excavator = RamunapExcavatorFactory.Create(_alice, _bus);
        _alice.Zones.Battlefield.AddCard(excavator);
        excavator.SetZone(ZoneType.Battlefield);

        var bobForest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        bobForest.SetOwner(_bob);
        _bob.Zones.Library.AddCard(bobForest);
        bobForest.SetZone(ZoneType.Library);

        _zones.MoveCard(bobForest, ZoneType.Library, ZoneType.Graveyard, _bob);

        bobForest.MayPlayFromGraveyard.Should().BeFalse(
            "Excavator scoped to controller's graveyard only");
    }

    [Fact]
    public void Excavator_BusAware_DoesNotStampNonLandEntries()
    {
        var excavator = RamunapExcavatorFactory.Create(_alice, _bus);
        _alice.Zones.Battlefield.AddCard(excavator);
        excavator.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        _zones.MoveCard(bolt, ZoneType.Library, ZoneType.Graveyard, _alice);

        bolt.MayPlayFromGraveyard.Should().BeFalse();
    }

    [Fact]
    public void Excavator_BusAware_StaysSilentWhenExcavatorNotOnBattlefield()
    {
        // Bus-aware overload but Excavator never enters the battlefield.
        var excavator = RamunapExcavatorFactory.Create(_alice, _bus);
        // Excavator is in nowhere (default zone after construction).

        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        _zones.MoveCard(forest, ZoneType.Library, ZoneType.Graveyard, _alice);

        forest.MayPlayFromGraveyard.Should().BeFalse(
            "Excavator isn't on battlefield → permission doesn't apply");
    }
}
