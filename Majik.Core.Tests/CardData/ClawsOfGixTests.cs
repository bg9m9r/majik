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
/// Unit tests for <see cref="ClawsOfGixFactory"/>.
///
/// Card: Claws of Gix — Artifact {0}. Oracle text:
///   "{1}, Sacrifice a permanent: You gain 1 life."
///
/// Covers:
/// - Card identity: Artifact, mana cost {0}, mana value 0.
/// - Dispatch via <see cref="NamedCardFactory"/>.
/// - Exactly one activated ability with a {1} mana cost + sac-any-permanent cost.
/// - Effect: controller gains 1 life.
/// - CanPay false when controller controls no permanents.
/// - CanPay true when controller controls any permanent (creature, land, self).
/// - Self-sacrifice: Claws of Gix itself is a legal sacrifice target.
/// - Sac fodder lands in graveyard; life total increases by 1.
/// </summary>
public class ClawsOfGixTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ClawsOfGix_IsArtifact()
    {
        var claws = ClawsOfGixFactory.Create(_alice);
        claws.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void ClawsOfGix_IsNotCreature()
    {
        var claws = ClawsOfGixFactory.Create(_alice);
        claws.HasType(CardType.Creature).Should().BeFalse();
    }

    [Fact]
    public void ClawsOfGix_NameAndManaCost()
    {
        var claws = ClawsOfGixFactory.Create(_alice);
        claws.Name.Should().Be("Claws of Gix");
        claws.ManaCost.Should().Be("{0}");
    }

    [Fact]
    public void ClawsOfGix_OwnerAndControllerAreSet()
    {
        var claws = ClawsOfGixFactory.Create(_alice);
        claws.Owner.Should().BeSameAs(_alice);
        claws.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ClawsOfGix_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Claws of Gix", _alice);
        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Claws of Gix");
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability structure
    // -----------------------------------------------------------------------

    [Fact]
    public void ClawsOfGix_HasExactlyOneActivatedAbility()
    {
        var claws = ClawsOfGixFactory.Create(_alice);
        claws.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void ClawsOfGix_Ability_HasManaCostCostOf1Generic()
    {
        var claws = ClawsOfGixFactory.Create(_alice);
        var ability = claws.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.OfType<ManaCostCost>().Should().HaveCount(1);
        ability.Costs.OfType<ManaCostCost>().Single().Cost.Generic.Should().Be(1,
            "cost is {1} — one generic mana");
    }

    [Fact]
    public void ClawsOfGix_Ability_HasSacrificeAnyPermanentCost()
    {
        var claws = ClawsOfGixFactory.Create(_alice);
        var ability = claws.Abilities.OfType<ClawsOfGixAbility>().Single();
        ability.SacrificeChoice.Should().NotBeNull();
    }

    [Fact]
    public void ClawsOfGix_HasNoKeywordAbilities()
    {
        var claws = ClawsOfGixFactory.Create(_alice);
        claws.Abilities.OfType<KeywordAbility>().Should().BeEmpty();
    }

    [Fact]
    public void ClawsOfGix_HasNoTriggeredAbilities()
    {
        var claws = ClawsOfGixFactory.Create(_alice);
        claws.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // CanPay logic (CR 602.5a)
    // -----------------------------------------------------------------------

    [Fact]
    public void SacrificeAnyPermanentCost_CannotPay_WhenNoPermanentsOnBattlefield()
    {
        var claws = ClawsOfGixFactory.Create(_alice);
        // Controller has no permanents — not even Claws itself (not yet placed).
        var ability = claws.Abilities.OfType<ClawsOfGixAbility>().Single();
        ability.SacrificeChoice.CanPay(_alice).Should().BeFalse(
            "controller controls no permanents to sacrifice");
    }

    [Fact]
    public void SacrificeAnyPermanentCost_CanPay_WhenControllerHasCreature()
    {
        var claws = ClawsOfGixFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(claws);
        claws.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice); bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var ability = claws.Abilities.OfType<ClawsOfGixAbility>().Single();
        ability.SacrificeChoice.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void SacrificeAnyPermanentCost_CanPay_WhenClawsItselfIsOnBattlefield()
    {
        // Claws of Gix itself counts as the permanent to sacrifice
        // (no "another" restriction on the printed text — CR 602).
        var claws = ClawsOfGixFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(claws);
        claws.SetZone(ZoneType.Battlefield);

        var ability = claws.Abilities.OfType<ClawsOfGixAbility>().Single();
        ability.SacrificeChoice.CanPay(_alice).Should().BeTrue(
            "Claws of Gix itself is a legal sacrifice target");
    }

    // -----------------------------------------------------------------------
    // Effect: gain 1 life
    // -----------------------------------------------------------------------

    [Fact]
    public void Activation_GainsOneLife_AndSacrificesChosenPermanent()
    {
        var claws = ClawsOfGixFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(claws);
        claws.SetZone(ZoneType.Battlefield);

        var token = new Creature("Goblin Token", "R", 1, 1);
        token.SetOwner(_alice); token.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(token);
        token.SetZone(ZoneType.Battlefield);

        var ability = claws.Abilities.OfType<ClawsOfGixAbility>().Single();
        ability.SacrificeChoice.Target = token;

        // Pay the sacrifice cost and resolve the gain-life effect.
        ability.SacrificeChoice.Pay(_alice);
        foreach (var e in ability.Effects) e.Execute();

        token.Zone.Should().Be(ZoneType.Graveyard, "the token was sacrificed");
        _alice.LifeTotal.Should().Be(21, "gained 1 life from the ability");
    }

    [Fact]
    public void Activation_SelfSacrifice_ClawsGoesToGraveyard_AndControllerGainsLife()
    {
        // Claws of Gix can sacrifice itself as the permanent cost.
        var claws = ClawsOfGixFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(claws);
        claws.SetZone(ZoneType.Battlefield);

        var ability = claws.Abilities.OfType<ClawsOfGixAbility>().Single();
        ability.SacrificeChoice.Target = claws; // self-sacrifice

        ability.SacrificeChoice.Pay(_alice);
        foreach (var e in ability.Effects) e.Execute();

        claws.Zone.Should().Be(ZoneType.Graveyard, "Claws of Gix was self-sacrificed");
        _alice.LifeTotal.Should().Be(21, "gained 1 life even from self-sacrifice");
    }

    [Fact]
    public void Activation_PermanentSacrificed_GoesToOwnersGraveyard()
    {
        // CR 701.16a — sacrificed permanents go to their owner's graveyard.
        var claws = ClawsOfGixFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(claws);
        claws.SetZone(ZoneType.Battlefield);

        // A permanent _alice controls but _bob owns (stolen/controlled).
        var stolenToken = new Creature("Stolen Token", "0", 1, 1);
        stolenToken.SetOwner(_bob);     // owned by Bob
        stolenToken.SetController(_alice); // controlled by Alice
        _alice.Zones.Battlefield.AddCard(stolenToken);
        stolenToken.SetZone(ZoneType.Battlefield);

        var ability = claws.Abilities.OfType<ClawsOfGixAbility>().Single();
        ability.SacrificeChoice.Target = stolenToken;
        ability.SacrificeChoice.Pay(_alice);

        stolenToken.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(stolenToken,
            "CR 701.16a — goes to owner's graveyard (Bob's)");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(stolenToken);
    }

    [Fact]
    public void Activation_FallsBackToFirstPermanent_WhenTargetNotSet()
    {
        var claws = ClawsOfGixFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(claws);
        claws.SetZone(ZoneType.Battlefield);

        // No explicit target set — should auto-pick the first permanent.
        var ability = claws.Abilities.OfType<ClawsOfGixAbility>().Single();
        ability.SacrificeChoice.Target = null;

        // CanPay is true (Claws itself is on battlefield).
        ability.SacrificeChoice.CanPay(_alice).Should().BeTrue();

        ability.SacrificeChoice.Pay(_alice);
        foreach (var e in ability.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(21, "fallback auto-pick still resolves gain 1 life");
    }
}
