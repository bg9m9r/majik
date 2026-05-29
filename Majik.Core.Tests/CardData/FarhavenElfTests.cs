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
/// Tests for <see cref="FarhavenElfFactory"/> — Creature — Elf Druid {2}{G}
/// 1/1 (Shadowmoor). Oracle:
///   "When this creature enters, you may search your library for a basic land
///    card, put it onto the battlefield tapped, then shuffle."
///
/// Covers:
///   - Card identity (Creature, Elf Druid, {2}{G}, 1/1, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Ability shape: exactly one ETB <see cref="TriggeredAbility"/>, no
///     activated/mana abilities, no target requests.
///   - ETB resolve: tutors ONE basic land to battlefield tapped (CR 701.18).
///   - ETB resolve: only nonbasics in library → no land moved.
/// </summary>
public class FarhavenElfTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void FarhavenElf_IsElfDruid_AtTwoG_OneOne()
    {
        var c = FarhavenElfFactory.Create(_alice);

        c.Name.Should().Be("Farhaven Elf");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FarhavenElf()
    {
        var card = NamedCardFactory.Create("Farhaven Elf", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Farhaven Elf");
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void Farhaven_HasOneEtbTrigger_NoActivatedOrManaAbilities()
    {
        var c = FarhavenElfFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void EtbTrigger_HasNoTargetRequests()
    {
        var c = FarhavenElfFactory.Create(_alice);

        var trig = c.Abilities.OfType<TriggeredAbility>().Single();
        trig.TargetRequests.Should().BeEmpty();
    }

    [Fact]
    public void Etb_Tutors_OneBasicToBattlefieldTapped()
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        // A second basic so we exercise the "search for A basic" (singular)
        // path — only ONE should be moved.
        var island = new Land("Island",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        _alice.Zones.Library.AddCard(island);
        island.SetZone(ZoneType.Library);

        var elf = FarhavenElfFactory.Create(_alice);

        var etb = elf.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        var battlefield = _alice.Zones.Battlefield.GetCards();
        battlefield.Count(c => c is Land).Should().Be(1,
            "Farhaven Elf searches for A (one) basic land");
        var movedLand = battlefield.OfType<Land>().Single();
        movedLand.IsTapped.Should().BeTrue("the basic enters tapped (CR 701.18)");
        movedLand.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Library.GetCards().Should().Contain(c => c is Land,
            "only one of the two basics is taken");
    }

    [Fact]
    public void Etb_NoBasicsInLibrary_MovesNoLand()
    {
        var bog = new Land("Bojuka Bog"); // nonbasic
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);

        var elf = FarhavenElfFactory.Create(_alice);
        var etb = elf.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().NotContain(bog);
        _alice.Zones.Library.GetCards().Should().Contain(bog);
    }
}
