using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="FanaticalFirebrandFactory"/> — 1/1 Goblin Pirate {R}
/// with Haste + "{T}, Sacrifice ~: ~ deals 1 damage to any target."
///
/// Covers:
/// - Identity (Creature, {R}, 1/1, Goblin Pirate, owner/controller).
/// - NamedCardFactory dispatch.
/// - Haste keyword marker present.
/// - Activated-ability shape: tap + sacrifice + 1..1 any target.
/// - Resolution: damage to player / creature / planeswalker; sac happens.
/// </summary>
public class FanaticalFirebrandTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FanaticalFirebrand_IsRedGoblinPirate_OneOne_ForR()
    {
        var fb = FanaticalFirebrandFactory.Create(_alice);

        fb.HasType(CardType.Creature).Should().BeTrue();
        fb.Name.Should().Be("Fanatical Firebrand");
        fb.ManaCost.Should().Be("{R}");
        fb.Power.Should().Be(1);
        fb.Toughness.Should().Be(1);
        fb.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        fb.HasSubtype(CardSubtype.Pirate).Should().BeTrue();
        fb.Owner.Should().BeSameAs(_alice);
        fb.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FanaticalFirebrand()
    {
        var card = NamedCardFactory.Create("Fanatical Firebrand", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Fanatical Firebrand");
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Haste marker (CR 702.10)
    // -----------------------------------------------------------------------

    [Fact]
    public void FanaticalFirebrand_HasHasteKeywordMarker()
    {
        var fb = FanaticalFirebrandFactory.Create(_alice);
        fb.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => string.Equals(k.Keyword, "Haste", StringComparison.OrdinalIgnoreCase));
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Ability_HasTap_AndSacrifice_AndOneAnyTarget()
    {
        var fb = FanaticalFirebrandFactory.Create(_alice);

        var ability = fb.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the activation taps the firebrand");
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the activation sacrifices the firebrand");

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.TargetRequests[0].Description.Should().Contain("any target");
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_DealsOneToPlayerTarget_AndSacrifices()
    {
        var fb = FanaticalFirebrandFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(fb);
        fb.SetZone(ZoneType.Battlefield);

        var ability = fb.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        ability.Resolve();

        _bob.LifeTotal.Should().Be(19, "1 damage to Bob");
        _alice.Zones.Graveyard.GetCards().Should().Contain(fb);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(fb);
        fb.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_DealsOneToCreatureTarget()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield);

        var fb = FanaticalFirebrandFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(fb);
        fb.SetZone(ZoneType.Battlefield);

        var ability = fb.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bears },
        });

        ability.Resolve();

        bears.Damage.Should().Be(1);
        _alice.Zones.Graveyard.GetCards().Should().Contain(fb);
    }

    [Fact]
    public void Activate_PlaneswalkerTarget_RoutesToLoyaltyRemoval()
    {
        // CR 306.7 — damage to a planeswalker removes that many loyalty
        // counters. Fx.DealDamageAny routes Planeswalker → RemoveLoyalty.
        var pw = new Planeswalker("Test Walker", "{3}", startingLoyalty: 4,
            subtypes: new[] { CardSubtype.Chandra });
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var fb = FanaticalFirebrandFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(fb);
        fb.SetZone(ZoneType.Battlefield);

        var ability = fb.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { pw },
        });

        ability.Resolve();

        pw.Loyalty.Should().Be(3, "1 loyalty counter removed (4 - 1)");
    }
}
