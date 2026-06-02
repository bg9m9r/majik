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
/// Tests for Ranger of Eos — Creature — Human Soldier Ranger {3}{W} 3/2
/// (Shards of Alara / reprints).
///
/// Oracle text (per Scryfall):
///   "When this creature enters, you may search your library for up to two
///    creature cards with mana value 1 or less, reveal them, put them into
///    your hand, then shuffle."
///
/// Covers:
/// - Card identity (P/T, subtypes, mana cost) + dispatcher routing.
/// - ETB tutor: searches the controller's library for the first TWO creature
///   cards with mana value ≤ 1 and moves them to hand (CR 603.6a / 701.19a /
///   701.20a shuffle). The up-to-two cap is the only difference from
///   Ranger-Captain of Eos's single-card fetch.
/// </summary>
public class RangerOfEosTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RangerOfEos_HasCorrectIdentity_AndPT_AndSubtypes()
    {
        var rc = RangerOfEosFactory.Create(_alice);

        rc.Name.Should().Be("Ranger of Eos");
        rc.ManaCost.Should().Be("{3}{W}");
        rc.Power.Should().Be(3);
        rc.Toughness.Should().Be(2);
        rc.HasType(CardType.Creature).Should().BeTrue();
        rc.HasSubtype(CardSubtype.Human).Should().BeTrue();
        rc.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        rc.HasSubtype(CardSubtype.Ranger).Should().BeTrue();
        rc.Owner.Should().BeSameAs(_alice);
        rc.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_RoutesRangerOfEos_ToFactory()
    {
        var card = NamedCardFactory.Create("Ranger of Eos", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Ranger of Eos");
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        card.HasSubtype(CardSubtype.Ranger).Should().BeTrue();
        ((Creature)card).Power.Should().Be(3);
        ((Creature)card).Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // ETB tutor (CR 603.6a / 701.19a)
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbTutor_OnResolve_MovesUpToTwoMvLeq1Creatures_FromLibrary_ToHand()
    {
        var rc = RangerOfEosFactory.Create(_alice);

        // Three mv-≤-1 creatures + one too-expensive creature.
        var lion = new Creature("Savannah Lions", "{W}", 2, 1);
        var mystic = new Creature("Birds of Paradise", "{G}", 0, 1);
        var dryad = new Creature("Dryad Arbor", "", 1, 1);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        foreach (var c in new[] { lion, mystic, dryad, bear })
        {
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var trigger = rc.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in trigger.Effects) effect.Execute();

        // Exactly two eligible creatures move to hand (the first two found).
        _alice.Zones.Hand.GetCards().Should().HaveCount(2);
        _alice.Zones.Hand.GetCards().Should().OnlyContain(c =>
            ((Creature)c).Power == 2 && ((Creature)c).Toughness == 1
            || ((Creature)c).Power == 0
            || ((Creature)c).Power == 1);
        // Bear (mv=2) never leaves the library.
        _alice.Zones.Library.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void EtbTutor_OnResolve_OnlyOneEligible_MovesThatOne_ToHand()
    {
        var rc = RangerOfEosFactory.Create(_alice);

        var lion = new Creature("Savannah Lions", "{W}", 2, 1);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        foreach (var c in new[] { lion, bear })
        {
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var trigger = rc.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(lion);
        _alice.Zones.Hand.GetCards().Should().HaveCount(1);
        _alice.Zones.Library.GetCards().Should().Contain(bear);
        _alice.Zones.Library.GetCards().Should().NotContain(lion);
    }

    [Fact]
    public void EtbTutor_OnResolve_NoEligibleCard_LeavesHandUntouched()
    {
        var rc = RangerOfEosFactory.Create(_alice);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        var trigger = rc.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().Contain(bear);
    }
}
