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
/// Unit tests for <see cref="RaucousTheaterFactory"/> (Murders at Karlov Manor
/// "surveil land" dual cycle — the B/R member).
///
/// B/R surveil tapland. Oracle text (verified against Scryfall):
///   "({T}: Add {B} or {R}.)
///    This land enters tapped.
///    When this land enters, surveil 1. (Look at the top card of your
///    library. You may put it into your graveyard.)"
///
/// Type line is <c>Land — Swamp Mountain</c>. The shared cycle suite
/// (<see cref="Majik.Core.Tests.CardData.SurveilLandCycleTests"/>) already
/// covers identity / dual mana / ETB surveil for every cycle-mate; this fixture
/// pins the one detail it doesn't — the printed Swamp + Mountain subtypes — and
/// re-asserts the unique ETB surveil-1 behaviour (CR 701.43) for this member.
/// </summary>
[Trait("Color", "M")]
public class RaucousTheaterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity — the one assert the shared cycle suite doesn't make:
    // printed type line is "Land — Swamp Mountain".
    // -----------------------------------------------------------------------

    [Fact]
    public void RaucousTheater_Identity_LandWithSwampMountainSubtypes()
    {
        var land = RaucousTheaterFactory.Create(_alice);

        land.Name.Should().Be("Raucous Theater");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Swamp).Should().BeTrue(
            "Raucous Theater's printed type line is 'Land — Swamp Mountain'");
        land.HasSubtype(CardSubtype.Mountain).Should().BeTrue(
            "Raucous Theater's printed type line is 'Land — Swamp Mountain'");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Raucous Theater is a nonbasic Land");
    }

    // -----------------------------------------------------------------------
    // ETB surveil 1 (CR 603.6a + CR 701.43) — with no registered agent the
    // peeked top card defaults into the controller's graveyard (same posture
    // as the rest of the surveil-land cycle).
    // -----------------------------------------------------------------------

    [Fact]
    public void RaucousTheater_SurveilEffect_PutsTopCardInGraveyard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", "");
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var land = RaucousTheaterFactory.Create(alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        foreach (var effect in trigger.Effects) effect.Execute();

        alice.Zones.Graveyard.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Graveyard);
    }
}
