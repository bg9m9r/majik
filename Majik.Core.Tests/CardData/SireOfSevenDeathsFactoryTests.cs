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
/// Unit tests for <see cref="SireOfSevenDeathsFactory"/>
/// (Modern Horizons 3, {7}).
///
/// Creature — Eldrazi 7/7. Oracle text (Scryfall, verified):
///   "First strike, vigilance
///    Menace, trample
///    Reach, lifelink
///    Ward—Pay 7 life."
///
/// Covers:
///   - Identity (Creature — Eldrazi, {7}, 7/7, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - All six combat keyword markers attached (First Strike, Vigilance,
///     Menace, Trample, Reach, Lifelink) plus a Ward marker.
///   - <see cref="SireOfSevenDeathsFactory.BuildWardEffect"/> exposes a
///     bound <see cref="Majik.Core.Keywords.WardEffect"/> with mana-zero
///     cost (non-mana "Pay 7 life" rider deferred — see WardLifeCost).
/// </summary>
public class SireOfSevenDeathsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SireOfSevenDeaths_Identity()
    {
        var sire = SireOfSevenDeathsFactory.Create(_alice);

        sire.Name.Should().Be("Sire of Seven Deaths");
        sire.ManaCost.Should().Be("{7}");
        sire.HasType(CardType.Creature).Should().BeTrue();
        sire.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        sire.BasePower.Should().Be(7);
        sire.BaseToughness.Should().Be(7);
        sire.Owner.Should().BeSameAs(_alice);
        sire.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SireOfSevenDeaths_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Sire of Seven Deaths", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Sire of Seven Deaths");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(7);
        ((Creature)card).BaseToughness.Should().Be(7);
    }

    [Fact]
    public void SireOfSevenDeaths_HasAllKeywordMarkers()
    {
        var sire = SireOfSevenDeathsFactory.Create(_alice);
        var keywords = sire.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();

        keywords.Should().Contain("First Strike", "CR 702.7 — First strike marker");
        keywords.Should().Contain("Vigilance", "CR 702.20 — Vigilance marker");
        keywords.Should().Contain("Menace", "CR 702.111 — Menace marker");
        keywords.Should().Contain("Trample", "CR 702.19 — Trample marker");
        keywords.Should().Contain("Reach", "CR 702.17 — Reach marker");
        keywords.Should().Contain("Lifelink", "CR 702.15 — Lifelink marker");
        keywords.Should().Contain("Ward",
            "CR 702.21 — Ward marker (printed 'Pay 7 life' rider deferred)");
    }

    [Fact]
    public void SireOfSevenDeaths_BuildWardEffect_ExposesManaZeroCost()
    {
        // Printed Ward cost is non-mana ("Pay 7 life") — the helper's mana
        // portion is zero; the life-payment rider is documentation-only
        // (see WardLifeCost) until the non-mana Ward primitive lands.
        // Same posture as Reality Smasher's "discard a card" Ward.
        var sire = SireOfSevenDeathsFactory.Create(_alice);
        var ward = SireOfSevenDeathsFactory.BuildWardEffect(sire);

        ward.Source.Should().BeSameAs(sire);
        ward.Cost.TotalValue.Should().Be(0,
            "printed cost is non-mana (Pay 7 life) — mana portion is zero");
        SireOfSevenDeathsFactory.WardLifeCost.Should().Be("Pay 7 life");
    }
}
