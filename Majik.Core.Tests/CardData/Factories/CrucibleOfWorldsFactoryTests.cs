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
/// Unit tests for <see cref="CrucibleOfWorldsFactory"/>.
///
/// Card: Crucible of Worlds — Artifact {3} (Fifth Dawn).
///   "You may play land cards from your graveyard."
///
/// Covers:
///   - Identity (name, type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Static-ability marker (description, battlefield gate).
///   - Per-card permission stamp: lands currently in the controller's
///     graveyard at construction time get
///     <see cref="Card.MayPlayFromGraveyard"/> = true.
///   - Non-land cards in graveyard are NOT stamped.
///   - Opponent's graveyard lands are NOT stamped (Crucible is "your"-scoped).
///   - Bus-aware overload: lands entering the controller's graveyard
///     after construction are stamped via <see cref="CardMovedEvent"/>.
/// </summary>
public class CrucibleOfWorldsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public CrucibleOfWorldsFactoryTests()
    {
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Crucible_Identity()
    {
        var crucible = CrucibleOfWorldsFactory.Create(_alice);

        crucible.Name.Should().Be("Crucible of Worlds");
        crucible.ManaCost.Should().Be("{3}");
        crucible.HasType(CardType.Artifact).Should().BeTrue();
        crucible.Owner.Should().BeSameAs(_alice);
        crucible.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Crucible_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Crucible of Worlds", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Crucible of Worlds");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{3}");
    }

    // -----------------------------------------------------------------------
    // Static-ability marker
    // -----------------------------------------------------------------------

    [Fact]
    public void Crucible_HasStaticAbility_WithPrintedDescription()
    {
        var crucible = CrucibleOfWorldsFactory.Create(_alice);

        var statics = crucible.Abilities.OfType<StaticAbility>().ToList();
        statics.Should().HaveCount(1);
        statics[0].Description.Should().Contain("play land cards from your graveyard");
    }

    [Fact]
    public void Crucible_StaticAbility_GatedOnBattlefield()
    {
        var crucible = CrucibleOfWorldsFactory.Create(_alice);
        var staticAbility = crucible.Abilities.OfType<StaticAbility>().Single();

        // Crucible starts in nowhere; static ability should be inactive.
        staticAbility.IsActive().Should().BeFalse(
            "static abilities don't function off-battlefield (CR 603.6e)");

        // Move to battlefield — now active.
        _alice.Zones.Battlefield.AddCard(crucible);
        crucible.SetZone(ZoneType.Battlefield);

        staticAbility.IsActive().Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Snapshot per-card permission stamp
    // -----------------------------------------------------------------------

    [Fact]
    public void Crucible_Snapshot_StampsLandsAlreadyInGraveyard()
    {
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        var wasteland = new Land("Wasteland");
        wasteland.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(forest);
        _alice.Zones.Graveyard.AddCard(wasteland);

        forest.MayPlayFromGraveyard.Should().BeFalse("not stamped yet");
        wasteland.MayPlayFromGraveyard.Should().BeFalse();

        // Construct Crucible — snapshot path stamps current graveyard lands.
        var _ = CrucibleOfWorldsFactory.Create(_alice);

        forest.MayPlayFromGraveyard.Should().BeTrue();
        wasteland.MayPlayFromGraveyard.Should().BeTrue();
    }

    [Fact]
    public void Crucible_Snapshot_DoesNotStampNonLandCards()
    {
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(bolt);

        var _ = CrucibleOfWorldsFactory.Create(_alice);

        bolt.MayPlayFromGraveyard.Should().BeFalse(
            "Crucible only applies to land cards");
    }

    [Fact]
    public void Crucible_Snapshot_DoesNotStampOpponentsGraveyardLands()
    {
        var bobForest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        bobForest.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(bobForest);

        // Alice's Crucible doesn't stamp Bob's graveyard lands.
        var _ = CrucibleOfWorldsFactory.Create(_alice);

        bobForest.MayPlayFromGraveyard.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Bus-aware permission stamp (lands entering graveyard after ETB)
    // -----------------------------------------------------------------------

    [Fact]
    public void Crucible_BusAware_StampsLandsThatEnterGraveyardAfterEtb()
    {
        var crucible = CrucibleOfWorldsFactory.Create(_alice, _bus);
        // Put crucible on the battlefield so the lifecycle gate accepts.
        _alice.Zones.Battlefield.AddCard(crucible);
        crucible.SetZone(ZoneType.Battlefield);

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
    public void Crucible_BusAware_DoesNotStampOpponentLandsEnteringGraveyard()
    {
        var crucible = CrucibleOfWorldsFactory.Create(_alice, _bus);
        _alice.Zones.Battlefield.AddCard(crucible);
        crucible.SetZone(ZoneType.Battlefield);

        var bobForest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        bobForest.SetOwner(_bob);
        _bob.Zones.Library.AddCard(bobForest);
        bobForest.SetZone(ZoneType.Library);

        _zones.MoveCard(bobForest, ZoneType.Library, ZoneType.Graveyard, _bob);

        bobForest.MayPlayFromGraveyard.Should().BeFalse(
            "Crucible scoped to controller's graveyard only");
    }

    [Fact]
    public void Crucible_BusAware_DoesNotStampNonLandEntries()
    {
        var crucible = CrucibleOfWorldsFactory.Create(_alice, _bus);
        _alice.Zones.Battlefield.AddCard(crucible);
        crucible.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        _zones.MoveCard(bolt, ZoneType.Library, ZoneType.Graveyard, _alice);

        bolt.MayPlayFromGraveyard.Should().BeFalse();
    }

    [Fact]
    public void Crucible_BusAware_StaysSilentWhenCrucibleNotOnBattlefield()
    {
        // Bus-aware overload but Crucible never enters the battlefield.
        var crucible = CrucibleOfWorldsFactory.Create(_alice, _bus);
        // Crucible is in nowhere (default zone after construction).

        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        _zones.MoveCard(forest, ZoneType.Library, ZoneType.Graveyard, _alice);

        forest.MayPlayFromGraveyard.Should().BeFalse(
            "Crucible isn't on battlefield → permission doesn't apply");
    }
}
