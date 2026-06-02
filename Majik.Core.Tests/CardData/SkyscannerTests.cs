using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="SkyscannerFactory"/> — Artifact Creature — Thopter
/// {3} 1/1 (Fifth Dawn / many reprints). Oracle (verified against Scryfall):
///   "Flying
///    When this creature enters, draw a card."
///
/// Covers:
///   - Card identity (Artifact + Creature + Thopter, {3}, 1/1, owner / controller).
///   - Flying keyword marker (CR 702.9).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Ability shape: exactly one ETB <see cref="TriggeredAbility"/>, no
///     activated/mana abilities, no target requests.
///   - ETB resolve: controller draws ONE card (CR 603.6a).
/// </summary>
public class SkyscannerTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Skyscanner_IsArtifactCreatureThopter_AtThree_OneOne()
    {
        var c = SkyscannerFactory.Create(_alice);

        c.Name.Should().Be("Skyscanner");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Skyscanner is BOTH Artifact and Creature (CR 205.2a)");
        c.HasSubtype(CardSubtype.Thopter).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Skyscanner_HasFlying()
    {
        var c = SkyscannerFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flying",
                "Skyscanner has Flying (CR 702.9)");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Skyscanner()
    {
        var card = NamedCardFactory.Create("Skyscanner", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Skyscanner");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void Skyscanner_HasOneTrigger_NoActivatedOrManaAbilities()
    {
        var c = SkyscannerFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void EtbTrigger_HasNoTargetRequests()
    {
        var c = SkyscannerFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>().Single();

        etb.TargetRequests.Should().BeEmpty();
    }

    [Fact]
    public void Etb_Draws_OneCard()
    {
        // Two cards in library so the draw has something to pull.
        var top = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var second = new Land("Island",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Island });
        second.SetOwner(_alice);
        _alice.Zones.Library.AddCard(second);
        second.SetZone(ZoneType.Library);

        var startHand = _alice.Zones.Hand.GetCards().Count();
        var startLibrary = _alice.Zones.Library.GetCards().Count();

        var sky = SkyscannerFactory.Create(_alice);
        var etb = sky.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Hand.GetCards().Count().Should().Be(startHand + 1,
            "Skyscanner draws a card when it enters (CR 603.6a)");
        _alice.Zones.Library.GetCards().Count().Should().Be(startLibrary - 1,
            "the drawn card leaves the library");
    }
}
