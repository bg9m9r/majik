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
/// Tests for <see cref="CausticCaterpillarFactory"/> — Creature — Insect {G}
/// 1/1 (Magic Origins) with a single sacrifice-self activated ability:
///   "Sacrifice this creature: Destroy target artifact or enchantment."
///
/// Covers:
///   - Card identity (Creature, {G}, 1/1, Insect subtype, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Ability shape: single <see cref="ActivatedAbility"/> with a sacrifice
///     additional cost and one 1..1 target request.
///   - Resolve: target artifact → destroyed; caterpillar sacrificed.
///   - Resolve: target enchantment → destroyed; caterpillar sacrificed.
///   - Resolve: target creature (illegal pick) → no destroy, but caterpillar
///     still sacrificed (cost was paid; CR 608.2b).
///   - Resolve: target left the battlefield → no destroy, caterpillar
///     still sacrificed (CR 608.2b).
/// </summary>
public class CausticCaterpillarTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CausticCaterpillar_IsInsect_AtG_OneOne()
    {
        var c = CausticCaterpillarFactory.Create(_alice);

        c.Name.Should().Be("Caustic Caterpillar");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Insect).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CausticCaterpillar()
    {
        var card = NamedCardFactory.Create("Caustic Caterpillar", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Caustic Caterpillar");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{G}");
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void CausticCaterpillar_HasSingleSacrificeAbility_WithOneTarget()
    {
        var c = CausticCaterpillarFactory.Create(_alice);

        var abilities = c.Abilities.OfType<ActivatedAbility>().ToList();
        abilities.Should().HaveCount(1);

        var ab = abilities[0];
        ab.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(x => x.CostType == AdditionalCostType.Sacrifice,
                "the printed cost is 'Sacrifice this creature'");
        ab.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "the ability carries no mana component");

        ab.TargetRequests.Should().ContainSingle();
        ab.TargetRequests[0].MinTargets.Should().Be(1);
        ab.TargetRequests[0].MaxTargets.Should().Be(1);
        ab.TargetRequests[0].Description.Should()
            .Contain("artifact").And.Contain("enchantment");
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_DestroysTargetArtifact_AndSacrificesSelf()
    {
        // Bob controls a vanilla artifact; Alice activates the sac ability.
        var trinket = new Artifact("Bob's Trinket", "{2}");
        trinket.SetOwner(_bob);
        trinket.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(trinket);
        trinket.SetZone(ZoneType.Battlefield);

        var bug = CausticCaterpillarFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bug);
        bug.SetZone(ZoneType.Battlefield);

        var ab = bug.Abilities.OfType<ActivatedAbility>().Single();
        ab.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { trinket } });
        ab.Resolve();

        // Artifact destroyed.
        trinket.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(trinket);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(trinket);

        // Caterpillar sacrificed.
        bug.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(bug);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bug);
    }

    [Fact]
    public void Activate_DestroysTargetEnchantment_AndSacrificesSelf()
    {
        var aura = new Enchantment("Sticky Web", "{1}{G}");
        aura.SetOwner(_bob);
        aura.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        var bug = CausticCaterpillarFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bug);
        bug.SetZone(ZoneType.Battlefield);

        var ab = bug.Abilities.OfType<ActivatedAbility>().Single();
        ab.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { aura } });
        ab.Resolve();

        aura.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(aura);
        bug.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(bug);
    }

    [Fact]
    public void Activate_NonArtifactNonEnchantmentTarget_DestroyNoOp_StillSacrifices()
    {
        // Pure creature target — not artifact or enchantment. Resolution-
        // time predicate fails, so the destroy is a no-op (CR 608.2b),
        // but the sacrifice already happened (cost was paid).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var bug = CausticCaterpillarFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bug);
        bug.SetZone(ZoneType.Battlefield);

        var ab = bug.Abilities.OfType<ActivatedAbility>().Single();
        ab.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });
        ab.Resolve();

        // Bear stays put.
        bear.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(bear);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bear);

        // Caterpillar still sacrificed.
        bug.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(bug);
    }

    [Fact]
    public void Activate_TargetLeftBattlefield_DestroyNoOp_StillSacrifices()
    {
        // Target artifact removed before resolution (CR 608.2b — illegal
        // target → no-op on destroy; sacrifice still happens).
        var trinket = new Artifact("Trinket", "{1}");
        trinket.SetOwner(_bob);
        trinket.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(trinket);
        trinket.SetZone(ZoneType.Battlefield);

        var bug = CausticCaterpillarFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bug);
        bug.SetZone(ZoneType.Battlefield);

        var ab = bug.Abilities.OfType<ActivatedAbility>().Single();
        ab.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { trinket } });

        // Simulate the artifact leaving the battlefield between activation
        // and resolution (e.g. bounced or sacrificed).
        _bob.Zones.Battlefield.RemoveCard(trinket);
        _bob.Zones.Hand.AddCard(trinket);
        trinket.SetZone(ZoneType.Hand);

        ab.Resolve();

        // Trinket unaffected by the destroy (already off-battlefield).
        trinket.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(trinket);

        // Caterpillar still sacrificed.
        bug.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(bug);
    }
}
