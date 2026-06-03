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

    // ------------------------------------------------------------------
    // Cast-from-top of NONLANDS (deferral: cast-artifacts-from-top, Mystic
    // Forge). CR 601.3e — an effect may let you cast a card from a zone other
    // than your hand. The Artifacts / Colorless / Any filters drive
    // MayCastTopCard, the CAST analogue of MayPlayTopCard (which is lands-only).
    // ------------------------------------------------------------------

    private Artifact PutArtifactOnTop(Player p, string name = "Ornithopter", string cost = "{0}")
    {
        var artifact = new Artifact(name, cost);
        artifact.SetOwner(p);
        p.Zones.Library.AddCard(artifact);
        artifact.SetZone(ZoneType.Library);
        return artifact;
    }

    [Fact]
    public void ArtifactGrant_TopArtifact_IsCastable_ButNotPlayableAsLand()
    {
        var art = PutArtifactOnTop(_alice);
        LibraryTopPlayPermissions.AddGrant(new object(), _alice, TopPlayFilter.Artifacts);

        LibraryTopPlayPermissions.MayCastTopCard(_alice, art).Should().BeTrue();
        LibraryTopPlayPermissions.CastableSpellFromTop(_alice).Should().BeSameAs(art);
        // A nonland artifact is not a land play.
        LibraryTopPlayPermissions.PlayableLandFromTop(_alice).Should().BeNull();
    }

    [Fact]
    public void ArtifactGrant_DoesNotMakeLandsCastable_NorNonArtifactSpells()
    {
        // A nonartifact, colored spell on top is NOT castable under an
        // Artifacts grant (Mystic Forge casts artifacts + colorless only).
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        PutCardOnLibrary(_alice, bolt);
        LibraryTopPlayPermissions.AddGrant(new object(), _alice, TopPlayFilter.Artifacts);

        LibraryTopPlayPermissions.MayCastTopCard(_alice, bolt).Should().BeFalse();
        LibraryTopPlayPermissions.CastableSpellFromTop(_alice).Should().BeNull();
    }

    [Fact]
    public void ColorlessGrant_TopColorlessNonArtifactSpell_IsCastable()
    {
        // Mystic Forge also casts colorless spells (e.g. an Eldrazi).
        var eldrazi = new Creature("Eldrazi", "{8}", 5, 5);
        eldrazi.SetOwner(_alice);
        PutCardOnLibrary(_alice, eldrazi);
        LibraryTopPlayPermissions.AddGrant(new object(), _alice, TopPlayFilter.Colorless);

        LibraryTopPlayPermissions.MayCastTopCard(_alice, eldrazi).Should().BeTrue(
            "a {8} creature is colorless");
        LibraryTopPlayPermissions.CastableSpellFromTop(_alice).Should().BeSameAs(eldrazi);
    }

    [Fact]
    public void ColorlessGrant_TopColoredSpell_NotCastable()
    {
        var coloredCreature = new Creature("Bear", "{1}{G}", 2, 2);
        coloredCreature.SetOwner(_alice);
        PutCardOnLibrary(_alice, coloredCreature);
        LibraryTopPlayPermissions.AddGrant(new object(), _alice, TopPlayFilter.Colorless);

        LibraryTopPlayPermissions.MayCastTopCard(_alice, coloredCreature).Should().BeFalse(
            "a {1}{G} creature is green, not colorless");
    }

    [Fact]
    public void MysticForge_DualGrant_CastsArtifactsAndColorless_NotColoredLand()
    {
        // Mystic Forge registers BOTH an Artifacts grant and a Colorless grant.
        var token = new object();
        LibraryTopPlayPermissions.AddGrant(token, _alice, TopPlayFilter.Artifacts);
        LibraryTopPlayPermissions.AddGrant(token, _alice, TopPlayFilter.Colorless);

        // Colored artifact (e.g. {1}{U} artifact) → castable via the Artifacts grant.
        var coloredArtifact = new Artifact("Coastal Piracy", "{1}{U}");
        coloredArtifact.SetOwner(_alice);
        PutCardOnLibrary(_alice, coloredArtifact);
        LibraryTopPlayPermissions.MayCastTopCard(_alice, coloredArtifact).Should().BeTrue();

        // Removing under the single token revokes BOTH grants.
        LibraryTopPlayPermissions.RemoveGrant(token);
        LibraryTopPlayPermissions.MayCastTopCard(_alice, coloredArtifact).Should().BeFalse();
    }

    [Fact]
    public void AnyGrant_AlsoCastsNonlands()
    {
        // Bolas's Citadel-style "Any" grant casts any nonland spell from top.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        PutCardOnLibrary(_alice, bolt);
        LibraryTopPlayPermissions.AddGrant(new object(), _alice, TopPlayFilter.Any);

        LibraryTopPlayPermissions.MayCastTopCard(_alice, bolt).Should().BeTrue();
    }

    [Fact]
    public void MayCastTopCard_LandOnTop_NotCastable()
    {
        // A land is never "cast" (CR 601.1 — lands are played, not cast).
        var land = PutLandOnTop(_alice);
        LibraryTopPlayPermissions.AddGrant(new object(), _alice, TopPlayFilter.Any);

        LibraryTopPlayPermissions.MayCastTopCard(_alice, land).Should().BeFalse(
            "lands are played, not cast — even an Any grant doesn't make a land 'castable'");
    }

    [Fact]
    public void CastableSpellFromTop_OnlyTopCard_Eligible()
    {
        // Bury the artifact under a non-eligible top card.
        var topLand = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        topLand.SetOwner(_alice);
        var art = new Artifact("Ornithopter", "{0}");
        art.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topLand); // top
        topLand.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(art);     // second
        art.SetZone(ZoneType.Library);
        LibraryTopPlayPermissions.AddGrant(new object(), _alice, TopPlayFilter.Artifacts);

        LibraryTopPlayPermissions.MayCastTopCard(_alice, art).Should().BeFalse(
            "only the top card is a legal cast source");
        LibraryTopPlayPermissions.CastableSpellFromTop(_alice).Should().BeNull();
    }
}
