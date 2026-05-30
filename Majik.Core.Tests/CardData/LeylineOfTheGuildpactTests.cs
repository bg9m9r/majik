using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Leyline of the Guildpact — Enchantment
/// {G/W}{G/U}{B/G}{R/G}. Three clauses (CR 702.95 opening-hand alt-cost,
/// CR 305.7 / 613.1d "lands are every basic land type", CR 105.2c / 613.1e
/// "each nonland permanent you control is all colors").
/// </summary>
public class LeylineOfTheGuildpactTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public LeylineOfTheGuildpactTests()
    {
        _zones = new ZoneService(_bus);
    }

    // ---- Shape / identity ----

    [Fact]
    public void Leyline_IsEnchantment_WithFiveColorHybridCost()
    {
        var leyline = LeylineOfTheGuildpactFactory.Create(_alice);

        leyline.Name.Should().Be("Leyline of the Guildpact");
        leyline.HasType(CardType.Enchantment).Should().BeTrue();
        leyline.ManaCost.Should().Be("{G/W}{G/U}{B/G}{R/G}");

        // CR 202.2 — hybrid pips contribute both colours → all five.
        CardColors.GetColors(leyline).Should().BeEquivalentTo(new[]
        {
            ManaColor.White, ManaColor.Blue, ManaColor.Black,
            ManaColor.Red, ManaColor.Green,
        });
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Leyline()
    {
        var leyline = NamedCardFactory.Create("Leyline of the Guildpact", _alice);

        leyline.Should().BeOfType<Enchantment>();
        leyline.Name.Should().Be("Leyline of the Guildpact");
    }

    // ---- Clause (a): opening-hand Leyline alt-cost ----

    [Fact]
    public void Leyline_CarriesOpeningHandLeylineKeyword()
    {
        var leyline = LeylineOfTheGuildpactFactory.Create(_alice);

        leyline.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == OpeningHandLeylineAlternativeCost.LeylineKeyword)
            .Should().BeTrue();
    }

    // ---- Clause (b): lands are every basic land type ----

    [Fact]
    public void Leyline_GrantsEveryBasicLandType_ToControllersLands()
    {
        // A plain Wastes-less nonbasic the controller controls: use a
        // basic Island so we can also see the additive mana below.
        var island = (Land)NamedCardFactory.Create("Island", _alice);
        _zones.MoveCard(island, ZoneType.Library, ZoneType.Battlefield, _alice);

        var leyline = LeylineOfTheGuildpactFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(leyline, ZoneType.Library, ZoneType.Battlefield, _alice);

        var subtypes = _effects.Compute((Permanent)island).Subtypes;

        subtypes.Should().Contain(CardSubtype.Plains);
        subtypes.Should().Contain(CardSubtype.Island);
        subtypes.Should().Contain(CardSubtype.Swamp);
        subtypes.Should().Contain(CardSubtype.Mountain);
        subtypes.Should().Contain(CardSubtype.Forest);
    }

    [Fact]
    public void Leyline_AffectedLand_TapsForAllFiveColors()
    {
        var island = (Land)NamedCardFactory.Create("Island", _alice);
        _zones.MoveCard(island, ZoneType.Library, ZoneType.Battlefield, _alice);

        var leyline = LeylineOfTheGuildpactFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(leyline, ZoneType.Library, ZoneType.Battlefield, _alice);

        var abilities = EffectiveManaAbilities.For(island, _effects, _alice);

        abilities.Should().Contain(a => a.ManaGenerated.White == 1);
        abilities.Should().Contain(a => a.ManaGenerated.Blue == 1);
        abilities.Should().Contain(a => a.ManaGenerated.Black == 1);
        abilities.Should().Contain(a => a.ManaGenerated.Red == 1);
        abilities.Should().Contain(a => a.ManaGenerated.Green == 1);
    }

    [Fact]
    public void Leyline_DoesNotGrantLandTypes_ToOpponentsLands()
    {
        var bobIsland = (Land)NamedCardFactory.Create("Island", _bob);
        _zones.MoveCard(bobIsland, ZoneType.Library, ZoneType.Battlefield, _bob);

        var leyline = LeylineOfTheGuildpactFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(leyline, ZoneType.Library, ZoneType.Battlefield, _alice);

        var subtypes = _effects.Compute((Permanent)bobIsland).Subtypes;

        // "Lands you control" — Bob's land is unaffected.
        subtypes.Should().NotContain(CardSubtype.Mountain);
        subtypes.Should().Contain(CardSubtype.Island, "its printed subtype is untouched");
    }

    [Fact]
    public void LandGrants_EndWhenLeylineLeavesBattlefield()
    {
        var island = (Land)NamedCardFactory.Create("Island", _alice);
        _zones.MoveCard(island, ZoneType.Library, ZoneType.Battlefield, _alice);

        var leyline = LeylineOfTheGuildpactFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(leyline, ZoneType.Library, ZoneType.Battlefield, _alice);
        _effects.Compute((Permanent)island).Subtypes.Should().Contain(CardSubtype.Mountain);

        _zones.MoveCard(leyline, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        _effects.Compute((Permanent)island).Subtypes.Should().NotContain(CardSubtype.Mountain);
    }

    // ---- Clause (c): nonland permanents you control are all colors ----

    [Fact]
    public void Leyline_MakesControllersNonlandPermanent_AllColors()
    {
        var bear = (Creature)NamedCardFactory.Create("Grizzly Bears", _alice);
        bear.ActiveEffects = _effects;
        _zones.MoveCard(bear, ZoneType.Library, ZoneType.Battlefield, _alice);

        var leyline = LeylineOfTheGuildpactFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(leyline, ZoneType.Library, ZoneType.Battlefield, _alice);

        bear.GetEffectiveColors().Should().BeEquivalentTo(new[]
        {
            ManaColor.White, ManaColor.Blue, ManaColor.Black,
            ManaColor.Red, ManaColor.Green,
        });
    }

    [Fact]
    public void Leyline_DoesNotColor_OpponentsNonlandPermanents()
    {
        var bobBear = (Creature)NamedCardFactory.Create("Grizzly Bears", _bob);
        bobBear.ActiveEffects = _effects;
        _zones.MoveCard(bobBear, ZoneType.Library, ZoneType.Battlefield, _bob);

        var leyline = LeylineOfTheGuildpactFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(leyline, ZoneType.Library, ZoneType.Battlefield, _alice);

        // "nonland permanent you control" — Bob's bear keeps its green.
        bobBear.GetEffectiveColors().Should().BeEquivalentTo(new[] { ManaColor.Green });
    }

    [Fact]
    public void Leyline_DoesNotColor_ControllersLands()
    {
        var island = (Land)NamedCardFactory.Create("Island", _alice);
        island.ActiveEffects = _effects;
        _zones.MoveCard(island, ZoneType.Library, ZoneType.Battlefield, _alice);

        var leyline = LeylineOfTheGuildpactFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(leyline, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Lands are excluded from the all-colors clause (printed Island is
        // blue-identity but the basic land card itself is colourless).
        island.GetEffectiveColors().Should().BeEmpty();
    }

    [Fact]
    public void ColorEffect_EndsWhenLeylineLeavesBattlefield()
    {
        var bear = (Creature)NamedCardFactory.Create("Grizzly Bears", _alice);
        bear.ActiveEffects = _effects;
        _zones.MoveCard(bear, ZoneType.Library, ZoneType.Battlefield, _alice);

        var leyline = LeylineOfTheGuildpactFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(leyline, ZoneType.Library, ZoneType.Battlefield, _alice);
        bear.GetEffectiveColors().Should().HaveCount(5);

        _zones.MoveCard(leyline, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        // Reverts to printed green.
        bear.GetEffectiveColors().Should().BeEquivalentTo(new[] { ManaColor.Green });
    }
}
