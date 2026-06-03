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
/// Tests for <see cref="CatharCommandoFactory"/> — Creature — Human Soldier
/// {1}{W} 3/1 (Innistrad: Midnight Hunt) with Flash and a single activated
/// ability:
///   "Flash
///    {1}, Sacrifice this creature: Destroy target artifact or enchantment."
///
/// Covers:
///   - Card identity (Creature, {1}{W}, 3/1, Human + Soldier subtypes,
///     owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Flash keyword marker.
///   - Ability shape: single <see cref="ActivatedAbility"/> with a {1} mana
///     cost + sacrifice additional cost and one 1..1 target request.
///   - Resolve: target artifact → destroyed; commando sacrificed.
///   - Resolve: target enchantment → destroyed; commando sacrificed.
///   - Resolve: target creature (illegal pick) → no destroy, but commando
///     still sacrificed (cost was paid; CR 608.2b).
///   - Resolve: target left the battlefield → no destroy, commando still
///     sacrificed (CR 608.2b).
/// </summary>
public class CatharCommandoTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CatharCommando_IsHumanSoldier_AtOneW_ThreeOne()
    {
        var c = CatharCommandoFactory.Create(_alice);

        c.Name.Should().Be("Cathar Commando");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CatharCommando()
    {
        var card = NamedCardFactory.Create("Cathar Commando", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Cathar Commando");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{W}");
    }

    // -----------------------------------------------------------------------
    // Flash keyword marker (CR 702.8)
    // -----------------------------------------------------------------------

    [Fact]
    public void CatharCommando_HasFlashKeyword()
    {
        var c = CatharCommandoFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flash",
                "the printed text leads with Flash (CR 702.8)");
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void CatharCommando_HasSingleSacrificeAbility_WithManaAndOneTarget()
    {
        var c = CatharCommandoFactory.Create(_alice);

        var abilities = c.Abilities.OfType<ActivatedAbility>().ToList();
        abilities.Should().HaveCount(1);

        var ab = abilities[0];
        ab.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(x => x.CostType == AdditionalCostType.Sacrifice,
                "the printed cost includes 'Sacrifice this creature'");
        // ManaCost.ToString() renders the generic {1} as the bare string "1".
        ab.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(x => x.Cost.Generic == 1
                    && x.Cost.ToString() == "1",
                "the printed cost includes a {1} mana component");

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
        var trinket = new Artifact("Bob's Trinket", "{2}");
        trinket.SetOwner(_bob);
        trinket.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(trinket);
        trinket.SetZone(ZoneType.Battlefield);

        var commando = CatharCommandoFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(commando);
        commando.SetZone(ZoneType.Battlefield);

        var ab = commando.Abilities.OfType<ActivatedAbility>().Single();
        ab.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { trinket } });
        ab.Resolve();

        trinket.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(trinket);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(trinket);

        commando.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(commando);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(commando);
    }

    [Fact]
    public void Activate_DestroysTargetEnchantment_AndSacrificesSelf()
    {
        var aura = new Enchantment("Sticky Web", "{1}{G}");
        aura.SetOwner(_bob);
        aura.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        var commando = CatharCommandoFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(commando);
        commando.SetZone(ZoneType.Battlefield);

        var ab = commando.Abilities.OfType<ActivatedAbility>().Single();
        ab.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { aura } });
        ab.Resolve();

        aura.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(aura);
        commando.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(commando);
    }

    [Fact]
    public void Activate_NonArtifactNonEnchantmentTarget_DestroyNoOp_StillSacrifices()
    {
        // Pure creature target — not artifact or enchantment. Resolution-time
        // predicate fails, so the destroy is a no-op (CR 608.2b), but the
        // sacrifice already happened (cost was paid).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var commando = CatharCommandoFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(commando);
        commando.SetZone(ZoneType.Battlefield);

        var ab = commando.Abilities.OfType<ActivatedAbility>().Single();
        ab.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });
        ab.Resolve();

        bear.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(bear);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bear);

        commando.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(commando);
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

        var commando = CatharCommandoFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(commando);
        commando.SetZone(ZoneType.Battlefield);

        var ab = commando.Abilities.OfType<ActivatedAbility>().Single();
        ab.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { trinket } });

        // Simulate the artifact leaving the battlefield between activation and
        // resolution (e.g. bounced or sacrificed).
        _bob.Zones.Battlefield.RemoveCard(trinket);
        _bob.Zones.Hand.AddCard(trinket);
        trinket.SetZone(ZoneType.Hand);

        ab.Resolve();

        trinket.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(trinket);

        commando.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(commando);
    }
}
