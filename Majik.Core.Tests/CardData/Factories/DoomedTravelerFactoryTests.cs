using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="DoomedTravelerFactory"/> — Creature — Human Soldier
/// {W} 1/1 with a dies-triggered ability that creates a 1/1 white Spirit
/// creature token with flying under the controller's control.
///
/// Oracle text: "When this creature dies, create a 1/1 white Spirit creature
///               token with flying."
///
/// Covers:
/// - Card identity (name, cost {W}, mana value 1, type Creature,
///   subtypes Human + Soldier, P/T 1/1, owner/controller, colour white).
/// - No keyword abilities on the card itself (Doomed Traveler is vanilla
///   except for the dies trigger).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Exactly one <see cref="TriggeredAbility"/> attached, active in
///   Battlefield + Graveyard zones.
/// - Live dies trigger: fires on Battlefield → Graveyard and places exactly
///   one 1/1 white Spirit creature token with Flying on the controller's
///   battlefield (CR 603.6c / 700.4 / CR 111 / CR 702.9).
/// - No trigger on non-death zone changes (bounce, exile).
/// </summary>
[Trait("Color", "W")]
public class DoomedTravelerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ------------------------------------------------------------------
    // Shape / identity
    // ------------------------------------------------------------------

    [Fact]
    public void DoomedTraveler_IsCorrect_Identity()
    {
        var card = DoomedTravelerFactory.Create(_alice);

        card.Name.Should().Be("Doomed Traveler");
        card.ManaCost.Should().Be("{W}");
        card.ManaCostValue.TotalValue.Should().Be(1,
            "mana value of {W} is 1 (CR 202.3)");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DoomedTraveler_IsWhite()
    {
        var card = DoomedTravelerFactory.Create(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColor.White,
            "{W} mana cost makes the card white (CR 105.2)");
    }

    [Fact]
    public void DoomedTraveler_HasNoKeywordAbility()
    {
        // Doomed Traveler itself has no keywords — it's a vanilla 1/1 with
        // a triggered ability. The Flying is only on the Spirit token it makes.
        var card = DoomedTravelerFactory.Create(_alice);
        card.Abilities.OfType<KeywordAbility>()
            .Should().BeEmpty(
                "Doomed Traveler has no keyword abilities printed on it");
    }

    // ------------------------------------------------------------------
    // Dispatcher
    // ------------------------------------------------------------------
    // ------------------------------------------------------------------
    // Triggered ability — active zones
    // ------------------------------------------------------------------

    [Fact]
    public void DoomedTraveler_DiesTrigger_IsActiveInBattlefieldAndGraveyardZones()
    {
        // The dies trigger must include Graveyard in its active zones because
        // ZoneService stamps card.Zone = Graveyard BEFORE publishing the
        // CardMovedEvent (CR 603.6c — same posture as Aven Fisher / Wurmcoil).
        var card = DoomedTravelerFactory.Create(_alice);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "dies trigger is active while on the battlefield");
        trigger.ActiveZones.Should().Contain(ZoneType.Graveyard,
            "dies trigger must remain observable after ZoneService stamps zone (CR 603.6c)");
    }

    // ------------------------------------------------------------------
    // Live dies trigger — creates 1/1 white Spirit token with Flying
    // ------------------------------------------------------------------

    [Fact]
    public void DoomedTraveler_Dies_CreatesOneSpiritTokenWithFlying()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var traveler = DoomedTravelerFactory.Create(_alice, triggers, zones);
        traveler.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(traveler);

        // Kill it: Battlefield → Graveyard via ZoneService.
        zones.MoveCard(traveler, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        triggers.PendingCount.Should().Be(1,
            "the dies trigger must queue on Battlefield → Graveyard");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // The Spirit token should now be on Alice's battlefield.
        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        tokens.Should().HaveCount(1,
            "exactly one Spirit token is created when Doomed Traveler dies");

        var token = tokens.Single();
        token.Name.Should().Be("Spirit");
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        token.Controller.Should().BeSameAs(_alice,
            "the token is under the controller's control (CR 111.4)");
        token.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "the Spirit token has Flying (CR 702.9)");
        token.TokenColorsOverride.Should().NotBeNull();
        token.TokenColorsOverride!.Should().Contain(ManaColor.White,
            "the Spirit token is white (CR 105 / CR 111.4)");

        // Doomed Traveler itself is in the graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(traveler,
            "Doomed Traveler is in the graveyard after dying");
    }

    // ------------------------------------------------------------------
    // No trigger on non-death zone changes
    // ------------------------------------------------------------------

    [Fact]
    public void DoomedTraveler_BouncedToHand_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var traveler = DoomedTravelerFactory.Create(_alice, triggers, zones);
        traveler.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(traveler);

        // Bounce: Battlefield → Hand (not death).
        zones.MoveCard(traveler, ZoneType.Battlefield, ZoneType.Hand, _alice);

        triggers.PendingCount.Should().Be(0,
            "dies trigger must not fire on a bounce (Battlefield → Hand is not death per CR 700.4)");
    }

    [Fact]
    public void DoomedTraveler_ExiledFromBattlefield_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var traveler = DoomedTravelerFactory.Create(_alice, triggers, zones);
        traveler.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(traveler);

        // Exile: Battlefield → Exile (skips graveyard, not death per CR 700.4).
        zones.MoveCard(traveler, ZoneType.Battlefield, ZoneType.Exile, _alice);

        triggers.PendingCount.Should().Be(0,
            "dies trigger must not fire on Battlefield → Exile (not death per CR 700.4)");
    }
}
