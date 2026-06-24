using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BellowingCrierFactory"/>.
///
/// Bellowing Crier ({1}{U} Creature — Frog Advisor, 2/1):
///   "When this creature enters, draw a card, then discard a card."
///
/// Covers:
/// - Identity ({1}{U} 2/1 Frog Advisor, blue, mana value 2).
/// - The unique ETB loot: a single battlefield-active triggered ability that
///   draws a card FIRST and then mandatorily discards a card (ordered loot,
///   net-neutral hand size, digs one card deep).
/// - Discard pick is agent-driven (ScriptedAgent QueueFromHand).
/// - Empty-library draw flags the SBA loss but the discard still applies.
/// </summary>
[Trait("Color", "U")]
public class BellowingCrierFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose() => AgentRegistry.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void BellowingCrier_Identity()
    {
        var c = BellowingCrierFactory.Create(_alice);

        c.Name.Should().Be("Bellowing Crier");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Frog).Should().BeTrue("Bellowing Crier is a Frog");
        c.HasSubtype(CardSubtype.Advisor).Should().BeTrue("Bellowing Crier is an Advisor");
        c.ManaCost.Should().Be("{1}{U}");
        c.ManaCostValue.TotalValue.Should().Be(2, "CR 202.3 — {1}{U} has mana value 2");

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Blue, "{U} pip → blue (CR 202.2c)");
        colors.Should().HaveCount(1, "only one color");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void BellowingCrier_HasExactlyOneTriggeredAbility_BattlefieldActive()
    {
        var c = BellowingCrierFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "exactly one ETB loot trigger");

        triggers.Single().ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active (CR 603.6a)");
    }

    // -----------------------------------------------------------------------
    // Unique behaviour: ETB loot — draw a card, then discard a card
    // -----------------------------------------------------------------------

    [Fact]
    public void BellowingCrier_Etb_DrawsThenDiscards_NetNeutralHand()
    {
        // Hand: one card the agent will discard. Library: one card to draw.
        var inHand = new Creature("InHand", "{B}", 1, 1);
        inHand.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(inHand);

        var topOfLibrary = new Creature("Drawn", "{G}", 1, 1);
        _alice.Zones.Library.AddCard(topOfLibrary);

        // Agent discards the originally-held card (not the freshly drawn one),
        // proving the draw resolves before the discard pick is offered.
        var agent = new ScriptedAgent();
        agent.QueueFromHand(inHand);
        AgentRegistry.Set(_alice, agent);

        var crier = BellowingCrierFactory.Create(_alice);
        var etb = crier.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        // Drew the library card; discarded the held card. Net hand size 1.
        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(topOfLibrary,
                "the freshly drawn card stays in hand; the held card was discarded");
        _alice.Zones.Graveyard.GetCards().Should().Contain(inHand,
            "'then discard a card' put the chosen card into the graveyard (CR 701.16)");
        _alice.Zones.Library.GetCards().Should().BeEmpty("the single library card was drawn");
    }

    [Fact]
    public void BellowingCrier_Etb_EmptyLibrary_FlagsLoss_ButDiscardStillApplies()
    {
        // Empty library — the draw fails (CR 704.5b flag) but the held card is
        // still discarded ("then discard a card" is independent of the draw).
        var inHand = new Creature("InHand", "{B}", 1, 1);
        inHand.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(inHand);

        var crier = BellowingCrierFactory.Create(_alice);
        var etb = crier.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "drawing from an empty library flags the SBA loss (CR 704.5b)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(inHand,
            "the discard still resolves even when the draw failed");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
