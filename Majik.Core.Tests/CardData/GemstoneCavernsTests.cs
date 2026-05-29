using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="GemstoneCavernsFactory"/> (Coldsnap).
/// Legendary Land. Oracle text (verified against Scryfall 2026-05-29):
///   "If this card is in your opening hand and you're not the starting
///    player, you may begin the game with Gemstone Caverns on the
///    battlefield with a luck counter on it. If you do, exile a card from
///    your hand.
///    {T}: Add {C}. If Gemstone Caverns has a luck counter on it, instead
///    add one mana of any color."
///
/// Covers:
/// - Identity (Legendary Land, name, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - {T}: Add {C} — the unconditional colorless mana ability, active only
///   while there is NO luck counter on the land (CR 605.1).
/// - With a luck counter present: the colorless ability is suppressed and
///   five WUBRG "any color" mana abilities become active instead
///   ("instead add one mana of any color").
/// - The opening-hand luck-counter start clause is carried as a marker
///   keyword (deferred — see factory xmldoc).
/// </summary>
public class GemstoneCavernsTests
{
    private readonly Player _alice = new("Alice", 20);

    private static ManaAbility ColorAbility(Land land, string colorSymbol) =>
        land.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.ToString() == ManaCost.Parse(colorSymbol).ToString());

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GemstoneCaverns_Identity()
    {
        var land = GemstoneCavernsFactory.Create(_alice);

        land.Name.Should().Be("Gemstone Caverns");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Gemstone Caverns is a Legendary Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GemstoneCaverns_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Gemstone Caverns", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Gemstone Caverns");
        card.HasType(CardType.Land).Should().BeTrue();

        // One {C} ability + five WUBRG "any color" abilities = six total.
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(6,
            "{T}: Add {C} plus the five-colour 'instead add one mana of any color' set");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C} — active when NO luck counter
    // -----------------------------------------------------------------------

    [Fact]
    public void GemstoneCaverns_NoLuckCounter_ColorlessAbilityActive_ColorAbilitiesInactive()
    {
        var land = GemstoneCavernsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var colorless = ColorAbility(land, "C");
        colorless.CanActivate().Should().BeTrue(
            "without a luck counter the land taps for {C}");

        var white = ColorAbility(land, "W");
        white.CanActivate().Should().BeFalse(
            "the 'any color' set only activates while a luck counter is present");
    }

    [Fact]
    public void GemstoneCaverns_NoLuckCounter_TapsForColorless()
    {
        var land = GemstoneCavernsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var colorless = ColorAbility(land, "C");
        var produced = colorless.Activate();

        produced.ToString().Should().Be(ManaCost.Parse("C").ToString(),
            "no luck counter → adds {C}");
        land.IsTapped.Should().BeTrue("{T} is the activation cost");
    }

    // -----------------------------------------------------------------------
    // With a luck counter — "instead add one mana of any color"
    // -----------------------------------------------------------------------

    [Fact]
    public void GemstoneCaverns_WithLuckCounter_ColorlessSuppressed_AnyColorActive()
    {
        var land = GemstoneCavernsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.Counters.Add(CounterType.Luck, 1);

        var colorless = ColorAbility(land, "C");
        colorless.CanActivate().Should().BeFalse(
            "with a luck counter the {C} ability is replaced ('instead add one mana of any color')");

        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            ColorAbility(land, color).CanActivate().Should().BeTrue(
                $"with a luck counter the land can tap for {color}");
        }
    }

    [Fact]
    public void GemstoneCaverns_WithLuckCounter_TapsForAnyColor()
    {
        var land = GemstoneCavernsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.Counters.Add(CounterType.Luck, 1);

        var green = ColorAbility(land, "G");
        var produced = green.Activate();

        produced.ToString().Should().Be(ManaCost.Parse("G").ToString(),
            "luck counter present → adds one mana of any color");
        land.IsTapped.Should().BeTrue("{T} is the activation cost");
    }

    // -----------------------------------------------------------------------
    // Opening-hand luck-counter start clause (deferred → marker keyword)
    // -----------------------------------------------------------------------

    [Fact]
    public void GemstoneCaverns_CarriesOpeningHandStartMarker()
    {
        var land = GemstoneCavernsFactory.Create(_alice);

        land.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == GemstoneCavernsFactory.OpeningHandStartKeyword)
            .Should().BeTrue(
                "the opening-hand luck-counter start clause is flagged for the deferred subscriber");
    }
}
