using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Rules;

/// <summary>
/// Unit tests for <see cref="LibraryTopPlayPermissions"/> — the per-game
/// "you may play [filter] from the top of your library" registry
/// (CR 601.3e / CR 305.6 / CR 715.4).
/// </summary>
public class LibraryTopPlayPermissionsTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose() => LibraryTopPlayPermissions.Clear();

    private Land PutLandOnTop(Player p, string name = "Forest")
    {
        var land = new Land(name, subtypes: new[] { CardSubtype.Forest });
        land.SetOwner(p);
        p.Zones.Library.AddCard(land);
        land.SetZone(ZoneType.Library);
        return land;
    }

    private Card PutCardOnLibrary(Player p, ICard card)
    {
        p.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        return (Card)card;
    }

    [Fact]
    public void NoGrant_TopLand_NotPlayable()
    {
        var land = PutLandOnTop(_alice);

        LibraryTopPlayPermissions.MayPlayTopCard(_alice, land).Should().BeFalse();
        LibraryTopPlayPermissions.PlayableLandFromTop(_alice).Should().BeNull();
        LibraryTopPlayPermissions.IsTopRevealed(_alice).Should().BeFalse();
    }

    [Fact]
    public void LandGrant_TopLand_IsPlayable_AndRevealed()
    {
        var land = PutLandOnTop(_alice);
        var token = new object();
        LibraryTopPlayPermissions.AddGrant(token, _alice, TopPlayFilter.Lands);

        LibraryTopPlayPermissions.MayPlayTopCard(_alice, land).Should().BeTrue();
        LibraryTopPlayPermissions.PlayableLandFromTop(_alice).Should().BeSameAs(land);
        LibraryTopPlayPermissions.IsTopRevealed(_alice).Should().BeTrue();
    }

    [Fact]
    public void LandGrant_TopNonLand_NotPlayable()
    {
        // Top card is a nonland (instant) — Courser's land grant doesn't cover it.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        PutCardOnLibrary(_alice, bolt);
        LibraryTopPlayPermissions.AddGrant(new object(), _alice, TopPlayFilter.Lands);

        LibraryTopPlayPermissions.MayPlayTopCard(_alice, bolt).Should().BeFalse(
            "the land grant only covers land cards");
        LibraryTopPlayPermissions.PlayableLandFromTop(_alice).Should().BeNull();
    }

    [Fact]
    public void Grant_OnlyTopCard_IsPlayable_NotSecondCard()
    {
        // Bury a second land below the top land.
        var topInstant = new Instant("Opt", "{U}");
        topInstant.SetOwner(_alice);
        // Add the land FIRST so it is the top, then push an instant in front.
        var land = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        land.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topInstant); // top
        topInstant.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(land);       // second
        land.SetZone(ZoneType.Library);

        LibraryTopPlayPermissions.AddGrant(new object(), _alice, TopPlayFilter.Lands);

        LibraryTopPlayPermissions.MayPlayTopCard(_alice, land).Should().BeFalse(
            "only the top card is a legal play source, not the second land");
    }

    [Fact]
    public void Grant_IsControllerScoped()
    {
        var aliceLand = PutLandOnTop(_alice);
        var bobLand = PutLandOnTop(_bob);
        LibraryTopPlayPermissions.AddGrant(new object(), _alice, TopPlayFilter.Lands);

        LibraryTopPlayPermissions.MayPlayTopCard(_alice, aliceLand).Should().BeTrue();
        LibraryTopPlayPermissions.MayPlayTopCard(_bob, bobLand).Should().BeFalse(
            "Bob has no grant");
    }

    [Fact]
    public void RemoveGrant_RevokesPermission()
    {
        var land = PutLandOnTop(_alice);
        var token = new object();
        LibraryTopPlayPermissions.AddGrant(token, _alice, TopPlayFilter.Lands);
        LibraryTopPlayPermissions.MayPlayTopCard(_alice, land).Should().BeTrue();

        LibraryTopPlayPermissions.RemoveGrant(token);

        LibraryTopPlayPermissions.MayPlayTopCard(_alice, land).Should().BeFalse();
        LibraryTopPlayPermissions.IsTopRevealed(_alice).Should().BeFalse();
    }

    [Fact]
    public void AddGrant_IsIdempotent_PerTokenAndFilter()
    {
        var token = new object();
        LibraryTopPlayPermissions.AddGrant(token, _alice, TopPlayFilter.Lands);
        LibraryTopPlayPermissions.AddGrant(token, _alice, TopPlayFilter.Lands);

        // One RemoveGrant clears it (no duplicate entry left behind).
        LibraryTopPlayPermissions.RemoveGrant(token);
        LibraryTopPlayPermissions.HasGrant(_alice, TopPlayFilter.Lands).Should().BeFalse();
    }

    [Fact]
    public void AnyFilter_CoversLandsAndCreatures()
    {
        var land = PutLandOnTop(_alice);
        LibraryTopPlayPermissions.AddGrant(new object(), _alice, TopPlayFilter.Any);

        LibraryTopPlayPermissions.MayPlayTopCard(_alice, land).Should().BeTrue();
        LibraryTopPlayPermissions.HasGrant(_alice, TopPlayFilter.Lands).Should().BeTrue(
            "an Any grant covers the lands capability");
        LibraryTopPlayPermissions.HasGrant(_alice, TopPlayFilter.Creatures).Should().BeTrue();
    }

    [Fact]
    public void CreatureFilter_TopCreature_Playable_TopLand_Not()
    {
        var creature = new Creature("Bear", "{1}{G}", 2, 2);
        creature.SetOwner(_alice);
        PutCardOnLibrary(_alice, creature);
        LibraryTopPlayPermissions.AddGrant(new object(), _alice, TopPlayFilter.Creatures);

        LibraryTopPlayPermissions.MayPlayTopCard(_alice, creature).Should().BeTrue();
        // A creature filter does not make the land-from-top land available.
        LibraryTopPlayPermissions.PlayableLandFromTop(_alice).Should().BeNull(
            "PlayableLandFromTop only returns a top LAND");
    }

    [Fact]
    public void RevealsTopFalse_GrantStillAllowsPlay_ButNotMarkedRevealed()
    {
        var land = PutLandOnTop(_alice);
        LibraryTopPlayPermissions.AddGrant(new object(), _alice, TopPlayFilter.Lands, revealsTop: false);

        LibraryTopPlayPermissions.MayPlayTopCard(_alice, land).Should().BeTrue();
        LibraryTopPlayPermissions.IsTopRevealed(_alice).Should().BeFalse();
    }
}
