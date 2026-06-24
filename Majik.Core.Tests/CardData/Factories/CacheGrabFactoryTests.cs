using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Cache Grab (Bloomburrow, {1}{G}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Mill four cards. You may put a permanent card from among the cards
///    milled this way into your hand. If you control a Squirrel or returned a
///    Squirrel card to your hand this way, create a Food token."
///
/// Card shape comes from the embedded JSON (<c>cache-grab.json</c>); the
/// resolve-time body is built by <see cref="CacheGrabFactory.BuildResolveEffect"/>
/// and routes through the shared <see cref="RevealAndChoose"/> primitive (same
/// posture as <see cref="MalevolentRumbleFactory"/>, but the token half is the
/// conditional Food rider instead of an unconditional Eldrazi Spawn).
///
/// Covers ONLY the card's unique behaviour:
///   - Identity ({1}{G}, Instant) — single _Identity assert.
///   - Mill four: top four library cards leave the library.
///   - A permanent in the milled four goes to hand; the rest go to graveyard.
///   - Empty library: clean no-op (no throw); no Food without a Squirrel.
///   - Food rider: created when controlling a Squirrel.
///   - Food rider: created when a Squirrel card was returned to hand.
///   - Food rider: NOT created when no Squirrel is involved.
/// </summary>
[Trait("Color", "G")]
public class CacheGrabFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity ──────────────────────────────────────────────────────────────

    [Fact]
    public void CacheGrab_Identity()
    {
        var card = CacheGrabFactory.Create(_alice);

        card.Name.Should().Be("Cache Grab");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // ── Mill four / permanent → hand / rest → graveyard ───────────────────────

    [Fact]
    public void Resolve_MillsTopFour_FromLibrary()
    {
        // Seed 6 cards on the library (only the top 4 are milled).
        var c1 = SeedLibraryCard(new Instant("I1", ""));
        var c2 = SeedLibraryCard(new Instant("I2", ""));
        var c3 = SeedLibraryCard(new Instant("I3", ""));
        var c4 = SeedLibraryCard(new Instant("I4", ""));
        var c5 = SeedLibraryCard(new Instant("I5", ""));
        var c6 = SeedLibraryCard(new Instant("I6", ""));

        Resolve();

        _alice.Zones.Library.GetCards().Should().NotContain(new[] { c1, c2, c3, c4 });
        _alice.Zones.Library.GetCards().Should().Contain(new[] { c5, c6 });
    }

    [Fact]
    public void Resolve_FirstPermanent_GoesToHand_NonPermanents_GoToGraveyard()
    {
        var instant1 = SeedLibraryCard(new Instant("Counterspell", "UU"));
        var sorcery = SeedLibraryCard(new Sorcery("Doom Blade", "1B"));
        var bear = SeedLibraryCard(new Creature("Bear", "1G", 2, 2));
        var instant2 = SeedLibraryCard(new Instant("Shock", "R"));

        Resolve();

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(bear, "a permanent card among the milled four goes to hand");
        bear.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Graveyard.GetCards()
            .Should().Contain(new Card[] { instant1, sorcery, instant2 },
                "the non-permanent milled cards go to the graveyard");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void Resolve_EmptyLibrary_DoesNotThrow_AndCreatesNoFood_WithoutSquirrel()
    {
        var act = () => Resolve();

        act.Should().NotThrow("empty library is a clean no-op for the mill half");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        FindFood(_alice).Should().BeNull("no Squirrel controlled or returned → no Food token");
    }

    // ── Food rider ────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_NoSquirrel_CreatesNoFood()
    {
        SeedLibraryCard(new Creature("Bear", "1G", 2, 2));

        Resolve();

        FindFood(_alice).Should().BeNull(
            "you neither control a Squirrel nor returned a Squirrel card to hand");
    }

    [Fact]
    public void Resolve_ControlsSquirrel_CreatesFood()
    {
        // A Squirrel on the battlefield satisfies "if you control a Squirrel".
        var squirrel = new Creature("Chittering Squirrel", "G", 1, 1,
            subtypes: new[] { CardSubtype.Squirrel });
        squirrel.SetOwner(_alice);
        squirrel.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(squirrel);
        squirrel.SetZone(ZoneType.Battlefield);

        // Mill a non-Squirrel permanent so the Food can only come from control.
        SeedLibraryCard(new Creature("Bear", "1G", 2, 2));

        Resolve();

        FindFood(_alice).Should().NotBeNull("controlling a Squirrel creates a Food token");
    }

    [Fact]
    public void Resolve_ReturnsSquirrelCardToHand_CreatesFood()
    {
        // No Squirrel on the battlefield — the Food must come from the
        // Squirrel CARD returned to hand by this spell.
        var squirrelCard = new Creature("Acornelia, Fashionable Filcher", "1GW", 2, 2,
            subtypes: new[] { CardSubtype.Squirrel });
        SeedLibraryCard(squirrelCard);

        Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(squirrelCard,
            "the Squirrel permanent card is put into hand");
        FindFood(_alice).Should().NotBeNull(
            "returning a Squirrel card to hand this way creates a Food token");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Resolve()
    {
        foreach (var e in CacheGrabFactory.BuildResolveEffect(_alice))
            e.Execute();
    }

    private T SeedLibraryCard<T>(T card) where T : Card
    {
        card.SetOwner(_alice);
        card.SetController(_alice);
        _alice.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        return card;
    }

    private static Artifact? FindFood(Player p) =>
        p.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .FirstOrDefault(c => c.Name == "Food");
}
