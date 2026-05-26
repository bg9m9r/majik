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
/// Tests for Aura of Silence (Tempest, {1}{W}{W}).
///
/// Oracle text:
///   "Artifact and enchantment spells your opponents cast cost {2} more
///    to cast."
///   "Sacrifice Aura of Silence: Destroy target artifact or enchantment."
///
/// Covers:
///   - Card identity (Enchantment, {1}{W}{W}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Cost-increase rider: opponents' artifact/enchantment spells cost
///     {2} more; controller's own spells unaffected; non-artifact /
///     non-enchantment spells unaffected.
///   - Ability shape: single <see cref="ActivatedAbility"/> with a
///     sacrifice additional cost and one 1..1 target request.
///   - Resolution: target artifact → destroyed; Aura sacrificed.
///   - Resolution: target enchantment → destroyed; Aura sacrificed.
///   - Resolution: target creature (illegal pick) → no destroy, Aura
///     still sacrificed (CR 608.2b).
///   - Resolution: target off battlefield at resolution → no destroy,
///     Aura still sacrificed (CR 608.2b).
/// </summary>
public class AuraOfSilenceTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void AuraOfSilence_IsEnchantment_AtOneWW()
    {
        var aura = AuraOfSilenceFactory.Create(_alice);

        aura.Name.Should().Be("Aura of Silence");
        aura.ManaCost.Should().Be("{1}{W}{W}");
        aura.HasType(CardType.Enchantment).Should().BeTrue();
        aura.Owner.Should().BeSameAs(_alice);
        aura.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchesAuraOfSilence()
    {
        var card = NamedCardFactory.Create("Aura of Silence", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Aura of Silence");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{W}{W}");
    }

    // -----------------------------------------------------------------------
    // Cost-increase rider — "your opponents' artifact/enchantment spells
    // cost {2} more"
    // -----------------------------------------------------------------------

    [Fact]
    public void CostIncrease_OpponentArtifactSpell_PaysTwoMore()
    {
        // Alice has Aura of Silence on the battlefield. Bob casts an
        // artifact spell — its effective cost should be +{2}.
        var aura = AuraOfSilenceFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        var trinket = new Artifact("Bob's Trinket", "{2}");
        trinket.SetOwner(_bob);
        trinket.SetController(_bob);

        var effective = CostReduction.GetEffectiveCost(
            trinket, _bob, allPlayers: new[] { _alice, _bob });

        effective.Generic.Should().Be(4,
            "Bob's {2} artifact + {2} surcharge from Alice's Aura of Silence");
    }

    [Fact]
    public void CostIncrease_OpponentEnchantmentSpell_PaysTwoMore()
    {
        var aura = AuraOfSilenceFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        var ench = new Enchantment("Bob's Enchantment", "{3}");
        ench.SetOwner(_bob);
        ench.SetController(_bob);

        var effective = CostReduction.GetEffectiveCost(
            ench, _bob, allPlayers: new[] { _alice, _bob });

        effective.Generic.Should().Be(5,
            "{3} + {2} surcharge for opponent's enchantment");
    }

    [Fact]
    public void CostIncrease_DoesNotApplyToControllersOwnArtifactSpell()
    {
        // Alice controls Aura of Silence and casts her own artifact —
        // "your opponents cast" means Alice is exempt.
        var aura = AuraOfSilenceFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        var trinket = new Artifact("Alice's Trinket", "{2}");
        trinket.SetOwner(_alice);
        trinket.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(
            trinket, _alice, allPlayers: new[] { _alice, _bob });

        effective.Generic.Should().Be(2,
            "controller's own artifact spells are not taxed");
    }

    [Fact]
    public void CostIncrease_DoesNotApplyToOpponentNonArtifactNonEnchantmentSpell()
    {
        // Bob casts a creature spell — not artifact, not enchantment —
        // surcharge does not apply.
        var aura = AuraOfSilenceFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);

        var effective = CostReduction.GetEffectiveCost(
            bear, _bob, allPlayers: new[] { _alice, _bob });

        effective.Generic.Should().Be(1,
            "creature spells aren't taxed by Aura of Silence");
    }

    [Fact]
    public void CostIncrease_AlsoCoversArtifactCreatureSpells()
    {
        // Artifact creatures (e.g. Walking Ballista) are artifact spells —
        // they ARE taxed. Walking Ballista's printed cost is {X}{X} so the
        // baked-in generic component is 0; the +{2} surcharge is what
        // we're asserting on.
        var aura = AuraOfSilenceFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        var ballista = NamedCardFactory.Create("Walking Ballista", _bob);
        ballista.SetController(_bob);
        ballista.HasType(CardType.Artifact).Should().BeTrue(
            "precondition — Walking Ballista is an artifact creature");
        ballista.HasType(CardType.Creature).Should().BeTrue();

        var baseline = CostReduction.GetEffectiveCost(
            ballista, _bob, allPlayers: new[] { _bob }).Generic;
        var withAura = CostReduction.GetEffectiveCost(
            ballista, _bob, allPlayers: new[] { _alice, _bob }).Generic;

        (withAura - baseline).Should().Be(2,
            "an artifact creature is an artifact spell and is taxed +{2}");
    }

    [Fact]
    public void CostIncrease_NotRegisteredWhenAuraIsNotOnBattlefield()
    {
        // Aura of Silence is in Alice's hand — it must not tax anything.
        // CostReduction.GetEffectiveCost only scans permanents on the
        // battlefield, so the static is automatically inert off-battlefield.
        var aura = AuraOfSilenceFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(aura);
        aura.SetZone(ZoneType.Hand);

        var trinket = new Artifact("Bob's Trinket", "{2}");
        trinket.SetOwner(_bob);
        trinket.SetController(_bob);

        var effective = CostReduction.GetEffectiveCost(
            trinket, _bob, allPlayers: new[] { _alice, _bob });

        effective.Generic.Should().Be(2,
            "off-battlefield Aura of Silence does not tax spells");
    }

    // -----------------------------------------------------------------------
    // Sacrifice activated ability
    // -----------------------------------------------------------------------

    [Fact]
    public void AuraOfSilence_HasSingleSacrificeAbility_WithOneTarget()
    {
        var aura = AuraOfSilenceFactory.Create(_alice);

        var abilities = aura.Abilities.OfType<ActivatedAbility>().ToList();
        abilities.Should().HaveCount(1);

        var ab = abilities[0];
        ab.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(x => x.CostType == AdditionalCostType.Sacrifice,
                "the printed cost is 'Sacrifice Aura of Silence'");
        ab.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "the ability carries no mana component");

        ab.TargetRequests.Should().ContainSingle();
        ab.TargetRequests[0].MinTargets.Should().Be(1);
        ab.TargetRequests[0].MaxTargets.Should().Be(1);
        ab.TargetRequests[0].Description.Should()
            .Contain("artifact").And.Contain("enchantment");
    }

    [Fact]
    public void Activate_DestroysTargetArtifact_AndSacrificesSelf()
    {
        var trinket = new Artifact("Bob's Trinket", "{2}");
        trinket.SetOwner(_bob);
        trinket.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(trinket);
        trinket.SetZone(ZoneType.Battlefield);

        var aura = AuraOfSilenceFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        var ab = aura.Abilities.OfType<ActivatedAbility>().Single();
        ab.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { trinket } });
        ab.Resolve();

        trinket.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(trinket);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(trinket);

        aura.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(aura);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(aura);
    }

    [Fact]
    public void Activate_DestroysTargetEnchantment_AndSacrificesSelf()
    {
        var ench = new Enchantment("Bob's Enchant", "{1}{G}");
        ench.SetOwner(_bob);
        ench.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(ench);
        ench.SetZone(ZoneType.Battlefield);

        var aura = AuraOfSilenceFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        var ab = aura.Abilities.OfType<ActivatedAbility>().Single();
        ab.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ench } });
        ab.Resolve();

        ench.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(ench);
        aura.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(aura);
    }

    [Fact]
    public void Activate_IllegalCreatureTarget_DestroyNoOp_StillSacrifices()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var aura = AuraOfSilenceFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        var ab = aura.Abilities.OfType<ActivatedAbility>().Single();
        ab.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });
        ab.Resolve();

        bear.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(bear);

        aura.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(aura);
    }

    [Fact]
    public void Activate_TargetLeftBattlefield_DestroyNoOp_StillSacrifices()
    {
        var trinket = new Artifact("Trinket", "{1}");
        trinket.SetOwner(_bob);
        trinket.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(trinket);
        trinket.SetZone(ZoneType.Battlefield);

        var aura = AuraOfSilenceFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        var ab = aura.Abilities.OfType<ActivatedAbility>().Single();
        ab.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { trinket } });

        // Target leaves before resolution.
        _bob.Zones.Battlefield.RemoveCard(trinket);
        _bob.Zones.Hand.AddCard(trinket);
        trinket.SetZone(ZoneType.Hand);

        ab.Resolve();

        trinket.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(trinket);

        aura.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(aura);
    }
}
