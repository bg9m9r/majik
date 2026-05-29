using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="DemolitionFieldFactory"/> — a Field-of-Ruin
/// analogue. Nonbasic-land destruction utility land with a {2} fixed-mana
/// activated ability and a two-player "may tutor a basic" rider.
///
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {2}, {T}, Sacrifice this land: Destroy target nonbasic land an
///    opponent controls. That land's controller may search their library
///    for a basic land card, put it onto the battlefield, then shuffle.
///    You may search your library for a basic land card, put it onto the
///    battlefield, then shuffle."
///
/// Unlike Field of Ruin's "each player searches", Demolition Field only
/// lets exactly two players tutor: the destroyed land's controller, and
/// the activator ("you").
/// </summary>
public class DemolitionFieldTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void DemolitionField_IsLand_NoSubtypes()
    {
        var land = DemolitionFieldFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.Subtypes.Should().BeEmpty();
        land.Supertypes.Should().BeEmpty();
        land.Name.Should().Be("Demolition Field");
        land.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DemolitionField()
    {
        var card = NamedCardFactory.Create("Demolition Field", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Demolition Field");
    }

    [Fact]
    public void DemolitionField_HasManaAbility_AndSingleDestroyActivatedAbility()
    {
        var land = DemolitionFieldFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        activated.TargetRequests.Should().HaveCount(1);
        activated.TargetRequests[0].MinTargets.Should().Be(1);
        activated.TargetRequests[0].MaxTargets.Should().Be(1);
        activated.TargetRequests[0].Description.Should().Contain("nonbasic land");
        activated.TargetRequests[0].Description.Should().Contain("opponent");
    }

    [Fact]
    public void DemolitionField_Destroys_OpponentsNonbasicLand_AndSacrificesSelf()
    {
        var target = new Land(
            name: "Karakas",
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var field = DemolitionFieldFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(field);
        field.SetZone(ZoneType.Battlefield);

        var activated = field.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        // Demolition Field costs {2} — top up Alice's mana pool.
        _alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Zero.AddGenericCost(2));
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        activated.Resolve();

        target.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(target);
        field.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(field);
    }

    [Fact]
    public void DemolitionField_TargetingOwnLand_IsNoOp_OnDestroy_StillSacrifices()
    {
        // Demolition Field's destroy target is "an opponent controls" —
        // targeting your own land is illegal. CR 608.2b — the destroy half
        // does nothing; the cost was paid up front so it still sacrifices.
        var ownLand = new Land(
            name: "Karakas",
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        ownLand.SetOwner(_alice);
        ownLand.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ownLand);
        ownLand.SetZone(ZoneType.Battlefield);

        var field = DemolitionFieldFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(field);
        field.SetZone(ZoneType.Battlefield);

        var activated = field.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        _alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Zero.AddGenericCost(2));
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { ownLand },
        });
        activated.Resolve();

        ownLand.Zone.Should().Be(ZoneType.Battlefield);
        field.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void DemolitionField_TargetController_AndActivator_BothTutorBasic()
    {
        // Bob's nonbasic land is destroyed; Bob (target's controller) and
        // Alice (activator) each may tutor a basic. Both have basics in
        // their libraries — both should move to the battlefield.
        var alicePlains = new Land(
            name: "Plains",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Plains });
        alicePlains.SetOwner(_alice);
        _alice.Zones.Library.AddCard(alicePlains);
        alicePlains.SetZone(ZoneType.Library);

        var bobIsland = new Land(
            name: "Island",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Island });
        bobIsland.SetOwner(_bob);
        _bob.Zones.Library.AddCard(bobIsland);
        bobIsland.SetZone(ZoneType.Library);

        var target = new Land(
            name: "Wasteland",
            supertypes: null,
            subtypes: null);
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var field = DemolitionFieldFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(field);
        field.SetZone(ZoneType.Battlefield);

        var activated = field.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        _alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Zero.AddGenericCost(2));
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        activated.Resolve();

        // Both the target's controller (Bob) and the activator (Alice)
        // tutored their basic.
        alicePlains.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(alicePlains);
        _alice.Zones.Library.GetCards().Should().NotContain(alicePlains);

        bobIsland.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobIsland);
        _bob.Zones.Library.GetCards().Should().NotContain(bobIsland);

        target.Zone.Should().Be(ZoneType.Graveyard);
        field.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void DemolitionField_IllegalTarget_OnlyActivatorTutors()
    {
        // Targeting Alice's own land is illegal — destroy does nothing and
        // there is no "that land's controller" tutor (no land destroyed).
        // Alice (activator) still may tutor her basic.
        var alicePlains = new Land(
            name: "Plains",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Plains });
        alicePlains.SetOwner(_alice);
        _alice.Zones.Library.AddCard(alicePlains);
        alicePlains.SetZone(ZoneType.Library);

        var bobIsland = new Land(
            name: "Island",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Island });
        bobIsland.SetOwner(_bob);
        _bob.Zones.Library.AddCard(bobIsland);
        bobIsland.SetZone(ZoneType.Library);

        var ownLand = new Land(
            name: "Karakas",
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        ownLand.SetOwner(_alice);
        ownLand.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ownLand);
        ownLand.SetZone(ZoneType.Battlefield);

        var field = DemolitionFieldFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(field);
        field.SetZone(ZoneType.Battlefield);

        var activated = field.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        _alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Zero.AddGenericCost(2));
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { ownLand },
        });
        activated.Resolve();

        // Activator tutored.
        alicePlains.Zone.Should().Be(ZoneType.Battlefield);
        // No land destroyed → no "that land's controller" tutor. Bob's
        // basic stays in his library.
        bobIsland.Zone.Should().Be(ZoneType.Library);
        _bob.Zones.Library.GetCards().Should().Contain(bobIsland);

        ownLand.Zone.Should().Be(ZoneType.Battlefield);
        field.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void DemolitionField_PlayerWithoutBasic_StillResolvesNoCrash()
    {
        // Bob's land destroyed but neither player has a basic. Resolution
        // must not throw.
        var target = new Land(
            name: "Wasteland",
            supertypes: null,
            subtypes: null);
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var field = DemolitionFieldFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(field);
        field.SetZone(ZoneType.Battlefield);

        var activated = field.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        _alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Zero.AddGenericCost(2));
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        Action act = () => activated.Resolve();
        act.Should().NotThrow();

        target.Zone.Should().Be(ZoneType.Graveyard);
    }
}
