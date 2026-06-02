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
/// Tests for <see cref="BottleGnomesFactory"/> — Artifact Creature — Gnome {3}
/// 1/3 (Tempest). Oracle text (Scryfall, verified 2026-06-02):
///   "Sacrifice this creature: You gain 3 life."
///
/// Covers:
///   - Card identity (Artifact + Creature + Gnome, {3}, 1/3, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Ability shape: single <see cref="ActivatedAbility"/> with exactly one
///     cost (Sacrifice this creature; NO mana, NO Tap) and no target requests.
///   - Resolve: sacrifices the gnomes (battlefield -> graveyard) and the
///     controller gains 3 life.
/// </summary>
public class BottleGnomesTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BottleGnomes_IsArtifactCreatureGnome_AtThree_OneThree()
    {
        var c = BottleGnomesFactory.Create(_alice);

        c.Name.Should().Be("Bottle Gnomes");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Bottle Gnomes is BOTH Artifact and Creature (CR 205.2a)");
        c.HasSubtype(CardSubtype.Gnome).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BottleGnomes()
    {
        var card = NamedCardFactory.Create("Bottle Gnomes", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Bottle Gnomes");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void Gnomes_HasSingleActivatedAbility_NoManaAbilities()
    {
        var c = BottleGnomesFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Ability_HasSacrificeOnly_NoMana_NoTap_NoTargets()
    {
        var c = BottleGnomesFactory.Create(_alice);
        var ab = c.Abilities.OfType<ActivatedAbility>().Single();

        ab.TargetRequests.Should().BeEmpty();
        ab.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "Bottle Gnomes' sacrifice ability has no mana cost");
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Sacrifice,
            "the ability sacrifices the gnomes");
        ab.Costs.OfType<AdditionalCost>().Should().NotContain(
            c => c.CostType == AdditionalCostType.Tap,
            "Bottle Gnomes' printed cost has no {T} pip");
    }

    [Fact]
    public void Activate_GainsThreeLife_AndSacrificesGnomes()
    {
        var gnomes = BottleGnomesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(gnomes);
        gnomes.SetZone(ZoneType.Battlefield);

        var startingLife = _alice.LifeTotal;

        var ab = gnomes.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.LifeTotal.Should().Be(startingLife + 3, "you gain 3 life");

        _alice.Zones.Graveyard.GetCards().Should().Contain(gnomes,
            "the gnomes were sacrificed as a cost");
        gnomes.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(gnomes);
    }
}
