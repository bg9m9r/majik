using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="FulminatorMageFactory"/> — Shadowmoor Creature
/// — Elemental Shaman {B/R}{B/R} 2/2 with the activated ability
/// "Sacrifice Fulminator Mage: Destroy target nonbasic land."
///
/// Covers:
/// - Card identity (2/2 Elemental Shaman, {B/R}{B/R}, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Activated ability shape: empty Costs, single 1..1 "target nonbasic
///   land" TargetRequest.
/// - Resolution: legal nonbasic-land target → graveyard + mage sac'd.
/// - Resolution: basic-land target → fizzles (CR 608.2b) but mage still
///   sacrifices itself (cost was paid on activation).
/// - Resolution: off-battlefield target → fizzles, mage still sacrificed.
/// </summary>
public class FulminatorMageTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FulminatorMage_Is22ElementalShaman_WithHybridManaCost()
    {
        var mage = FulminatorMageFactory.Create(_alice);

        mage.Name.Should().Be("Fulminator Mage");
        mage.ManaCost.Should().Be("{B/R}{B/R}");
        mage.Power.Should().Be(2);
        mage.Toughness.Should().Be(2);
        mage.HasType(CardType.Creature).Should().BeTrue();
        mage.Subtypes.Should().Contain(CardSubtype.Elemental);
        mage.Subtypes.Should().Contain(CardSubtype.Shaman);
        mage.Owner.Should().BeSameAs(_alice);
        mage.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FulminatorMage()
    {
        var card = NamedCardFactory.Create("Fulminator Mage", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Fulminator Mage");
        ((Creature)card).Power.Should().Be(2);
        ((Creature)card).Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void FulminatorMage_HasDestroyActivatedAbility_WithSingleNonbasicLandTarget()
    {
        var mage = FulminatorMageFactory.Create(_alice);

        var activated = mage.Abilities.OfType<ActivatedAbility>().Single();
        activated.Costs.Should().BeEmpty(
            because: "the only activation cost is sacrifice-self, which is " +
                     "performed inline by the effect closure (same pattern as " +
                     "Wasteland's sacrifice cost).");
        activated.TargetRequests.Should().HaveCount(1);
        activated.TargetRequests[0].MinTargets.Should().Be(1);
        activated.TargetRequests[0].MaxTargets.Should().Be(1);
        activated.TargetRequests[0].Description.Should().Contain("nonbasic land");
    }

    // -----------------------------------------------------------------------
    // Resolution paths
    // -----------------------------------------------------------------------

    [Fact]
    public void FulminatorMage_Destroy_NonbasicLand_TargetGoesToGraveyard_MageSacrificed()
    {
        // Bob controls a nonbasic land (Karakas). Alice sacrifices
        // Fulminator Mage to destroy it.
        var target = new Land(
            name: "Karakas",
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var mage = FulminatorMageFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mage);
        mage.SetZone(ZoneType.Battlefield);

        var activated = mage.Abilities.OfType<ActivatedAbility>().Single();

        // Pay structural costs (none); sacrifice happens inline at resolve.
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

        // Fulminator Mage sacrificed itself — now in Alice's graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(mage);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(mage);
        mage.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void FulminatorMage_Destroy_BasicLand_FizzlesButStillSacrifices()
    {
        // CR 608.2b — illegal target makes the part of the effect that
        // involves the target do nothing. The sacrifice cost is paid on
        // activation (modeled inline at resolution here), so Fulminator
        // Mage still goes to the graveyard while the basic Mountain stays.
        var basicLand = new Land(
            name: "Mountain",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Mountain });
        basicLand.SetOwner(_bob);
        basicLand.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(basicLand);
        basicLand.SetZone(ZoneType.Battlefield);

        var mage = FulminatorMageFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mage);
        mage.SetZone(ZoneType.Battlefield);

        var activated = mage.Abilities.OfType<ActivatedAbility>().Single();
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

        // Fulminator Mage still sacrificed itself.
        _alice.Zones.Graveyard.GetCards().Should().Contain(mage);
        mage.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void FulminatorMage_Destroy_OffBattlefieldTarget_FizzlesButStillSacrifices()
    {
        // CR 608.2b — if the chosen target has left the battlefield
        // between activation and resolution, the destroy half does
        // nothing. The mage still sacrifices.
        var target = new Land(
            name: "Karakas",
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        target.SetOwner(_bob);
        target.SetController(_bob);
        // Note: NOT on the battlefield — sits in Bob's graveyard to
        // simulate a target that left between activation and resolution.
        _bob.Zones.Graveyard.AddCard(target);
        target.SetZone(ZoneType.Graveyard);

        var mage = FulminatorMageFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mage);
        mage.SetZone(ZoneType.Battlefield);

        var activated = mage.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        activated.Resolve();

        // Target stays in the graveyard (was already there); no double-move.
        _bob.Zones.Graveyard.GetCards().Should().Contain(target);
        target.Zone.Should().Be(ZoneType.Graveyard);

        // Fulminator Mage still sacrificed itself.
        _alice.Zones.Graveyard.GetCards().Should().Contain(mage);
        mage.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void FulminatorMage_Destroy_OwnNonbasicLand_Works()
    {
        // Fulminator Mage's destroy target isn't ownership-restricted —
        // Alice can target her own nonbasic land (niche but legal).
        var ownLand = new Land(
            name: "Karakas",
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        ownLand.SetOwner(_alice);
        ownLand.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ownLand);
        ownLand.SetZone(ZoneType.Battlefield);

        var mage = FulminatorMageFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mage);
        mage.SetZone(ZoneType.Battlefield);

        var activated = mage.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { ownLand },
        });
        activated.Resolve();

        _alice.Zones.Graveyard.GetCards().Should().Contain(ownLand);
        ownLand.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(mage);
        mage.Zone.Should().Be(ZoneType.Graveyard);
    }
}
