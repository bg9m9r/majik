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
/// Tests for <see cref="FieldOfRuinFactory"/> — nonbasic land destruction
/// utility land with a fixed-mana cost and the "each player tutors a
/// basic" rider.
/// </summary>
public class FieldOfRuinTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void FieldOfRuin_IsLand_NoSubtypes()
    {
        var land = FieldOfRuinFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.Subtypes.Should().BeEmpty();
        land.Supertypes.Should().BeEmpty();
        land.Name.Should().Be("Field of Ruin");
        land.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FieldOfRuin()
    {
        var card = NamedCardFactory.Create("Field of Ruin", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Field of Ruin");
    }

    [Fact]
    public void FieldOfRuin_HasManaAbility_AndSingleDestroyActivatedAbility()
    {
        var land = FieldOfRuinFactory.Create(_alice);

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
    public void FieldOfRuin_Destroys_OpponentsNonbasicLand_AndSacrificesSelf()
    {
        // Setup: Bob controls a nonbasic land. Alice activates Field of
        // Ruin targeting it.
        var target = new Land(
            name: "Karakas",
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var fieldOfRuin = FieldOfRuinFactory.Create(_alice,
            allPlayersResolver: () => new[] { _alice, _bob });
        _alice.Zones.Battlefield.AddCard(fieldOfRuin);
        fieldOfRuin.SetZone(ZoneType.Battlefield);

        var activated = fieldOfRuin.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        // Field of Ruin costs {1} — top up Alice's mana pool so the cost
        // can be paid.
        _alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Zero.AddGenericCost(1));
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        activated.Resolve();

        target.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(target);
        fieldOfRuin.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(fieldOfRuin);
    }

    [Fact]
    public void FieldOfRuin_TargetingOwnLand_IsNoOp_OnDestroy_StillSacrifices()
    {
        // Field of Ruin's destroy target is "an opponent controls" —
        // targeting your own land is an illegal target. CR 608.2b — the
        // destroy half does nothing; the cost (sacrifice + tap + {1})
        // was paid up front so Field of Ruin still goes to graveyard.
        var ownLand = new Land(
            name: "Karakas",
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        ownLand.SetOwner(_alice);
        ownLand.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ownLand);
        ownLand.SetZone(ZoneType.Battlefield);

        var fieldOfRuin = FieldOfRuinFactory.Create(_alice,
            allPlayersResolver: () => new[] { _alice, _bob });
        _alice.Zones.Battlefield.AddCard(fieldOfRuin);
        fieldOfRuin.SetZone(ZoneType.Battlefield);

        var activated = fieldOfRuin.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        // Field of Ruin costs {1} — top up Alice's mana pool so the cost
        // can be paid.
        _alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Zero.AddGenericCost(1));
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { ownLand },
        });
        activated.Resolve();

        // Own land stays.
        ownLand.Zone.Should().Be(ZoneType.Battlefield);
        // Field of Ruin still sacrificed.
        fieldOfRuin.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void FieldOfRuin_EachPlayerTutorsBasic_FromTheirLibrary()
    {
        // Both Alice and Bob put one basic land into their library.
        // Activating Field of Ruin should move each player's basic onto
        // the battlefield (CR 701.19a tutor + CR 701.20a shuffle).
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

        // Bob also has a nonbasic land — Alice's destroy target.
        var target = new Land(
            name: "Wasteland",
            supertypes: null,
            subtypes: null);
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var fieldOfRuin = FieldOfRuinFactory.Create(_alice,
            allPlayersResolver: () => new[] { _alice, _bob });
        _alice.Zones.Battlefield.AddCard(fieldOfRuin);
        fieldOfRuin.SetZone(ZoneType.Battlefield);

        var activated = fieldOfRuin.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        // Field of Ruin costs {1} — top up Alice's mana pool so the cost
        // can be paid.
        _alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Zero.AddGenericCost(1));
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        activated.Resolve();

        // Both players' basics moved to the battlefield.
        alicePlains.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(alicePlains);
        _alice.Zones.Library.GetCards().Should().NotContain(alicePlains);

        bobIsland.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobIsland);
        _bob.Zones.Library.GetCards().Should().NotContain(bobIsland);

        // Destroy + sacrifice halves still hold.
        target.Zone.Should().Be(ZoneType.Graveyard);
        fieldOfRuin.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void FieldOfRuin_PlayerWithoutBasic_StillShufflesNoCrash()
    {
        // Alice has no basics; Bob has one. Resolution should not throw
        // and Bob's basic should be tutored.
        var bobMountain = new Land(
            name: "Mountain",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Mountain });
        bobMountain.SetOwner(_bob);
        _bob.Zones.Library.AddCard(bobMountain);
        bobMountain.SetZone(ZoneType.Library);

        var target = new Land(
            name: "Wasteland",
            supertypes: null,
            subtypes: null);
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var fieldOfRuin = FieldOfRuinFactory.Create(_alice,
            allPlayersResolver: () => new[] { _alice, _bob });
        _alice.Zones.Battlefield.AddCard(fieldOfRuin);
        fieldOfRuin.SetZone(ZoneType.Battlefield);

        var activated = fieldOfRuin.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        // Field of Ruin costs {1} — top up Alice's mana pool so the cost
        // can be paid.
        _alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Zero.AddGenericCost(1));
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        Action act = () => activated.Resolve();
        act.Should().NotThrow();

        bobMountain.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobMountain);
    }
}
