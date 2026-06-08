using FluentAssertions;
using Majik.Core.CardData;
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
/// Tests the PRODUCTION wiring of the additive land-retype static (CR 305.7)
/// via <see cref="AdditiveLandSubtypeBinder"/> — the binder-chain entry point
/// that <c>GameFacade.BindCardAbilities</c> calls for Land cards.
///
/// <para>The per-card <c>UrborgTombOfYawgmothFactory</c> /
/// <c>YavimayaCradleOfGrowthFactory</c> only wire the
/// <see cref="GrantLandSubtypeStaticEffect"/> on a test-only overload; lands
/// are never routed through their factory in prod (see
/// <see cref="FactoryRouting"/>), so without this binder the additive grant was
/// silently dropped at the live table. These tests exercise the binder against
/// the REAL oracle text from <see cref="EmbeddedCardRepository"/>, exactly as
/// the production binder pipeline does.</para>
/// </summary>
public class AdditiveLandSubtypeBinderTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects;
    private readonly ZoneService _zones;
    private readonly EmbeddedCardRepository _repo = new();

    public AdditiveLandSubtypeBinderTests()
    {
        _zones = new ZoneService(_bus);
        _effects = new ContinuousEffectsService { PlayersProvider = () => new[] { _alice } };
    }

    /// <summary>Build a bare Land shell from the repo entity and bind the
    /// additive static the way the production pipeline does.</summary>
    private Land BuildAndBind(string name)
    {
        var entity = _repo.GetByName(name);
        entity.Should().NotBeNull($"{name} should exist in the embedded card pool");

        var land = new Land(name,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        land.SetOwner(_alice);
        land.SetController(_alice);

        var bound = AdditiveLandSubtypeBinder.Bind(land, entity!, _effects, _bus);
        bound.Should().BeTrue($"{name}'s oracle text is an 'Each land is a [basic]' grant");
        return land;
    }

    // -----------------------------------------------------------------------
    // Urborg — "Each land is a Swamp in addition to its other land types."
    // -----------------------------------------------------------------------

    [Fact]
    public void Urborg_BoundViaBinder_GrantsSwampAndBlackManaToEveryLand()
    {
        // A basic Mountain already on the battlefield.
        var mountain = new Land("Mountain",
            new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain });
        OracleManaBinderBasic(mountain);
        _zones.MoveCard(mountain, ZoneType.Library, ZoneType.Battlefield, _alice);

        var urborg = BuildAndBind("Urborg, Tomb of Yawgmoth");
        _zones.MoveCard(urborg, ZoneType.Library, ZoneType.Battlefield, _alice);

        var subtypes = _effects.Compute(mountain).Subtypes;
        subtypes.Should().Contain(CardSubtype.Mountain, "printed subtype preserved (additive)");
        subtypes.Should().Contain(CardSubtype.Swamp, "Urborg grants Swamp to every land");

        var abilities = EffectiveManaAbilities.For(mountain, _effects, _alice);
        abilities.Should().Contain(a => a.ManaGenerated.Red == 1, "printed {R} preserved");
        abilities.Should().Contain(a => a.ManaGenerated.Black == 1, "granted Swamp taps for {B} (CR 305.6)");
    }

    [Fact]
    public void Urborg_BoundViaBinder_SelfTapsForBlack()
    {
        var urborg = BuildAndBind("Urborg, Tomb of Yawgmoth");
        _zones.MoveCard(urborg, ZoneType.Library, ZoneType.Battlefield, _alice);

        var abilities = EffectiveManaAbilities.For(urborg, _effects, _alice);
        abilities.Should().ContainSingle("Urborg self-applies Swamp → one synthesized {B} ability");
        abilities[0].ManaGenerated.Black.Should().Be(1);
    }

    [Fact]
    public void Urborg_RemovedFromBattlefield_RevertsGrant()
    {
        var mountain = new Land("Mountain",
            new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain });
        _zones.MoveCard(mountain, ZoneType.Library, ZoneType.Battlefield, _alice);

        var urborg = BuildAndBind("Urborg, Tomb of Yawgmoth");
        _zones.MoveCard(urborg, ZoneType.Library, ZoneType.Battlefield, _alice);

        _effects.Compute(mountain).Subtypes.Should().Contain(CardSubtype.Swamp);

        // Urborg leaves → grant reverts (lifecycle unregisters on CardMovedEvent).
        _zones.MoveCard(urborg, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        _effects.Compute(mountain).Subtypes.Should().NotContain(CardSubtype.Swamp,
            "removing Urborg reverts the additive grant");
        _effects.Compute(mountain).Subtypes.Should().Contain(CardSubtype.Mountain,
            "the land keeps its own printed subtype");
    }

    [Fact]
    public void Urborg_LandAlreadySwamp_IsUnaffected_NoDuplicateManaAbility()
    {
        // A basic Swamp already on the battlefield — additive grant re-adds an
        // already-present subtype, so nothing changes and no {B} is doubled.
        var swamp = new Land("Swamp",
            new[] { CardSupertype.Basic }, new[] { CardSubtype.Swamp });
        OracleManaBinderBasic(swamp);
        _zones.MoveCard(swamp, ZoneType.Library, ZoneType.Battlefield, _alice);

        var urborg = BuildAndBind("Urborg, Tomb of Yawgmoth");
        _zones.MoveCard(urborg, ZoneType.Library, ZoneType.Battlefield, _alice);

        _effects.Compute(swamp).Subtypes.Should().Contain(CardSubtype.Swamp);
        EffectiveManaAbilities.For(swamp, _effects, _alice)
            .Should().ContainSingle("a printed Swamp taps for {B} once — additive grant adds no duplicate")
            .Which.ManaGenerated.Black.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Yavimaya — "Each land is a Forest in addition to its other land types."
    // -----------------------------------------------------------------------

    [Fact]
    public void Yavimaya_BoundViaBinder_GrantsForestAndGreenManaToEveryLand()
    {
        var mountain = new Land("Mountain",
            new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain });
        OracleManaBinderBasic(mountain);
        _zones.MoveCard(mountain, ZoneType.Library, ZoneType.Battlefield, _alice);

        var yavimaya = BuildAndBind("Yavimaya, Cradle of Growth");
        _zones.MoveCard(yavimaya, ZoneType.Library, ZoneType.Battlefield, _alice);

        var subtypes = _effects.Compute(mountain).Subtypes;
        subtypes.Should().Contain(CardSubtype.Mountain, "printed subtype preserved (additive)");
        subtypes.Should().Contain(CardSubtype.Forest, "Yavimaya grants Forest to every land");

        var abilities = EffectiveManaAbilities.For(mountain, _effects, _alice);
        abilities.Should().Contain(a => a.ManaGenerated.Green == 1, "granted Forest taps for {G} (CR 305.6)");
    }

    [Fact]
    public void Yavimaya_BoundViaBinder_SelfTapsForGreen()
    {
        var yavimaya = BuildAndBind("Yavimaya, Cradle of Growth");
        _zones.MoveCard(yavimaya, ZoneType.Library, ZoneType.Battlefield, _alice);

        var abilities = EffectiveManaAbilities.For(yavimaya, _effects, _alice);
        abilities.Should().ContainSingle("Yavimaya self-applies Forest → one synthesized {G} ability");
        abilities[0].ManaGenerated.Green.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Negatives
    // -----------------------------------------------------------------------

    [Fact]
    public void Bind_NonGrantLand_ReturnsFalse()
    {
        var entity = _repo.GetByName("Forest");
        entity.Should().NotBeNull();

        var forest = new Land("Forest",
            new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);

        AdditiveLandSubtypeBinder.Bind(forest, entity!, _effects, _bus)
            .Should().BeFalse("a basic Forest is not an 'Each land is a [basic]' grant");
    }

    /// <summary>Give a basic land its printed tap-for-color mana ability so
    /// the additive-vs-replacement detection has a printed ability to merge
    /// with (mirrors what OracleManaBinder does in prod for the basic).</summary>
    private void OracleManaBinderBasic(Land land)
    {
        land.SetOwner(_alice);
        land.SetController(_alice);
        OracleManaBinder.BindBasicLandMana(land, _alice);
    }
}
