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
/// Tests for <see cref="CivicWayfinderFactory"/> — Creature — Elf Druid
/// Warrior {2}{G} 2/2 (Ravnica / many reprints). Oracle text (verified
/// against Scryfall):
///   "When this creature enters, you may search your library for a basic
///    land card, reveal it, put it into your hand, then shuffle."
///
/// Covers:
///   - Card identity (Creature, Elf Druid Warrior, 2/2, {2}{G}, owner /
///     controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Single ETB triggered ability.
///   - Resolve: tutors a basic land card to the HAND (not battlefield),
///     then shuffles.
///   - Resolve: a nonbasic land is NOT eligible (oracle says "basic land").
///   - Resolve: zero basics in library → no-op (legal under CR 701.19a).
/// </summary>
public class CivicWayfinderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ───────────────────────────────────────────────────────────────────
    // Identity / dispatch
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void CivicWayfinder_IsElfDruidWarrior2_2_AtCost2G()
    {
        var card = CivicWayfinderFactory.Create(_alice);

        card.Name.Should().Be("Civic Wayfinder");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        card.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        card.Power.Should().Be(2);
        card.Toughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CivicWayfinder()
    {
        var card = NamedCardFactory.Create("Civic Wayfinder", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Civic Wayfinder");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{G}");
    }

    [Fact]
    public void CivicWayfinder_HasExactlyOneTriggeredAbility()
    {
        var card = CivicWayfinderFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one ETB trigger on Civic Wayfinder.");
    }

    // ───────────────────────────────────────────────────────────────────
    // Resolve
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void EtbTrigger_TutorsBasicLandToHand()
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var card = CivicWayfinderFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(forest,
            "the tutored basic land goes to the controller's hand");
        forest.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().NotContain(forest);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(forest,
            "Civic Wayfinder puts the basic into hand, not the battlefield.");
    }

    [Fact]
    public void EtbTrigger_NonbasicLand_IsNotEligible()
    {
        // A nonbasic land — even one with a basic land subtype (dual) — is
        // not a "basic land card": the oracle text requires the Basic
        // supertype (CR 305.6 / CR 205.4a).
        var dual = new Land("Stomping Ground",
            subtypes: new[] { CardSubtype.Mountain, CardSubtype.Forest });
        dual.SetOwner(_alice);
        _alice.Zones.Library.AddCard(dual);
        dual.SetZone(ZoneType.Library);

        var card = CivicWayfinderFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().NotContain(dual,
            "a nonbasic land lacks the Basic supertype; not a legal target.");
        _alice.Zones.Library.GetCards().Should().Contain(dual);
    }

    [Fact]
    public void EtbTrigger_NoBasicsInLibrary_IsNoOp()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        bears.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(bears);

        var card = CivicWayfinderFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        Action act = () => { foreach (var e in trigger.Effects) e.Execute(); };
        act.Should().NotThrow(
            "no basics → no-op (CR 701.19a — finding nothing is legal).");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().Contain(bears);
    }
}
