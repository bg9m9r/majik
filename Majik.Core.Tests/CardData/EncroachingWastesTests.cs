using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="EncroachingWastesFactory"/> — Theros utility land:
/// {T}: Add {C} and {4}, {T}, Sacrifice this land: destroy target nonbasic
/// land. Gate-free sibling of <see cref="WastelandFactory"/> /
/// <see cref="TectonicEdgeFactory"/>; the only delta from Wasteland is the
/// {4} generic mana on the activation cost (CR 602.1).
///
/// Covers:
/// - Card identity (Land, name) + <see cref="NamedCardFactory"/> dispatch.
/// - {T}: Add {C} mana ability (from JSON) taps the land and produces {C}.
/// - Activated ability shape: {4} mana cost + tap + single nonbasic-land
///   target request.
/// - Activated ability: target nonbasic land → graveyard + self sac'd.
/// - Activated ability: target basic land → no-op (CR 608.2b illegal target),
///   self still sacrificed (the cost was paid).
/// </summary>
public class EncroachingWastesTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void EncroachingWastes_IsNonbasicLand_NoSubtypes()
    {
        var land = EncroachingWastesFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.Subtypes.Should().BeEmpty();
        land.Supertypes.Should().BeEmpty();
        land.Name.Should().Be("Encroaching Wastes");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_EncroachingWastes()
    {
        var card = NamedCardFactory.Create("Encroaching Wastes", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Encroaching Wastes");
        card.HasSupertype(CardSupertype.Basic).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void EncroachingWastes_HasColorlessManaAbility_TapsAndProducesC()
    {
        var land = EncroachingWastesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var manaAbility = land.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue();
        var produced = manaAbility.Activate();

        // {C} parses into the Generic slot today (no dedicated Colorless
        // property on ManaCost — mirrors Wasteland's tap-for-{C} test).
        produced.Generic.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Black.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {4}, {T}, Sacrifice this land: Destroy target nonbasic land
    // -----------------------------------------------------------------------

    [Fact]
    public void EncroachingWastes_HasDestroyActivatedAbility_WithSingleTargetRequest()
    {
        var land = EncroachingWastesFactory.Create(_alice);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        activated.TargetRequests.Should().HaveCount(1);
        activated.TargetRequests[0].MinTargets.Should().Be(1);
        activated.TargetRequests[0].MaxTargets.Should().Be(1);
        activated.TargetRequests[0].Description.Should().Contain("nonbasic land");
    }

    [Fact]
    public void EncroachingWastes_Destroy_NonbasicLand_TargetGoesToGraveyard_SelfSacrificed()
    {
        var target = new Land(
            name: "Karakas",
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var wastes = EncroachingWastesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(wastes);
        wastes.SetZone(ZoneType.Battlefield);

        var activated = wastes.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        // {4} cost — top up Alice's mana pool, then pay all costs.
        _alice.AddManaToPool(ManaCost.Zero.AddGenericCost(4));
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        activated.Resolve();

        // Target nonbasic land is in Bob's graveyard.
        _bob.Zones.Graveyard.GetCards().Should().Contain(target);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(target);
        target.Zone.Should().Be(ZoneType.Graveyard);

        // Encroaching Wastes sacrificed itself — now in Alice's graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(wastes);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(wastes);
        wastes.Zone.Should().Be(ZoneType.Graveyard);

        // CR 400.7 / 614 — Encroaching Wastes sacrificed itself; the tap cost
        // ran on the battlefield, but in the graveyard it is a new object and
        // is no longer tapped.
        wastes.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void EncroachingWastes_Destroy_BasicLand_IsNoOp_ButStillSacrifices()
    {
        // CR 608.2b — an illegal target makes the part of the effect that
        // involves the target do nothing. The cost (incl. self-sac) is still
        // paid, so Encroaching Wastes still goes to the graveyard; the basic
        // Mountain stays put.
        var basicLand = new Land(
            name: "Mountain",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Mountain });
        basicLand.SetOwner(_bob);
        basicLand.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(basicLand);
        basicLand.SetZone(ZoneType.Battlefield);

        var wastes = EncroachingWastesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(wastes);
        wastes.SetZone(ZoneType.Battlefield);

        var activated = wastes.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        _alice.AddManaToPool(ManaCost.Zero.AddGenericCost(4));
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { basicLand },
        });
        activated.Resolve();

        // Basic land stays on the battlefield.
        _bob.Zones.Battlefield.GetCards().Should().Contain(basicLand);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(basicLand);
        basicLand.Zone.Should().Be(ZoneType.Battlefield);

        // Encroaching Wastes still sacrificed itself (the cost was paid).
        _alice.Zones.Graveyard.GetCards().Should().Contain(wastes);
        wastes.Zone.Should().Be(ZoneType.Graveyard);
    }
}
