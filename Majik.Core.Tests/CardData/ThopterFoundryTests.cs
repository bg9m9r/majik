using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ThopterFoundryFactory"/> (Conflux, {W/B}{U}).
///
/// Oracle text (Scryfall, verified):
///   "{1}, Sacrifice a nontoken artifact: Create a 1/1 blue Thopter
///    artifact creature token with flying. You gain 1 life."
///
/// Covers:
/// - Identity (Artifact, {W/B}{U}, owner/controller).
/// - NamedCardFactory dispatch.
/// - The single activated ability's cost shape ({1} + nontoken-artifact
///   sacrifice).
/// - Sacrifice CanPay gates on a nontoken artifact (a token artifact does
///   NOT satisfy it).
/// - Resolution mints a 1/1 blue flying Thopter artifact creature token
///   and gains the controller 1 life.
/// </summary>
public class ThopterFoundryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ThopterFoundry_Identity()
    {
        var c = ThopterFoundryFactory.Create(_alice);

        c.Name.Should().Be("Thopter Foundry");
        c.ManaCost.Should().Be("{W/B}{U}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "Thopter Foundry has a single {1}, Sacrifice a nontoken artifact ability");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Thopter Foundry has no triggered abilities");
    }

    [Fact]
    public void ThopterFoundry_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Thopter Foundry", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Thopter Foundry");
    }

    [Fact]
    public void ActivatedAbility_Cost_IsManaPlusNontokenArtifactSacrifice()
    {
        var foundry = ThopterFoundryFactory.Create(_alice);

        var ability = foundry.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().HaveCount(1,
            "the activation cost includes {1}");
        ability.Costs.OfType<SacrificeAnArtifactCost>().Should().HaveCount(1,
            "the activation cost includes a Sacrifice an artifact cost");

        ability.Costs.OfType<SacrificeAnArtifactCost>().Single()
            .Description.Should().Contain("nontoken",
                "the printed cost is 'Sacrifice a nontoken artifact' (CR 111.8)");
    }

    [Fact]
    public void SacrificeCost_CannotBePaid_WithOnlyATokenArtifact()
    {
        var foundry = ThopterFoundryFactory.Create(_alice);

        // A token artifact does NOT satisfy "Sacrifice a nontoken artifact".
        var tokenArtifact = new Artifact("Treasure", "", subtypes: new[] { CardSubtype.Treasure })
        {
            Owner = _alice,
            Controller = _alice,
            IsToken = true,
        };
        _alice.Zones.Battlefield.AddCard(tokenArtifact);
        tokenArtifact.SetZone(ZoneType.Battlefield);

        var sacCost = foundry.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<SacrificeAnArtifactCost>().Single();

        sacCost.CanPay(_alice).Should().BeFalse(
            "CR 111.8 — a token artifact does not satisfy 'Sacrifice a nontoken artifact'");
    }

    [Fact]
    public void SacrificeCost_CanBePaid_WithANontokenArtifact()
    {
        var foundry = ThopterFoundryFactory.Create(_alice);

        var nontoken = new Artifact("Ornithopter", "{0}")
        {
            Owner = _alice,
            Controller = _alice,
        };
        _alice.Zones.Battlefield.AddCard(nontoken);
        nontoken.SetZone(ZoneType.Battlefield);

        var sacCost = foundry.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<SacrificeAnArtifactCost>().Single();

        sacCost.CanPay(_alice).Should().BeTrue(
            "a nontoken artifact on the battlefield satisfies the cost");
    }

    [Fact]
    public void Resolution_CreatesBlueFlyingThopter_AndGainsOneLife()
    {
        var foundry = ThopterFoundryFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(foundry);
        foundry.SetZone(ZoneType.Battlefield);

        var ability = foundry.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        // Lifegain side (CR 119.3).
        _alice.LifeTotal.Should().Be(21, "the controller gains 1 life on resolution");

        // A 1/1 blue Thopter artifact creature token with flying entered.
        var thopter = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.Name == "Thopter");

        thopter.IsToken.Should().BeTrue();
        thopter.BasePower.Should().Be(1);
        thopter.BaseToughness.Should().Be(1);
        thopter.Subtypes.Should().Contain(CardSubtype.Thopter);
        thopter.HasType(CardType.Artifact).Should().BeTrue("Thopter tokens are artifact creatures");
        thopter.HasType(CardType.Creature).Should().BeTrue();
        CombatAbilities.HasFlying(thopter).Should().BeTrue("the Thopter has flying (CR 702.9)");
        CardColors.GetColors(thopter).Should().Contain(ManaColor.Blue, "the Thopter is blue (CR 105.2)");
    }
}
