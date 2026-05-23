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
/// Tests for <see cref="WastelandFactory"/> — Land with
/// {T}: Add {C} and {T}, Sacrifice Wasteland: destroy target nonbasic land.
///
/// Covers:
/// - Card identity (Land, name) + <see cref="NamedCardFactory"/> dispatch.
/// - {T}: Add {C} mana ability taps the land and produces colorless.
/// - Activated ability: target nonbasic land → graveyard + Wasteland sac'd.
/// - Activated ability: target basic land → no-op (CR 608.2b illegal target).
/// </summary>
public class WastelandTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Wasteland_IsNonbasicLand()
    {
        var land = WastelandFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.Name.Should().Be("Wasteland");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Wasteland()
    {
        var card = NamedCardFactory.Create("Wasteland", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Wasteland");
        card.HasSupertype(CardSupertype.Basic).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void Wasteland_HasColorlessManaAbility_AndActivationTapsLandAndProducesC()
    {
        var land = WastelandFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var manaAbility = land.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue();
        var produced = manaAbility.Activate();

        // {C} parses into the Generic slot today (no dedicated Colorless
        // property on ManaCost — mirrors Phyrexian Tower's tap-for-{C} test).
        produced.Generic.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Black.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {T}, Sacrifice Wasteland: Destroy target nonbasic land
    // -----------------------------------------------------------------------

    [Fact]
    public void Wasteland_HasDestroyActivatedAbility_WithSingleTargetRequest()
    {
        var land = WastelandFactory.Create(_alice);

        var activated = land.Abilities.OfType<ActivatedAbility>().Single();
        activated.TargetRequests.Should().HaveCount(1);
        activated.TargetRequests[0].MinTargets.Should().Be(1);
        activated.TargetRequests[0].MaxTargets.Should().Be(1);
        activated.TargetRequests[0].Description.Should().Contain("nonbasic land");
    }

    [Fact]
    public void Wasteland_Destroy_NonbasicLand_TargetGoesToGraveyard_WastelandSacrificed()
    {
        // Bob controls a nonbasic land (Karakas). Alice taps + sacs
        // Wasteland to destroy it.
        var target = new Land(
            name: "Karakas",
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var wasteland = WastelandFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(wasteland);
        wasteland.SetZone(ZoneType.Battlefield);

        var activated = wasteland.Abilities.OfType<ActivatedAbility>().Single();

        // Pay the tap cost (sacrifice happens inline at resolution).
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

        // Wasteland sacrificed itself — now in Alice's graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(wasteland);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(wasteland);
        wasteland.Zone.Should().Be(ZoneType.Graveyard);

        // The tap cost ran before resolution.
        wasteland.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Wasteland_Destroy_BasicLand_IsNoOp_ButStillSacrifices()
    {
        // CR 608.2b — an illegal target makes the part of the effect that
        // involves the target do nothing. The sacrifice cost is still paid
        // (it's a cost, not the target-dependent effect), so Wasteland
        // still goes to the graveyard. The basic Mountain stays put.
        var basicLand = new Land(
            name: "Mountain",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Mountain });
        basicLand.SetOwner(_bob);
        basicLand.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(basicLand);
        basicLand.SetZone(ZoneType.Battlefield);

        var wasteland = WastelandFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(wasteland);
        wasteland.SetZone(ZoneType.Battlefield);

        var activated = wasteland.Abilities.OfType<ActivatedAbility>().Single();
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

        // Wasteland still sacrificed itself (the cost was paid before
        // resolution checked target legality).
        _alice.Zones.Graveyard.GetCards().Should().Contain(wasteland);
        wasteland.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Wasteland_Destroy_OwnNonbasicLand_Works()
    {
        // Wasteland's destroy target isn't ownership-restricted — Alice
        // can target her own nonbasic land (niche but legal).
        var ownLand = new Land(
            name: "Karakas",
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        ownLand.SetOwner(_alice);
        ownLand.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ownLand);
        ownLand.SetZone(ZoneType.Battlefield);

        var wasteland = WastelandFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(wasteland);
        wasteland.SetZone(ZoneType.Battlefield);

        var activated = wasteland.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { ownLand },
        });
        activated.Resolve();

        _alice.Zones.Graveyard.GetCards().Should().Contain(ownLand);
        ownLand.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(wasteland);
        wasteland.Zone.Should().Be(ZoneType.Graveyard);
    }
}
