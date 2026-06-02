using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SlipperyBogleFactory"/>.
///
/// Slippery Bogle (Eventide / Modern Horizons, {G/U}). Creature — Beast 1/1.
/// Oracle text (verified against Scryfall):
///   "Hexproof (This creature can't be the target of spells or abilities
///    your opponents control.)"
///
/// Covers:
/// - Identity (name, {G/U} hybrid cost, Beast subtype, 1/1, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Hexproof keyword marker attached.
/// - Hexproof (CR 702.11) — unconditional: opponents can't target, controller
///   can; unaffected by tap state.
/// </summary>
[Trait("Color", "GU")]
public class SlipperyBogleFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity + dispatch ──────────────────────────────────────────────

    [Fact]
    public void SlipperyBogle_Identity()
    {
        var c = SlipperyBogleFactory.Create(_alice);

        c.Name.Should().Be("Slippery Bogle");
        c.ManaCost.Should().Be("{G/U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SlipperyBogle_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Slippery Bogle", _alice);

        c.Should().NotBeNull("Slippery Bogle is registered via [CardName].");
        c!.Name.Should().Be("Slippery Bogle");
        c.Should().BeAssignableTo<Creature>();
    }

    // ── Hexproof keyword marker ──────────────────────────────────────────

    [Fact]
    public void SlipperyBogle_HasHexproofKeywordMarker()
    {
        var c = SlipperyBogleFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Hexproof",
            "card-text marker for the Hexproof keyword (CR 702.11) — read by " +
            "TargetLegality to reject opponents' targeting");
    }

    // ── Hexproof (CR 702.11) — unconditional read path ───────────────────

    private static TargetSpec CreatureTargetSpec() =>
        new TargetSpec("target creature").Creatures();

    [Fact]
    public void SlipperyBogle_IsHexproofFromOpponents()
    {
        var c = SlipperyBogleFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        TargetLegality.IsLegal(CreatureTargetSpec(), c, _bob)
            .Should().BeFalse("Slippery Bogle has hexproof from opponents (CR 702.11).");
    }

    [Fact]
    public void SlipperyBogle_ControllerCanStillTarget()
    {
        var c = SlipperyBogleFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        TargetLegality.IsLegal(CreatureTargetSpec(), c, _alice)
            .Should().BeTrue("hexproof only blocks opponents' spells/abilities (CR 702.11b).");
    }

    [Fact]
    public void SlipperyBogle_HexproofIsUnconditional_EvenWhenTapped()
    {
        var c = SlipperyBogleFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        c.Tap();

        TargetLegality.IsLegal(CreatureTargetSpec(), c, _bob)
            .Should().BeFalse("a tapped Slippery Bogle keeps hexproof (unconditional, CR 702.11).");
    }
}
