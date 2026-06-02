using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="FierceEmpathFactory"/> — Creature — Elf {2}{G} 1/1
/// (Legions / reprints). Oracle:
///   "When this creature enters, you may search your library for a creature
///    card with mana value 6 or greater, reveal it, put it into your hand,
///    then shuffle."
///
/// Covers:
///   - Card identity (Creature + Elf, {2}{G}, 1/1, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Ability shape: exactly one ETB <see cref="TriggeredAbility"/>, no
///     activated/mana abilities, no target requests.
///   - ETB resolve: tutors ONE creature with mana value ≥ 6 into hand.
///   - ETB resolve: only sub-6 creatures in library → no card moved.
/// </summary>
[Trait("Color", "G")]
public class FierceEmpathFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void FierceEmpath_IsElf_AtTwoG_OneOne()
    {
        var c = FierceEmpathFactory.Create(_alice);

        c.Name.Should().Be("Fierce Empath");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FierceEmpath_HasOneEtbTrigger_NoActivatedOrManaAbilities()
    {
        var c = FierceEmpathFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void EtbTrigger_HasNoTargetRequests()
    {
        var c = FierceEmpathFactory.Create(_alice);

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        etb.TargetRequests.Should().BeEmpty();
    }

    [Fact]
    public void Etb_Tutors_OneBigCreatureIntoHand()
    {
        // Mana value 7 creature — eligible (≥ 6).
        var titan = new Creature("Primeval Titan", "{4}{G}{G}", 6, 6,
            subtypes: new[] { CardSubtype.Giant });
        titan.SetOwner(_alice);
        _alice.Zones.Library.AddCard(titan);
        titan.SetZone(ZoneType.Library);

        // A small creature (mana value 1) that must NOT be eligible.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        bear.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        var startHand = _alice.Zones.Hand.GetCards().Count();

        var empath = FierceEmpathFactory.Create(_alice);
        var etb = empath.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        var hand = _alice.Zones.Hand.GetCards().ToList();
        hand.Count.Should().Be(startHand + 1,
            "Fierce Empath searches for A (one) creature with mana value ≥ 6");
        hand.OfType<Creature>().Single().Name.Should().Be("Primeval Titan");
        hand.OfType<Creature>().Single().Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().Contain(bear,
            "the small creature is not eligible (mana value < 6)");
    }

    [Fact]
    public void Etb_NoBigCreatureInLibrary_MovesNoCard()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        bear.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        var startHand = _alice.Zones.Hand.GetCards().Count();

        var empath = FierceEmpathFactory.Create(_alice);
        var etb = empath.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Hand.GetCards().Count().Should().Be(startHand,
            "no creature with mana value ≥ 6 → nothing put into hand");
        _alice.Zones.Library.GetCards().Should().Contain(bear);
    }
}
