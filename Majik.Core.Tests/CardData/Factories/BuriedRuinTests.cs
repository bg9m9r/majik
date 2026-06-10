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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BuriedRuinFactory"/> — Scars of Mirrodin utility land:
///   "{T}: Add {C}.
///    {2}, {T}, Sacrifice this land: Return target artifact card from your
///    graveyard to your hand."
///
/// Gate-free sibling of <see cref="EncroachingWastesFactory"/> /
/// <see cref="TectonicEdgeFactory"/>; the delta from Encroaching Wastes is the
/// payload — grave-to-hand artifact recursion (the
/// <see cref="UnderworldCookbookFactory"/> primitive, restricted to artifacts)
/// in place of nonbasic-land destruction.
///
/// Covers:
/// - Card identity (Land, name) + <see cref="NamedCardFactory"/> dispatch.
/// - {T}: Add {C} mana ability (from JSON) taps the land and produces {C}.
/// - Activated ability shape: {2} mana cost + tap + single artifact-card
///   target request.
/// - Activated ability: target artifact card in graveyard → hand, self sac'd.
/// - Activated ability: noncreature/nonartifact target → return no-op
///   (CR 608.2b illegal target), self still sacrificed (the cost was paid).
/// </summary>
[Trait("Color", "Colorless")]
public class BuriedRuinTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BuriedRuin_IsNonbasicLand_NoSubtypes()
    {
        var land = BuriedRuinFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.Subtypes.Should().BeEmpty();
        land.Supertypes.Should().BeEmpty();
        land.Name.Should().Be("Buried Ruin");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BuriedRuin()
    {
        var card = NamedCardFactory.Create("Buried Ruin", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Buried Ruin");
        card.HasSupertype(CardSupertype.Basic).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void BuriedRuin_HasColorlessManaAbility_TapsAndProducesC()
    {
        var land = BuriedRuinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var manaAbility = land.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue();
        var produced = manaAbility.Activate();

        // {C} parses into the Generic slot today (no dedicated Colorless
        // property on ManaCost — mirrors Encroaching Wastes' tap-for-{C} test).
        produced.Generic.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Black.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {2}, {T}, Sacrifice this land: Return target artifact card from your
    // graveyard to your hand
    // -----------------------------------------------------------------------

    [Fact]
    public void BuriedRuin_HasReturnActivatedAbility_WithSingleTargetRequest()
    {
        var land = BuriedRuinFactory.Create(_alice);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        activated.TargetRequests.Should().HaveCount(1);
        activated.TargetRequests[0].MinTargets.Should().Be(1);
        activated.TargetRequests[0].MaxTargets.Should().Be(1);
        activated.TargetRequests[0].Description.Should().Contain("artifact card");
    }

    [Fact]
    public void BuriedRuin_Return_ArtifactCard_GoesToHand_SelfSacrificed()
    {
        var land = BuriedRuinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Artifact card in Alice's graveyard — the recur target.
        var relic = new Artifact("Mind Stone", "2");
        relic.SetOwner(_alice);
        relic.SetController(_alice);
        relic.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(relic);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        // {2} cost — top up Alice's mana pool, then pay all costs.
        _alice.AddManaToPool(ManaCost.Zero.AddGenericCost(2));
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { relic },
        });
        activated.Resolve();

        // Target artifact card returned to Alice's hand.
        _alice.Zones.Hand.GetCards().Should().Contain(relic);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(relic);
        relic.Zone.Should().Be(ZoneType.Hand);

        // Buried Ruin sacrificed itself — now in Alice's graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(land);
        land.Zone.Should().Be(ZoneType.Graveyard);

        // CR 400.7 / 614 — Buried Ruin sacrificed itself; the tap cost ran on
        // the battlefield, but in the graveyard it is a new object and is no
        // longer tapped.
        land.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void BuriedRuin_Return_NonArtifactTarget_IsNoOp_ButStillSacrifices()
    {
        // CR 608.2b — an illegal target makes the part of the effect that
        // involves the target do nothing. The cost (incl. self-sac) is still
        // paid, so Buried Ruin still goes to the graveyard; the noncreature
        // nonartifact card stays in the graveyard.
        var land = BuriedRuinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "R");
        bolt.SetOwner(_alice);
        bolt.SetController(_alice);
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        var activated = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        _alice.AddManaToPool(ManaCost.Zero.AddGenericCost(2));
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bolt },
        });
        activated.Resolve();

        // Nonartifact card stays in the graveyard.
        bolt.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Hand.GetCards().Should().NotContain(bolt);

        // Buried Ruin still sacrificed itself (the cost was paid).
        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        land.Zone.Should().Be(ZoneType.Graveyard);
    }
}
