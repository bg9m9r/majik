using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for the Demolition Field both-players search-for-basic rider in
/// <see cref="LandActivatedAbilityBinder"/> (the PROD path for lands — the
/// <c>[CardName]</c> factory is never routed for lands; only the binder chain
/// runs on the live table).
///
/// <para>Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {2}, {T}, Sacrifice this land: Destroy target nonbasic land an opponent
///    controls. That land's controller may search their library for a basic
///    land card, put it onto the battlefield, then shuffle. You may search
///    your library for a basic land card, put it onto the battlefield, then
///    shuffle."</para>
///
/// <para>Before this seam shipped, <see cref="LandActivatedAbilityBinder"/>'s
/// destroy-target-land path bound only the destroy half and dropped the
/// two-player basic-land tutor rider (v1-deferrals
/// <c>demolition-field-search-rider</c>). These tests pin the rider: the
/// destroyed land's controller AND the activator each may tutor a basic
/// (UNtapped — Demolition Field's basics enter untapped, unlike the Panorama
/// cycle's tapped fetch).</para>
/// </summary>
public class DemolitionFieldBinderTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly ContinuousEffectsService _effects = new();

    private const string DemolitionFieldOracle =
        "{T}: Add {C}.\n{2}, {T}, Sacrifice this land: Destroy target nonbasic land an opponent controls. " +
        "That land's controller may search their library for a basic land card, put it onto the battlefield, then shuffle. " +
        "You may search your library for a basic land card, put it onto the battlefield, then shuffle.";

    private static CardEntity Entity(string name, string oracle, string typeLine = "Land")
        => new() { Name = name, TypeLine = typeLine, OracleText = oracle };

    private static Land Basic(Player owner, string name, CardSubtype subtype)
    {
        var land = new Land(name,
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { subtype });
        land.SetOwner(owner);
        owner.Zones.Library.AddCard(land);
        land.SetZone(ZoneType.Library);
        return land;
    }

    private ActivatedAbility BindAndGetDestroyAbility(Land field)
    {
        var bound = LandActivatedAbilityBinder.Bind(
            field, Entity("Demolition Field", DemolitionFieldOracle), _alice, _effects);
        bound.Should().BeTrue();
        return field.Abilities
            .OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1
                      && a.TargetRequests[0].Description.Contains("land"));
    }

    [Fact]
    public void Bind_DemolitionField_BindsDestroyAbilityWithTarget()
    {
        var field = new Land("Demolition Field") { Owner = _alice, Controller = _alice };

        var ability = BindAndGetDestroyAbility(field);

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void Resolve_DestroysTarget_AndBothPlayersTutorBasic()
    {
        var alicePlains = Basic(_alice, "Plains", CardSubtype.Plains);
        var bobIsland = Basic(_bob, "Island", CardSubtype.Island);

        var target = new Land("Wasteland");
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var field = new Land("Demolition Field") { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(field);
        field.SetZone(ZoneType.Battlefield);

        var ability = BindAndGetDestroyAbility(field);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        ability.Resolve();

        // Destroy half.
        target.Zone.Should().Be(ZoneType.Graveyard);

        // Both the destroyed land's controller (Bob) and the activator (Alice)
        // tutored a basic onto the battlefield, UNtapped (CR — Demolition Field
        // does NOT print "tapped").
        alicePlains.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(alicePlains);
        alicePlains.IsTapped.Should().BeFalse();

        bobIsland.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobIsland);
        bobIsland.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void Resolve_IllegalTarget_OnlyActivatorTutors()
    {
        // Targeting the activator's OWN land is illegal ("an opponent
        // controls"). The destroy half does nothing → there is no "that land's
        // controller" tutor. The activator still may tutor a basic.
        var alicePlains = Basic(_alice, "Plains", CardSubtype.Plains);
        var bobIsland = Basic(_bob, "Island", CardSubtype.Island);

        var ownLand = new Land("Karakas",
            supertypes: new[] { CardSupertype.Legendary }, subtypes: null);
        ownLand.SetOwner(_alice);
        ownLand.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ownLand);
        ownLand.SetZone(ZoneType.Battlefield);

        var field = new Land("Demolition Field") { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(field);
        field.SetZone(ZoneType.Battlefield);

        var ability = BindAndGetDestroyAbility(field);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ownLand } });
        ability.Resolve();

        ownLand.Zone.Should().Be(ZoneType.Battlefield);
        alicePlains.Zone.Should().Be(ZoneType.Battlefield);
        bobIsland.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void Resolve_NoBasicInLibrary_DoesNotThrow()
    {
        var target = new Land("Wasteland");
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var field = new Land("Demolition Field") { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(field);
        field.SetZone(ZoneType.Battlefield);

        var ability = BindAndGetDestroyAbility(field);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        Action act = () => ability.Resolve();
        act.Should().NotThrow();
        target.Zone.Should().Be(ZoneType.Graveyard);
    }
}
