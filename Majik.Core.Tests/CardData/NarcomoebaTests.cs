using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using MajikStack = Majik.Core.Stack.Stack;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="NarcomoebaFactory"/> (Future Sight, {1}{U}).
///
/// Covers:
///   - Identity (Illusion 1/1, {1}{U}, owner/controller, Flying marker).
///   - NamedCardFactory dispatch.
///   - Mill-trigger (Library → Graveyard) returns the card to the
///     battlefield (CR 603.6c).
///   - Trigger does NOT fire when moved by another path (Hand → Graveyard
///     does not count).
/// </summary>
public class NarcomoebaTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Narcomoeba_Identity_Illusion_1_1_AtCost1U()
    {
        var card = NarcomoebaFactory.Create(_alice);

        card.Name.Should().Be("Narcomoeba");
        card.ManaCost.Should().Be("{1}{U}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Illusion).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Narcomoeba_HasFlyingMarker()
    {
        var card = NarcomoebaFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flying");
    }

    [Fact]
    public void Narcomoeba_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Narcomoeba", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Narcomoeba");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Illusion).Should().BeTrue();
    }

    [Fact]
    public void Narcomoeba_HasMillTrigger_AttachedToCard()
    {
        var card = NarcomoebaFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one library→graveyard mill trigger is attached (CR 603.6c)");
    }

    // -----------------------------------------------------------------------
    // Mill-trigger — CR 603.6c (graveyard-resident)
    // -----------------------------------------------------------------------

    [Fact]
    public void MillTrigger_LibraryToGraveyard_ReturnsCardToBattlefield()
    {
        var (zones, stack, triggers, _) = BuildEngine();

        var card = NarcomoebaFactory.Create(
            _alice, zoneService: zones, triggers: triggers, agent: null);

        // Seat Narcomoeba on top of Alice's library, then mill it via
        // ZoneService so CardMovedEvent fires the graveyard-resident
        // trigger (registered with activeZones = {Graveyard}).
        _alice.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);

        zones.MoveCard(card, ZoneType.Library, ZoneType.Graveyard, _alice);

        triggers.PendingCount.Should().Be(1, "mill trigger queued for Narcomoeba");

        triggers.PutPendingTriggersOnStack(activePlayer: _alice);
        var triggerOnStack = (TriggeredAbility)stack.Pop()!;
        triggerOnStack.Resolve();

        card.Zone.Should().Be(ZoneType.Battlefield,
            "Narcomoeba returns from graveyard to battlefield on mill trigger (CR 603.6c)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(card);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(card);
    }

    [Fact]
    public void MillTrigger_DoesNotFire_OnHandToGraveyard()
    {
        var (zones, _, triggers, _) = BuildEngine();

        var card = NarcomoebaFactory.Create(
            _alice, zoneService: zones, triggers: triggers, agent: null);

        // Discard from hand — Hand → Graveyard, NOT Library → Graveyard.
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        zones.MoveCard(card, ZoneType.Hand, ZoneType.Graveyard, _alice);

        triggers.PendingCount.Should().Be(0,
            "Narcomoeba's trigger only fires on library→graveyard (printed text)");
        card.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (ZoneService zones, MajikStack stack, TriggerManager triggers, ReplacementBus replacements) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new MajikStack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers, rep);
    }
}
