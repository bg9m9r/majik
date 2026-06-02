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
using Creature = Majik.Core.Cards.Creature;
using Enchantment = Majik.Core.Cards.Enchantment;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SealOfFireFactory"/>.
///
/// Seal of Fire (Nemesis, {R}):
///   Enchantment.
///   "Sacrifice this enchantment: It deals 2 damage to any target."
///
/// Covers:
///   - Identity (Enchantment, {R}, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Activated ability shape: a sacrifice cost (no mana, no tap) + one
///     any-target request (CR 602).
///   - Resolution: 2 damage to player / creature / planeswalker target
///     (CR 306.7 loyalty route); the Seal is sacrificed to the graveyard.
/// </summary>
[Trait("Color", "R")]
public class SealOfFireFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SealOfFire_Identity()
    {
        var seal = SealOfFireFactory.Create(_alice);

        seal.Name.Should().Be("Seal of Fire");
        seal.ManaCost.Should().Be("{R}");
        seal.HasType(CardType.Enchantment).Should().BeTrue();
        seal.Owner.Should().BeSameAs(_alice);
        seal.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SealOfFire_ActivatedAbility_HasSacrifice_NoMana_OneAnyTarget()
    {
        var seal = SealOfFireFactory.Create(_alice);

        var ability = seal.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the ability sacrifices the Seal itself");
        ability.Costs.OfType<AdditionalCost>()
            .Should().NotContain(c => c.CostType == AdditionalCostType.Tap,
                "Seal of Fire has no tap cost");
        ability.Costs.OfType<ManaCostCost>()
            .Should().BeEmpty("the ability has no mana cost");

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.TargetRequests[0].Description.Should().Contain("any target");
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_DealsTwoToPlayer_AndSacrificesSeal()
    {
        var seal = SealOfFireFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(seal);
        seal.SetZone(ZoneType.Battlefield);

        var ability = seal.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        ability.Resolve();

        _bob.LifeTotal.Should().Be(18, "2 damage to Bob");

        _alice.Zones.Graveyard.GetCards().Should().Contain(seal);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(seal);
        seal.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_DealsTwoToCreatureTarget()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield);

        var seal = SealOfFireFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(seal);
        seal.SetZone(ZoneType.Battlefield);

        var ability = seal.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bears },
        });

        ability.Resolve();

        bears.Damage.Should().Be(2, "2 marked damage on the bears");
        _alice.Zones.Graveyard.GetCards().Should().Contain(seal);
    }

    [Fact]
    public void Activate_PlaneswalkerTarget_RoutesToLoyaltyRemoval()
    {
        // CR 306.7 — damage to a planeswalker removes loyalty counters.
        var pw = new Planeswalker("Test Walker", "{3}", startingLoyalty: 4,
            subtypes: new[] { CardSubtype.Chandra });
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var seal = SealOfFireFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(seal);
        seal.SetZone(ZoneType.Battlefield);

        var ability = seal.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { pw },
        });

        ability.Resolve();

        pw.Loyalty.Should().Be(2, "2 loyalty counters removed (4 - 2)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(seal);
    }
}
