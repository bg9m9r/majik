using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// CR 702.35 — Madness. Reusable mechanic: discard → exile (via
/// <see cref="MadnessReplacement"/> on the <see cref="ReplacementBus"/>), then
/// cast for the madness cost or fall through to the graveyard
/// (<see cref="MadnessHelper"/>). Proven via Fiery Temper ({R}),
/// Call to the Netherworld ({0}), and Alms of the Vein ({B}).
/// </summary>
public class MadnessTests
{
    private static (Player alice, ReplacementBus bus, ZoneService zones) Setup()
    {
        var alice = new Player("Alice", 20);
        var bus = new ReplacementBus();
        var zones = new ZoneService(eventBus: null, replacements: bus);
        return (alice, bus, zones);
    }

    [Fact]
    public void Discard_WithoutMadness_GoesToGraveyard()
    {
        var (alice, _, zones) = Setup();
        var plain = new Instant("Plain Spell", "1R") { Owner = alice };
        alice.Zones.Hand.AddCard(plain);
        plain.SetZone(ZoneType.Hand);

        // No MadnessReplacement registered → discard resolves to graveyard.
        var outcome = MadnessHelper.Discard(plain, alice, zones, tryCastForMadness: _ => true);

        outcome.Should().Be(MadnessHelper.Outcome.ToGraveyard);
        plain.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Discard_FieryTemper_GoesToExile_ThenCastForMadness()
    {
        var (alice, bus, zones) = Setup();
        var temper = FieryTemperFactory.Create(alice, bus);
        alice.Zones.Hand.AddCard(temper);
        temper.SetZone(ZoneType.Hand);

        ICard? offered = null;
        var outcome = MadnessHelper.Discard(temper, alice, zones, tryCastForMadness: c =>
        {
            // CR 702.35c — the card is in exile and offered for its madness cost.
            offered = c;
            c.Zone.Should().Be(ZoneType.Exile);
            FieryTemperFactory.MadnessAltCost.CanCastFor(c, alice).Should().BeTrue(
                "the exiled card is castable for its madness cost by its owner");
            return true; // cast it
        });

        outcome.Should().Be(MadnessHelper.Outcome.CastForMadness);
        offered.Should().BeSameAs(temper, "the madness window offered the discarded card");
        // The cast pipeline (caller's responsibility) would move it to the stack;
        // here the callback "cast" it, so it does NOT fall through to graveyard.
        temper.Zone.Should().Be(ZoneType.Exile, "the test callback left it in exile (a real cast moves it to the stack)");
    }

    [Fact]
    public void Discard_FieryTemper_DeclineMadness_GoesToGraveyard()
    {
        var (alice, bus, zones) = Setup();
        var temper = FieryTemperFactory.Create(alice, bus);
        alice.Zones.Hand.AddCard(temper);
        temper.SetZone(ZoneType.Hand);

        var outcome = MadnessHelper.Discard(temper, alice, zones, tryCastForMadness: _ => false);

        outcome.Should().Be(MadnessHelper.Outcome.ToGraveyard,
            "declining the madness cast puts the card into the graveyard (CR 702.35c)");
        temper.Zone.Should().Be(ZoneType.Graveyard);
        alice.Zones.Exile.GetCards().Should().NotContain(temper);
    }

    [Fact]
    public void CallToTheNetherworld_HasFreeMadnessCost()
    {
        var (alice, bus, zones) = Setup();
        var call = CallToTheNetherworldFactory.Create(alice, bus);
        alice.Zones.Hand.AddCard(call);
        call.SetZone(ZoneType.Hand);

        CallToTheNetherworldFactory.MadnessAltCost.AlternativeManaCost
            .Should().Be(ManaCost.Parse("{0}"), "Call to the Netherworld's madness cost is free");

        var outcome = MadnessHelper.Discard(call, alice, zones, tryCastForMadness: c =>
        {
            c.Zone.Should().Be(ZoneType.Exile);
            return true;
        });
        outcome.Should().Be(MadnessHelper.Outcome.CastForMadness);
    }

    [Fact]
    public void AlmsOfTheVein_MadnessReplacement_ExilesOnDiscard()
    {
        var (alice, bus, zones) = Setup();
        var alms = AlmsOfTheVeinFactory.Create(alice, bus);
        alice.Zones.Hand.AddCard(alms);
        alms.SetZone(ZoneType.Hand);

        var outcome = MadnessHelper.Discard(alms, alice, zones, tryCastForMadness: c =>
        {
            c.Zone.Should().Be(ZoneType.Exile, "Alms is discarded into exile (CR 702.35b)");
            AlmsOfTheVeinFactory.MadnessAltCost.AlternativeManaCost
                .Should().Be(ManaCost.Parse("{B}"));
            return false; // decline
        });

        outcome.Should().Be(MadnessHelper.Outcome.ToGraveyard);
        alms.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void MadnessAltCost_RejectsCast_FromHand()
    {
        var alice = new Player("Alice", 20);
        var card = new Instant("Fiery Temper", "1RR") { Owner = alice, Zone = ZoneType.Hand };
        var cost = new MadnessAlternativeCost(ManaCost.Parse("{R}"));

        cost.CanCastFor(card, alice).Should().BeFalse(
            "madness only permits casting from exile, not from hand");
    }
}
