using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Yavimaya, Cradle of Growth — Legendary Land,
/// "Each land is a Forest in addition to its other types." (CR 305.7).
/// Mirrors the Urborg shape with the granted subtype swapped to Forest.
/// </summary>
public class YavimayaCradleOfGrowthTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public YavimayaCradleOfGrowthTests()
    {
        _zones = new ZoneService(_bus);
    }

    [Fact]
    public void Yavimaya_IsLegendaryLand_NoPrintedMana()
    {
        var yavimaya = YavimayaCradleOfGrowthFactory.Create(_alice);

        yavimaya.Name.Should().Be("Yavimaya, Cradle of Growth");
        yavimaya.HasType(CardType.Land).Should().BeTrue();
        yavimaya.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        yavimaya.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        yavimaya.Abilities.OfType<IManaAbility>().Should().BeEmpty();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Yavimaya()
    {
        var yavimaya = NamedCardFactory.Create("Yavimaya, Cradle of Growth", _alice);

        yavimaya.Should().BeOfType<Land>();
        yavimaya.Name.Should().Be("Yavimaya, Cradle of Growth");
    }

    /// <summary>
    /// Basic Island under Yavimaya: effective mana = printed {U} + granted {G}.
    /// </summary>
    [Fact]
    public void Yavimaya_BasicIsland_TapsForBlueAndGreen()
    {
        var island = (Land)NamedCardFactory.Create("Island", _alice);
        _zones.MoveCard(island, ZoneType.Library, ZoneType.Battlefield, _alice);

        var yavimaya = YavimayaCradleOfGrowthFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(yavimaya, ZoneType.Library, ZoneType.Battlefield, _alice);

        var abilities = EffectiveManaAbilities.For(island, _effects, _alice);

        abilities.Should().HaveCount(2, "printed {U} preserved, {G} added for granted Forest");
        abilities.Should().Contain(a => a.ManaGenerated.Blue == 1);
        abilities.Should().Contain(a => a.ManaGenerated.Green == 1);
    }

    /// <summary>
    /// Yavimaya self-applies Forest → taps for {G}.
    /// </summary>
    [Fact]
    public void Yavimaya_SelfTapsForGreen()
    {
        var yavimaya = YavimayaCradleOfGrowthFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(yavimaya, ZoneType.Library, ZoneType.Battlefield, _alice);

        var abilities = EffectiveManaAbilities.For(yavimaya, _effects, _alice);

        abilities.Should().ContainSingle();
        abilities[0].ManaGenerated.Green.Should().Be(1);
    }
}
