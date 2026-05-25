using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="RealitySmasherFactory"/>
/// (Oath of the Gatewatch, {4}{C}).
///
/// Creature — Eldrazi 5/5. Oracle text:
///   "Trample, haste
///    Whenever this creature becomes the target of a spell an opponent
///    controls, counter that spell unless its controller discards a card."
///
/// Covers:
///   - Identity (Creature — Eldrazi, {4}{C}, 5/5, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Trample + Haste + Ward keyword markers attached.
///   - <see cref="RealitySmasherFactory.BuildWardEffect"/> exposes a
///     bound <see cref="Majik.Core.Keywords.WardEffect"/> with mana-zero
///     cost (non-mana discard rider deferred).
/// </summary>
public class RealitySmasherFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void RealitySmasher_Identity()
    {
        var smasher = RealitySmasherFactory.Create(_alice);

        smasher.Name.Should().Be("Reality Smasher");
        smasher.ManaCost.Should().Be("{4}{C}");
        smasher.HasType(CardType.Creature).Should().BeTrue();
        smasher.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        smasher.BasePower.Should().Be(5);
        smasher.BaseToughness.Should().Be(5);
        smasher.Owner.Should().BeSameAs(_alice);
        smasher.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RealitySmasher_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Reality Smasher", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Reality Smasher");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(5);
        ((Creature)card).BaseToughness.Should().Be(5);
    }

    [Fact]
    public void RealitySmasher_HasTrampleHasteAndWardMarkers()
    {
        var smasher = RealitySmasherFactory.Create(_alice);
        var keywords = smasher.Abilities.OfType<KeywordAbility>().ToList();

        keywords.Should().Contain(k => k.Keyword == "Trample",
            "CR 702.19 — Trample marker");
        keywords.Should().Contain(k => k.Keyword == "Haste",
            "CR 702.10 — Haste marker");
        keywords.Should().Contain(k => k.Keyword == "Ward",
            "CR 702.21 — Ward marker (printed 'discard a card' rider deferred)");
    }

    [Fact]
    public void RealitySmasher_BuildWardEffect_ExposesManaZeroCost()
    {
        // Printed Ward cost is non-mana ("discard a card") — the helper's
        // mana portion is zero; the discard rider is documentation-only
        // (see WardDiscardCost) until the non-mana Ward primitive lands.
        var smasher = RealitySmasherFactory.Create(_alice);
        var ward = RealitySmasherFactory.BuildWardEffect(smasher);

        ward.Source.Should().BeSameAs(smasher);
        ward.Cost.TotalValue.Should().Be(0,
            "printed cost is non-mana — mana portion is zero");
        RealitySmasherFactory.WardDiscardCost.Should().Be("Discard a card");
    }
}
