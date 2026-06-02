using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="FiligreeFamiliarFactory"/> — Artifact Creature — Fox
/// {3} 2/2 (Kaladesh). Oracle:
///   "When this creature enters, you gain 2 life.
///    When this creature dies, draw a card."
///
/// Covers:
///   - Card identity (Artifact + Creature + Fox, {3}, 2/2, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Ability shape: exactly two <see cref="TriggeredAbility"/> (one ETB, one
///     dies), no activated/mana abilities, no target requests.
///   - ETB resolve: controller gains 2 life (CR 119.3).
///   - Dies resolve: controller draws a card (CR 121.1).
/// </summary>
public class FiligreeFamiliarFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void FiligreeFamiliar_IsArtifactCreatureFox_AtThree_TwoTwo()
    {
        var c = FiligreeFamiliarFactory.Create(_alice);

        c.Name.Should().Be("Filigree Familiar");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Filigree Familiar is BOTH Artifact and Creature (CR 205.2a)");
        c.HasSubtype(CardSubtype.Fox).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FiligreeFamiliar()
    {
        var card = NamedCardFactory.Create("Filigree Familiar", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Filigree Familiar");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void Familiar_HasTwoTriggers_NoActivatedOrManaAbilities()
    {
        var c = FiligreeFamiliarFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void Triggers_HaveNoTargetRequests()
    {
        var c = FiligreeFamiliarFactory.Create(_alice);

        foreach (var trig in c.Abilities.OfType<TriggeredAbility>())
        {
            trig.TargetRequests.Should().BeEmpty();
        }
    }

    [Fact]
    public void Etb_GainsTwoLife()
    {
        var start = _alice.LifeTotal;

        var familiar = FiligreeFamiliarFactory.Create(_alice);
        var etb = familiar.Abilities.OfType<TriggeredAbility>().Single(IsEtb);
        etb.Resolve();

        _alice.LifeTotal.Should().Be(start + 2,
            "the ETB trigger gains 2 life (CR 119.3)");
    }

    [Fact]
    public void Dies_DrawsACard()
    {
        // Seed the library so a draw has something to take.
        var top = new Land("Plains",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Plains });
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var startHand = _alice.Zones.Hand.GetCards().Count();

        var familiar = FiligreeFamiliarFactory.Create(_alice);
        var dies = familiar.Abilities.OfType<TriggeredAbility>().Single(t => !IsEtb(t));
        dies.Resolve();

        _alice.Zones.Hand.GetCards().Count().Should().Be(startHand + 1,
            "the dies trigger draws one card (CR 121.1)");
    }

    // The ETB trigger is the one active in Battlefield only; the dies trigger
    // is active in Battlefield + Graveyard. Disambiguate by active zones.
    private static bool IsEtb(TriggeredAbility t) =>
        !t.ActiveZones.Contains(ZoneType.Graveyard);
}
