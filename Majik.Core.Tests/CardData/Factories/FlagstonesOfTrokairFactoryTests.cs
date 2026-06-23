using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="FlagstonesOfTrokairFactory"/> — Legendary Land
/// (Time Spiral). Oracle:
///   "{T}: Add {W}.
///    When Flagstones of Trokair is put into a graveyard from the battlefield,
///    you may search your library for a Plains card, put it onto the
///    battlefield tapped, then shuffle."
///
/// Covers the card's UNIQUE behaviour:
///   - Identity: Legendary Land producing {W} (single mana ability) plus the
///     leaves-the-battlefield tutor trigger; no activated abilities.
///   - LTB resolve: tutors a Plains card to the battlefield TAPPED (CR 701.18),
///     including a nonbasic Plains-typed land (CR 205.4b — "Plains card" = the
///     Plains land subtype).
///   - LTB resolve: no Plains in library → no land moved.
///
/// Dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests, so no dispatch test here.
/// </summary>
[Trait("Color", "W")]
public class FlagstonesOfTrokairFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Flagstones_Identity_LegendaryLand_ProducesWhite()
    {
        var land = FlagstonesOfTrokairFactory.Create(_alice);

        land.Name.Should().Be("Flagstones of Trokair");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Flagstones of Trokair is a Legendary land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        // {T}: Add {W} — exactly one mana ability, no other activated abilities.
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "the only activatable ability is the {T}: Add {W} mana ability");
    }

    [Fact]
    public void Flagstones_HasOneLeavesBattlefieldTrigger_NoTargets()
    {
        var land = FlagstonesOfTrokairFactory.Create(_alice);

        var triggers = land.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "the only trigger is the LTB tutor");
        triggers[0].ActiveZones.Should().Contain(ZoneType.Graveyard,
            "the trigger looks back from the graveyard after the land leaves play");
        triggers[0].TargetRequests.Should().BeEmpty(
            "the search is a library search, not a targeted ability");
    }

    [Fact]
    public void Ltb_Tutors_PlainsToBattlefieldTapped()
    {
        var plains = new Land("Plains",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Plains });
        plains.SetOwner(_alice);
        _alice.Zones.Library.AddCard(plains);
        plains.SetZone(ZoneType.Library);

        // A non-Plains basic that must NOT be taken.
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var land = FlagstonesOfTrokairFactory.Create(_alice);
        var ltb = land.Abilities.OfType<TriggeredAbility>().Single();
        ltb.Resolve();

        var battlefield = _alice.Zones.Battlefield.GetCards();
        battlefield.Should().Contain(plains, "the Plains card is fetched");
        battlefield.Should().NotContain(forest, "Forest is not a Plains card");
        plains.IsTapped.Should().BeTrue("the Plains enters tapped (CR 701.18)");
        plains.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Library.GetCards().Should().Contain(forest,
            "the non-Plains basic stays in the library");
    }

    [Fact]
    public void Ltb_Tutors_NonbasicPlainsTypedLand()
    {
        // CR 205.4b — a "Plains card" is any card with the Plains land subtype,
        // including nonbasic dual-type lands.
        var dual = new Land("Sacred Foundry",
            supertypes: null,
            subtypes: new[] { CardSubtype.Mountain, CardSubtype.Plains });
        dual.SetOwner(_alice);
        _alice.Zones.Library.AddCard(dual);
        dual.SetZone(ZoneType.Library);

        var land = FlagstonesOfTrokairFactory.Create(_alice);
        var ltb = land.Abilities.OfType<TriggeredAbility>().Single();
        ltb.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().Contain(dual,
            "a nonbasic land with the Plains subtype is a Plains card");
        dual.IsTapped.Should().BeTrue("it enters tapped (CR 701.18)");
    }

    [Fact]
    public void Ltb_NoPlainsInLibrary_MovesNoLand()
    {
        var bog = new Land("Bojuka Bog"); // no Plains subtype
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);

        var land = FlagstonesOfTrokairFactory.Create(_alice);
        var ltb = land.Abilities.OfType<TriggeredAbility>().Single();
        ltb.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().NotContain(bog);
        _alice.Zones.Library.GetCards().Should().Contain(bog);
    }
}
