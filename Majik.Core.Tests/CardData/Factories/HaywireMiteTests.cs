using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="HaywireMiteFactory"/> — The Brothers' War
/// Artifact Creature — Insect {1} 1/1. Oracle text (verified against
/// Scryfall):
///   "When this creature dies, you gain 2 life.
///    {G}, Sacrifice this creature: Exile target noncreature artifact or
///    noncreature enchantment."
///
/// Covers:
/// - Card identity (1/1 Insect, {1}, Artifact Creature, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Dies trigger shape (CR 700.4) + life gain on resolve (CR 119.3).
/// - Activated ability shape: {G} mana cost + sacrifice-self additional cost
///   (now a declarative <see cref="AdditionalCost"/> with
///   <see cref="AdditionalCostType.Sacrifice"/>, NOT a resolution-time
///   closure), single 1..1 "noncreature artifact or noncreature enchantment"
///   TargetRequest.
/// - Cost: paying the activation cost sacrifices the mite (CR 602.5 / 118.8).
/// - Resolution: legal noncreature artifact / enchantment target → exiled.
/// - Resolution: artifact creature target → fizzles (CR 608.2b).
/// </summary>
[Trait("Color", "C")]
public class HaywireMiteTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MiteOnBattlefield(Player owner)
    {
        var mite = HaywireMiteFactory.Create(owner);
        owner.Zones.Battlefield.AddCard(mite);
        mite.SetZone(ZoneType.Battlefield);
        return mite;
    }

    private static ActivatedAbility ExileAbility(Creature mite) =>
        mite.Abilities.OfType<ActivatedAbility>().Single();

    private static ICost SacrificeCost(ActivatedAbility ability) =>
        ability.Costs.Single(c =>
            c is AdditionalCost ac && ac.CostType == AdditionalCostType.Sacrifice);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void HaywireMite_Is11ArtifactCreatureInsect_WithGenericManaCost()
    {
        var mite = HaywireMiteFactory.Create(_alice);

        mite.Name.Should().Be("Haywire Mite");
        mite.ManaCost.Should().Be("{1}");
        mite.Power.Should().Be(1);
        mite.Toughness.Should().Be(1);
        mite.HasType(CardType.Creature).Should().BeTrue();
        mite.HasType(CardType.Artifact).Should().BeTrue();
        mite.Subtypes.Should().Contain(CardSubtype.Insect);
        mite.Owner.Should().BeSameAs(_alice);
        mite.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Ability shapes
    // -----------------------------------------------------------------------

    [Fact]
    public void HaywireMite_HasDiesTrigger_AndExileActivatedAbility()
    {
        var mite = HaywireMiteFactory.Create(_alice);

        mite.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);

        var activated = mite.Abilities.OfType<ActivatedAbility>().Single();
        activated.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            because: "the activation cost includes {G}.");
        activated.Costs.OfType<AdditionalCost>()
            .Where(c => c.CostType == AdditionalCostType.Sacrifice)
            .Should().ContainSingle(
                because: "sacrifice-this is a declarative additional cost (CR 602.5).");
        activated.TargetRequests.Should().HaveCount(1);
        activated.TargetRequests[0].MinTargets.Should().Be(1);
        activated.TargetRequests[0].MaxTargets.Should().Be(1);
        activated.TargetRequests[0].Description.Should().Contain("noncreature");
    }

    [Fact]
    public void HaywireMite_PayingActivationCost_SacrificesTheMite()
    {
        // CR 602.5 / 118.8 — the sacrifice is an ADDITIONAL COST, paid at
        // activation (NOT during resolution). Pay the declarative sacrifice
        // additional cost and assert the mite moved to the graveyard.
        var mite = MiteOnBattlefield(_alice);
        var sacCost = SacrificeCost(ExileAbility(mite));

        sacCost.CanPay(_alice).Should().BeTrue();
        sacCost.Pay(_alice);

        _alice.Zones.Graveyard.GetCards().Should().Contain(mite);
        mite.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Dies trigger — life gain (CR 119.3)
    // -----------------------------------------------------------------------

    [Fact]
    public void HaywireMite_Dies_ControllerGains2Life()
    {
        var mite = HaywireMiteFactory.Create(_alice);

        var diesTrigger = mite.Abilities.OfType<TriggeredAbility>().Single();
        _alice.LifeTotal.Should().Be(20);

        diesTrigger.Resolve();

        _alice.LifeTotal.Should().Be(22);
    }

    // -----------------------------------------------------------------------
    // Activated ability — exile target noncreature artifact/enchantment
    // -----------------------------------------------------------------------

    [Fact]
    public void HaywireMite_ProductionActivation_PaysManaAndSacrificesMite()
    {
        // End-to-end through the PRODUCTION activation path
        // (AbilityActivator.ActivateAbility → CostPayment.PayCosts), which pays
        // EVERY ICost — not just mana. Proves the sacrifice additional cost
        // (CR 602.5 / 118.8) is paid by activation, the gap the old hand-rolled
        // factory worked around by sacrificing inside the resolution closure.
        var mite = MiteOnBattlefield(_alice);
        _alice.AddManaToPool(ManaCost.Parse("G"));

        var ability = ExileAbility(mite);
        var costs = ability.Costs;

        var stack = new Majik.Core.Stack.Stack(new EventBus());
        var activator = new AbilityActivator(stack, new EventBus());
        activator.ActivateAbility(ability, _alice, targets: null, costs: costs);

        // {G} consumed and the mite sacrificed as part of activation.
        _alice.ManaPool.IsEmpty.Should().BeTrue("the {G} mana cost was paid");
        _alice.Zones.Graveyard.GetCards().Should().Contain(mite,
            "the sacrifice additional cost was paid at activation");
        mite.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void HaywireMite_Exile_NoncreatureArtifact_TargetExiled()
    {
        var artifact = new Artifact("Aether Spellbomb", "{1}");
        artifact.SetOwner(_bob);
        artifact.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        var mite = MiteOnBattlefield(_alice);
        var activated = ExileAbility(mite);

        // CR 602.5 — sacrifice is paid as a cost at activation; the exile is
        // the resolution effect.
        SacrificeCost(activated).Pay(_alice);
        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { artifact },
        });
        activated.Resolve();

        _bob.Zones.Exile.GetCards().Should().Contain(artifact);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(artifact);
        artifact.Zone.Should().Be(ZoneType.Exile);

        // The mite was sacrificed (the additional cost).
        _alice.Zones.Graveyard.GetCards().Should().Contain(mite);
        mite.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void HaywireMite_Exile_NoncreatureEnchantment_TargetExiled()
    {
        var enchantment = new Enchantment("Rancor", "{G}");
        enchantment.SetOwner(_bob);
        enchantment.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(enchantment);
        enchantment.SetZone(ZoneType.Battlefield);

        var mite = MiteOnBattlefield(_alice);
        var activated = ExileAbility(mite);

        SacrificeCost(activated).Pay(_alice);
        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { enchantment },
        });
        activated.Resolve();

        _bob.Zones.Exile.GetCards().Should().Contain(enchantment);
        enchantment.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Graveyard.GetCards().Should().Contain(mite);
    }

    [Fact]
    public void HaywireMite_Exile_ArtifactCreature_FizzlesButMiteStillSacrificed()
    {
        // CR 608.2b — an artifact creature is NOT a "noncreature artifact",
        // so it is an illegal target; the exile half does nothing. The
        // sacrifice cost was paid on activation, so Haywire Mite still goes to
        // the graveyard.
        var artifactCreature = AdaptiveAutomatonFactory.Create(_bob);
        _bob.Zones.Battlefield.AddCard(artifactCreature);
        artifactCreature.SetZone(ZoneType.Battlefield);

        var mite = MiteOnBattlefield(_alice);
        var activated = ExileAbility(mite);

        SacrificeCost(activated).Pay(_alice);
        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { artifactCreature },
        });
        activated.Resolve();

        // Artifact creature stays on the battlefield (illegal target).
        _bob.Zones.Battlefield.GetCards().Should().Contain(artifactCreature);
        artifactCreature.Zone.Should().Be(ZoneType.Battlefield);

        // Haywire Mite still sacrificed itself (cost paid at activation).
        _alice.Zones.Graveyard.GetCards().Should().Contain(mite);
        mite.Zone.Should().Be(ZoneType.Graveyard);
    }
}
