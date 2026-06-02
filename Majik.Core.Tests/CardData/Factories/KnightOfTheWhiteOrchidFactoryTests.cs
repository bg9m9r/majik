using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="KnightOfTheWhiteOrchidFactory"/> (Shards of Alara /
/// reprints, {W}{W}, Creature — Human Knight 2/2). Oracle text (verified
/// against Scryfall 2026-05):
///   "First strike
///    When this creature enters, if an opponent controls more lands than
///    you, you may search your library for a Plains card, put it onto the
///    battlefield, then shuffle."
///
/// Covers:
///   - Card identity (Creature, Human Knight, 2/2, {W}{W}, owner/controller).
///   - First strike keyword marker (CR 702.7).
///   - NamedCardFactory dispatch.
///   - Single ETB triggered ability attached.
///   - Intervening-if (CR 603.4): true only when an opponent controls
///     strictly more lands than you.
///   - Resolve: tutors a Plains card from library onto the battlefield
///     untapped, then is a single search (CR 701.20a shuffle).
///   - Resolve: no Plains in library → no-op.
///   - Resolve: "Plains card" matches the Plains land subtype (CR 305.6).
/// </summary>
[Trait("Color", "W")]
public class KnightOfTheWhiteOrchidFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ───────────────────────────────────────────────────────────────────
    // Identity / dispatch
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void KnightOfTheWhiteOrchid_IsCreatureHumanKnight2_2_AtCostWW()
    {
        var card = KnightOfTheWhiteOrchidFactory.Create(_alice);

        card.Name.Should().Be("Knight of the White Orchid");
        card.ManaCost.Should().Be("{W}{W}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        card.Power.Should().Be(2);
        card.Toughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KnightOfTheWhiteOrchid_HasFirstStrike()
    {
        var card = KnightOfTheWhiteOrchidFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "First strike",
                "Knight of the White Orchid has first strike (CR 702.7).");
    }
    [Fact]
    public void KnightOfTheWhiteOrchid_HasExactlyOneTriggeredAbility()
    {
        var card = KnightOfTheWhiteOrchidFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one ETB trigger on Knight of the White Orchid.");
    }

    // ───────────────────────────────────────────────────────────────────
    // Intervening-if (CR 603.4) — "if an opponent controls more lands"
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void InterveningIf_True_WhenAnOpponentControlsMoreLands()
    {
        SeedLandOnBattlefield("Island", _bob);
        SeedLandOnBattlefield("Island", _bob);
        // Alice controls zero lands; Bob controls two → 2 > 0.

        var card = KnightOfTheWhiteOrchidFactory.Create(
            _alice,
            triggers: null,
            opponentsResolver: () => new[] { _bob });
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.CanBePutOnStack().Should().BeTrue(
            "an opponent controls strictly more lands than you (CR 603.4).");
    }

    [Fact]
    public void InterveningIf_False_WhenYouControlAtLeastAsManyLands()
    {
        SeedLandOnBattlefield("Plains", _alice);
        SeedLandOnBattlefield("Plains", _alice);
        SeedLandOnBattlefield("Island", _bob);
        SeedLandOnBattlefield("Island", _bob);
        // Tie (2 vs 2) → "more lands than you" is false (strict).

        var card = KnightOfTheWhiteOrchidFactory.Create(
            _alice,
            triggers: null,
            opponentsResolver: () => new[] { _bob });
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.CanBePutOnStack().Should().BeFalse(
            "a tie is not 'more lands than you' — the comparison is strict (CR 603.4).");
    }

    [Fact]
    public void InterveningIf_False_WhenNoOpponentResolver()
    {
        // Shape-only path: no opponents resolver → cannot beat your land
        // count, so the intervening-if is false (no opponent to compare).
        var card = KnightOfTheWhiteOrchidFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.CanBePutOnStack().Should().BeFalse();
    }

    // ───────────────────────────────────────────────────────────────────
    // Resolve: tutor a Plains card to the battlefield untapped
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_TutorsAPlainsCardOntoBattlefieldUntapped()
    {
        // Bob out-lands Alice so the intervening-if re-check at resolution
        // (CR 603.4) passes.
        SeedLandOnBattlefield("Island", _bob);
        var plains = SeedInLibrary("Plains", _alice);
        var forest = SeedInLibrary("Forest", _alice);

        var card = KnightOfTheWhiteOrchidFactory.Create(
            _alice,
            triggers: null,
            opponentsResolver: () => new[] { _bob });
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().Contain(plains,
            "the Plains was tutored onto the battlefield.");
        _alice.Zones.Library.GetCards().Should().NotContain(plains);
        _alice.Zones.Library.GetCards().Should().Contain(forest,
            "non-Plains cards are not eligible.");

        plains.Should().BeOfType<Land>();
        ((Permanent)plains).IsTapped.Should().BeFalse(
            "no 'tapped' qualifier — Knight of the White Orchid's Plains enters untapped.");
    }

    [Fact]
    public void Resolve_OnlyPlainsSubtypeCardsAreEligible()
    {
        SeedLandOnBattlefield("Island", _bob);
        // A non-basic land typed Plains qualifies (CR 305.6 — "a Plains
        // card" reads the land subtype, not the Basic supertype).
        var typedPlains = new Land(
            "Sacred Foundry",
            subtypes: new[] { CardSubtype.Mountain, CardSubtype.Plains });
        typedPlains.SetOwner(_alice);
        typedPlains.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(typedPlains);

        var card = KnightOfTheWhiteOrchidFactory.Create(
            _alice,
            triggers: null,
            opponentsResolver: () => new[] { _bob });
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().Contain(typedPlains,
            "a card with the Plains land subtype is 'a Plains card' (CR 305.6).");
    }

    [Fact]
    public void Resolve_NoPlainsInLibrary_IsNoOp()
    {
        // Intervening-if passes (Bob out-lands Alice) but the library has
        // no Plains, so the "may search" finds nothing.
        SeedLandOnBattlefield("Island", _bob);
        SeedInLibrary("Forest", _alice);

        var card = KnightOfTheWhiteOrchidFactory.Create(
            _alice,
            triggers: null,
            opponentsResolver: () => new[] { _bob });
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        Action act = () => { foreach (var e in trigger.Effects) e.Execute(); };
        act.Should().NotThrow(
            "no Plains → 'may search' finds nothing; legal no-op (CR 701.19a).");
        _alice.Zones.Battlefield.GetCards().Count(c => c.HasType(CardType.Land))
            .Should().Be(0);
    }

    // ───────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────

    private static ICard SeedInLibrary(string name, Player owner)
    {
        var card = NamedCardFactory.Create(name, owner);
        card.SetZone(ZoneType.Library);
        owner.Zones.Library.AddCard(card);
        return card;
    }

    private static void SeedLandOnBattlefield(string name, Player owner)
    {
        var card = NamedCardFactory.Create(name, owner);
        card.SetController(owner);
        card.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
    }
}
