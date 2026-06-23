using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="ArborealGrazerFactory"/> — Creature — Sloth Beast
/// {G} 0/3 with Reach (Ravnica Allegiance). Oracle:
///   "Reach
///    When this creature enters, you may put a land card from your hand
///    onto the battlefield tapped."
///
/// Covers ONLY the card's unique behaviour plus a single identity assert:
///   - Identity: Creature — Sloth Beast, {G}, 0/3, Reach marker.
///   - Ability shape: exactly one ETB <see cref="TriggeredAbility"/>, no
///     activated / mana abilities, no target requests.
///   - ETB resolve (no agent): puts ONE land from hand onto the battlefield
///     tapped (CR 701.18); other hand lands stay in hand.
///   - ETB resolve: no lands in hand → nothing moved ("may" no-op).
/// </summary>
[Trait("Color", "G")]
public class ArborealGrazerTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ArborealGrazer_Identity_SlothBeast_AtG_ZeroThree_Reach()
    {
        var c = ArborealGrazerFactory.Create(_alice);

        c.Name.Should().Be("Arboreal Grazer");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Sloth).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.Power.Should().Be(0);
        c.Toughness.Should().Be(3);
        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Reach",
                "Arboreal Grazer has Reach (CR 702.9)");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ArborealGrazer_HasOneEtbTrigger_NoActivatedOrManaAbilities()
    {
        var c = ArborealGrazerFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void EtbTrigger_HasNoTargetRequests()
    {
        var c = ArborealGrazerFactory.Create(_alice);

        var trig = c.Abilities.OfType<TriggeredAbility>().Single();
        trig.TargetRequests.Should().BeEmpty();
    }

    [Fact]
    public void Etb_Puts_OneLandFromHand_OntoBattlefield_Tapped()
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(forest);
        forest.SetZone(ZoneType.Hand);

        // A second land so we exercise the "A (one) land" path — only ONE
        // should be moved.
        var island = new Land("Island",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(island);
        island.SetZone(ZoneType.Hand);

        var grazer = ArborealGrazerFactory.Create(_alice);
        var etb = grazer.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        var battlefield = _alice.Zones.Battlefield.GetCards();
        battlefield.Count(c => c is Land).Should().Be(1,
            "Arboreal Grazer puts A (one) land onto the battlefield");
        var movedLand = battlefield.OfType<Land>().Single();
        movedLand.IsTapped.Should().BeTrue("the land enters tapped (CR 701.18)");
        movedLand.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Hand.GetCards().Should().Contain(c => c is Land,
            "only one of the two hand lands is put onto the battlefield");
    }

    [Fact]
    public void Etb_NoLandsInHand_MovesNothing()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        var grazer = ArborealGrazerFactory.Create(_alice);
        var etb = grazer.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().NotContain(bear);
        _alice.Zones.Hand.GetCards().Should().Contain(bear);
    }
}
