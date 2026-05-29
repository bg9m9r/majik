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
/// - Activated ability shape: {G} mana cost + sacrifice-self, single 1..1
///   "noncreature artifact or noncreature enchantment" TargetRequest.
/// - Resolution: legal noncreature artifact target → exiled + mite sac'd.
/// - Resolution: legal noncreature enchantment target → exiled + mite sac'd.
/// - Resolution: artifact creature target → fizzles (CR 608.2b) but mite
///   still sacrifices.
/// </summary>
public class HaywireMiteTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

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

    [Fact]
    public void NamedCardFactory_Dispatches_HaywireMite()
    {
        var card = NamedCardFactory.Create("Haywire Mite", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Haywire Mite");
        ((Creature)card).Power.Should().Be(1);
        ((Creature)card).Toughness.Should().Be(1);
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
        activated.TargetRequests.Should().HaveCount(1);
        activated.TargetRequests[0].MinTargets.Should().Be(1);
        activated.TargetRequests[0].MaxTargets.Should().Be(1);
        activated.TargetRequests[0].Description.Should().Contain("noncreature");
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
    public void HaywireMite_Exile_NoncreatureArtifact_TargetExiled_MiteSacrificed()
    {
        var artifact = new Artifact("Aether Spellbomb", "{1}");
        artifact.SetOwner(_bob);
        artifact.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        var mite = HaywireMiteFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mite);
        mite.SetZone(ZoneType.Battlefield);

        var activated = mite.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { artifact },
        });
        activated.Resolve();

        _bob.Zones.Exile.GetCards().Should().Contain(artifact);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(artifact);
        artifact.Zone.Should().Be(ZoneType.Exile);

        _alice.Zones.Graveyard.GetCards().Should().Contain(mite);
        mite.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void HaywireMite_Exile_NoncreatureEnchantment_TargetExiled_MiteSacrificed()
    {
        var enchantment = new Enchantment("Rancor", "{G}");
        enchantment.SetOwner(_bob);
        enchantment.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(enchantment);
        enchantment.SetZone(ZoneType.Battlefield);

        var mite = HaywireMiteFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mite);
        mite.SetZone(ZoneType.Battlefield);

        var activated = mite.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { enchantment },
        });
        activated.Resolve();

        _bob.Zones.Exile.GetCards().Should().Contain(enchantment);
        enchantment.Zone.Should().Be(ZoneType.Exile);

        _alice.Zones.Graveyard.GetCards().Should().Contain(mite);
        mite.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void HaywireMite_Exile_ArtifactCreature_FizzlesButStillSacrifices()
    {
        // CR 608.2b — an artifact creature is NOT a "noncreature artifact",
        // so it is an illegal target; the exile half does nothing. The
        // sacrifice cost is paid on activation (modeled inline here), so
        // Haywire Mite still goes to the graveyard.
        var artifactCreature = AdaptiveAutomatonFactory.Create(_bob);
        _bob.Zones.Battlefield.AddCard(artifactCreature);
        artifactCreature.SetZone(ZoneType.Battlefield);

        var mite = HaywireMiteFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mite);
        mite.SetZone(ZoneType.Battlefield);

        var activated = mite.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { artifactCreature },
        });
        activated.Resolve();

        // Artifact creature stays on the battlefield (illegal target).
        _bob.Zones.Battlefield.GetCards().Should().Contain(artifactCreature);
        artifactCreature.Zone.Should().Be(ZoneType.Battlefield);

        // Haywire Mite still sacrificed itself.
        _alice.Zones.Graveyard.GetCards().Should().Contain(mite);
        mite.Zone.Should().Be(ZoneType.Graveyard);
    }
}
