using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="LeadTheStampedeFactory"/> — Sorcery {2}{G}.
///
/// Oracle text (verified against Scryfall):
///   "Look at the top five cards of your library. You may reveal any number
///    of creature cards from among them and put the revealed cards into your
///    hand. Put the rest on the bottom of your library in any order."
///
/// Dispatch + well-formedness are covered for every implemented card by
/// <see cref="CardFactoryContractTests"/>; this file covers only the card's
/// identity (non-vanilla mana cost / type / colour) and its UNIQUE resolve
/// behaviour:
/// - Look at the top five; creature cards go to hand, the rest bottom.
/// - Mixed top-five: only the creatures reach hand, non-creatures bottom
///   (CR 701.16 reveal-to-hand + CR 701.20 bottom-the-rest).
/// - The sixth card and below are never touched (look window is five).
/// - No creatures in the top five: nothing to hand, all five bottomed.
/// - Library shorter than five: works on whatever is available.
/// - Empty library: no throw, nothing moves (CR 701.21 short look).
/// </summary>
[Trait("Color", "G")]
public class LeadTheStampedeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var card = LeadTheStampedeFactory.Create(_alice);

        card.Name.Should().Be("Lead the Stampede");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Green, "the {G} pip makes it green");
    }

    // -----------------------------------------------------------------------
    // Resolve — unique behaviour
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_MixedTopFive_CreaturesToHand_RestBottomed_SixthUntouched()
    {
        // Top → bottom: Creature, Land, Creature, Instant, Land, Creature(#6).
        var c1 = SeedCreature(_alice, "Bear");
        var l1 = SeedLand(_alice, "Forest");
        var c2 = SeedCreature(_alice, "Elf");
        var i1 = SeedInstant(_alice, "Bolt");
        var l2 = SeedLand(_alice, "Island");
        var c6 = SeedCreature(_alice, "DeepCreature"); // 6th card — outside the look window.

        var result = LeadTheStampedeFactory.Resolve(_alice);

        // Five looked at; the two creatures in the window go to hand.
        result.LookedAt.Should().HaveCount(5);
        result.LookedAt.Should().Contain(new ICard[] { c1, l1, c2, i1, l2 });
        result.LookedAt.Should().NotContain(c6, "the sixth card is never looked at");

        result.PutInHand.Should().BeEquivalentTo(new ICard[] { c1, c2 });
        _alice.Zones.Hand.GetCards().Should().BeEquivalentTo(new ICard[] { c1, c2 });

        // The three non-creatures are bottomed; the untouched c6 remains too.
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(4);
        lib.Should().BeEquivalentTo(new ICard[] { c6, l1, i1, l2 });
        lib.Should().NotContain(new ICard[] { c1, c2 },
            "the creature cards left the library for hand");
        // c6 stays on top; the bottomed non-creatures go beneath it.
        lib[0].Should().BeSameAs(c6);
    }

    [Fact]
    public void Resolve_NoCreaturesInTopFive_NothingToHand_AllBottomed()
    {
        var l1 = SeedLand(_alice, "L1");
        var l2 = SeedLand(_alice, "L2");
        var i1 = SeedInstant(_alice, "I1");
        var i2 = SeedInstant(_alice, "I2");
        var i3 = SeedInstant(_alice, "I3");

        var result = LeadTheStampedeFactory.Resolve(_alice);

        result.PutInHand.Should().BeEmpty("no creature was in the top five");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        result.LookedAt.Should().HaveCount(5);
        _alice.Zones.Library.GetCards().Should()
            .BeEquivalentTo(new ICard[] { l1, l2, i1, i2, i3 },
                "every looked-at card was bottomed; none reached the hand");
    }

    [Fact]
    public void Resolve_ShortLibrary_WorksOnAvailableCards()
    {
        var c1 = SeedCreature(_alice, "Bear");
        var l1 = SeedLand(_alice, "Forest");

        var result = LeadTheStampedeFactory.Resolve(_alice);

        result.LookedAt.Should().HaveCount(2);
        result.PutInHand.Should().BeEquivalentTo(new ICard[] { c1 });
        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(c1);
        _alice.Zones.Library.GetCards().Should().ContainSingle().Which.Should().BeSameAs(l1);
    }

    [Fact]
    public void Resolve_EmptyLibrary_NoThrow_NothingMoves()
    {
        var result = LeadTheStampedeFactory.Resolve(_alice);

        result.LookedAt.Should().BeEmpty();
        result.PutInHand.Should().BeEmpty();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers — index 0 of the library list is the TOP (GetCards order).
    // -----------------------------------------------------------------------

    private static ICard SeedCreature(Player p, string name)
    {
        ICard c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(p);
        c.SetController(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static ICard SeedInstant(Player p, string name)
    {
        ICard c = new Instant(name, "{1}");
        c.SetOwner(p);
        c.SetController(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static ICard SeedLand(Player p, string name)
    {
        ICard c = new Land(name);
        c.SetOwner(p);
        c.SetController(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
