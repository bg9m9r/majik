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
/// Bellowing Crier ({1}{U}, Creature — Frog Advisor, 2/1):
///   "When this creature enters, draw a card, then discard a card."
///
/// Covers:
/// - Identity ({1}{U}, Creature — Frog Advisor, 2/1, blue, mana value 2).
/// - Exactly one battlefield-active ETB triggered ability.
/// - ETB loot: draws the top of library, then discards the agent-chosen
///   card — net hand size unchanged, library down one, graveyard up one.
/// - Empty-hand edge: after a draw that exhausts hand+library, no discard.
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
        // {1}{U} = mana value 2 (CR 202.3).
        c.ManaCostValue.TotalValue.Should().Be(2, "CR 202.3 — {1}{U} has mana value 2");

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Blue, "Bellowing Crier has a {U} pip");
        colors.Should().HaveCount(1, "mono-blue");
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

        var etb = triggers.Single();
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active (CR 603.6a)");
        etb.InterveningIf.Should().BeNull("unconditional ETB — no intervening-if");
    }

    // -----------------------------------------------------------------------
    // ETB loot — draw a card, then discard a card
    // -----------------------------------------------------------------------

    [Fact]
    public void BellowingCrier_Etb_DrawsThenDiscards_AgentChosen()
    {
        // Hand: one card the agent will choose to discard.
        var inHand = new Creature("InHand", "{B}", 1, 1);
        inHand.SetOwner(_alice);
        inHand.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(inHand);

        // Library: the card that will be drawn (top).
        var topOfLibrary = new Creature("TopOfLibrary", "{G}", 1, 1);
        topOfLibrary.SetOwner(_alice);
        topOfLibrary.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(topOfLibrary);

        // Agent discards the originally-in-hand card.
        var agent = new ScriptedAgent();
        agent.QueueFromHand(inHand);
        AgentRegistry.Set(_alice, agent);

        var crier = BellowingCrierFactory.Create(_alice);
        var etb = crier.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        // Drew the top of library, discarded the chosen hand card:
        // net hand size unchanged (1), library empty, graveyard has the pick.
        var hand = _alice.Zones.Hand.GetCards().ToList();
        hand.Should().HaveCount(1, "drew one (CR 121.1), then discarded one (CR 701.8) — net zero");
        hand.Should().Contain(topOfLibrary, "the drawn card stays in hand");
        hand.Should().NotContain(inHand, "the chosen card was discarded");

        _alice.Zones.Library.GetCards().Should().BeEmpty("the top of library was drawn");
        _alice.Zones.Graveyard.GetCards().Should().Contain(inHand,
            "the discarded card lands in the graveyard (CR 701.8)");
    }

    [Fact]
    public void BellowingCrier_Etb_EmptyHandAndLibrary_NoDiscard()
    {
        // No cards anywhere: the draw fails (empty library), and there is
        // nothing to discard. The loot resolves as a clean no-op.
        var crier = BellowingCrierFactory.Create(_alice);
        var etb = crier.Abilities.OfType<TriggeredAbility>().Single();

        var act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow("empty hand + library — draw fails, discard no-ops");
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty("nothing was discarded");
    }
}
