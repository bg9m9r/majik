using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="VisceraSeerFactory"/>.
///
/// Covers:
/// - Card identity (Creature — Vampire Wizard 1/1, mana cost {B}).
/// - Single activated ability with a sacrifice cost.
/// - Activation sacrifices the fodder + reorders the library top via the
///   default scry-decision posture (all-to-bottom when no agent is
///   registered).
/// </summary>
public class VisceraSeerTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void VisceraSeer_IsVampireWizardCreature()
    {
        var seer = VisceraSeerFactory.Create(_alice);
        seer.HasType(CardType.Creature).Should().BeTrue();
        seer.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        seer.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
    }

    [Fact]
    public void VisceraSeer_NameAndCostAndPT()
    {
        var seer = VisceraSeerFactory.Create(_alice);
        seer.Name.Should().Be("Viscera Seer");
        seer.ManaCost.ToString().Should().Contain("B");
        seer.Power.Should().Be(1);
        seer.Toughness.Should().Be(1);
    }

    [Fact]
    public void VisceraSeer_OwnerAndControllerAreSet()
    {
        var seer = VisceraSeerFactory.Create(_alice);
        seer.Owner.Should().BeSameAs(_alice);
        seer.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void VisceraSeer_HasExactlyOneActivatedAbility()
    {
        var seer = VisceraSeerFactory.Create(_alice);
        seer.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void VisceraSeer_Ability_HasSacrificeAnotherCreatureCost()
    {
        var seer = VisceraSeerFactory.Create(_alice);
        var ability = seer.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.OfType<SacrificeAnotherCreatureCost>().Should().HaveCount(1);
    }

    [Fact]
    public void Cost_CannotPay_WhenSeerIsAlone()
    {
        var seer = VisceraSeerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(seer);
        seer.SetZone(ZoneType.Battlefield);

        var ability = seer.Abilities.OfType<ActivatedAbility>().Single();
        var sac = ability.Costs.OfType<SacrificeAnotherCreatureCost>().Single();
        sac.CanPay(_alice).Should().BeFalse(
            "v1 uses SacrificeAnotherCreatureCost — self-sac deferred");
    }

    [Fact]
    public void Activation_SacrificesFodder_AndScryToBottomByDefault()
    {
        // No agent registered for this fresh Player Id → default scry
        // decision is all-to-bottom. (Do NOT call AgentRegistry.Clear here;
        // it would race other parallel tests that register agents on the
        // shared static registry — e.g. SenseisDiviningTopTests.)
        var seer = VisceraSeerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(seer);
        seer.SetZone(ZoneType.Battlefield);

        var fodder = new Creature("Fodder", "1B", 1, 1);
        fodder.SetOwner(_alice); fodder.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(fodder);
        fodder.SetZone(ZoneType.Battlefield);

        // Library: top card known so we can check it ends up on the bottom.
        var topCard = new Creature("Top", "1B", 1, 1);
        topCard.SetOwner(_alice); topCard.SetController(_alice);
        var deeper = new Creature("Deeper", "1B", 1, 1);
        deeper.SetOwner(_alice); deeper.SetController(_alice);
        _alice.Zones.Library.AddCard(topCard);
        _alice.Zones.Library.AddCard(deeper);
        topCard.SetZone(ZoneType.Library); deeper.SetZone(ZoneType.Library);

        var ability = (VisceraSeerAbility)seer.Abilities.OfType<ActivatedAbility>().Single();
        ability.SacrificeChoice.Target = fodder;

        foreach (var c in ability.Costs) c.Pay(_alice);
        foreach (var e in ability.Effects) e.Execute();

        fodder.Zone.Should().Be(ZoneType.Graveyard, "sacrificed");
        // Default agent-less posture: peeked card sent to the bottom.
        var libraryAfter = _alice.Zones.Library.GetCards().ToList();
        libraryAfter.Last().Should().BeSameAs(topCard, "scry-to-bottom moved it under deeper");
    }
}
