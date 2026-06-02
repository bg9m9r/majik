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
/// Tests for <see cref="DoomedDissenterFactory"/> — Creature — Human {1}{B}
/// 1/1 with a dies-triggered ability that creates a 2/2 black Zombie creature
/// token under the controller's control.
///
/// Oracle text: "When this creature dies, create a 2/2 black Zombie creature token."
///
/// Covers:
/// - Card identity (name, cost {1}{B}, mana value 2, type Creature,
///   subtype Human, P/T 1/1, owner/controller, colour black).
/// - No keyword abilities on the card itself.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Exactly one <see cref="TriggeredAbility"/> attached, active in
///   Battlefield + Graveyard zones.
/// - Live dies trigger: fires on Battlefield → Graveyard and places exactly
///   one 2/2 black Zombie creature token on the controller's battlefield
///   (CR 603.6c / 700.4 / CR 111 / CR 111.4).
/// - No trigger on non-death zone changes (bounce, exile).
/// </summary>
[Trait("Color", "B")]
public class DoomedDissenterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ------------------------------------------------------------------
    // Shape / identity
    // ------------------------------------------------------------------

    [Fact]
    public void DoomedDissenter_IsCorrect_Identity()
    {
        var card = DoomedDissenterFactory.Create(_alice);

        card.Name.Should().Be("Doomed Dissenter");
        card.ManaCost.Should().Be("{1}{B}");
        card.ManaCostValue.TotalValue.Should().Be(2,
            "mana value of {1}{B} is 2 (CR 202.3)");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DoomedDissenter_IsBlack()
    {
        var card = DoomedDissenterFactory.Create(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColor.Black,
            "{1}{B} mana cost makes the card black (CR 105.2)");
    }

    [Fact]
    public void DoomedDissenter_HasNoKeywordAbility()
    {
        // Doomed Dissenter itself has no keywords — it's a 1/1 with
        // a triggered ability only.
        var card = DoomedDissenterFactory.Create(_alice);
        card.Abilities.OfType<KeywordAbility>()
            .Should().BeEmpty(
                "Doomed Dissenter has no keyword abilities printed on it");
    }

    // ------------------------------------------------------------------
    // Dispatcher
    // ------------------------------------------------------------------
    // ------------------------------------------------------------------
    // Triggered ability — active zones
    // ------------------------------------------------------------------

    [Fact]
    public void DoomedDissenter_DiesTrigger_IsActiveInBattlefieldAndGraveyardZones()
    {
        // The dies trigger must include Graveyard in its active zones because
        // ZoneService stamps card.Zone = Graveyard BEFORE publishing the
        // CardMovedEvent (CR 603.6c — same posture as Aven Fisher / Wurmcoil).
        var card = DoomedDissenterFactory.Create(_alice);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "dies trigger is active while on the battlefield");
        trigger.ActiveZones.Should().Contain(ZoneType.Graveyard,
            "dies trigger must remain observable after ZoneService stamps zone (CR 603.6c)");
    }

    // ------------------------------------------------------------------
    // Live dies trigger — creates 2/2 black Zombie token
    // ------------------------------------------------------------------

    [Fact]
    public void DoomedDissenter_Dies_CreatesOneZombieToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var dissenter = DoomedDissenterFactory.Create(_alice, triggers, zones);
        dissenter.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(dissenter);

        // Kill it: Battlefield → Graveyard via ZoneService.
        zones.MoveCard(dissenter, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        triggers.PendingCount.Should().Be(1,
            "the dies trigger must queue on Battlefield → Graveyard");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // The Zombie token should now be on Alice's battlefield.
        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        tokens.Should().HaveCount(1,
            "exactly one Zombie token is created when Doomed Dissenter dies");

        var token = tokens.Single();
        token.Name.Should().Be("Zombie");
        token.BasePower.Should().Be(2);
        token.BaseToughness.Should().Be(2);
        token.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        token.Controller.Should().BeSameAs(_alice,
            "the token is under the controller's control (CR 111.4)");
        token.Abilities.OfType<KeywordAbility>()
            .Should().BeEmpty(
                "the Zombie token has no keyword abilities");
        token.TokenColorsOverride.Should().NotBeNull();
        token.TokenColorsOverride!.Should().Contain(ManaColor.Black,
            "the Zombie token is black (CR 105 / CR 111.4)");

        // Doomed Dissenter itself is in the graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(dissenter,
            "Doomed Dissenter is in the graveyard after dying");
    }

    // ------------------------------------------------------------------
    // No trigger on non-death zone changes
    // ------------------------------------------------------------------

    [Fact]
    public void DoomedDissenter_BouncedToHand_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var dissenter = DoomedDissenterFactory.Create(_alice, triggers, zones);
        dissenter.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(dissenter);

        // Bounce: Battlefield → Hand (not death).
        zones.MoveCard(dissenter, ZoneType.Battlefield, ZoneType.Hand, _alice);

        triggers.PendingCount.Should().Be(0,
            "dies trigger must not fire on a bounce (Battlefield → Hand is not death per CR 700.4)");
    }

    [Fact]
    public void DoomedDissenter_ExiledFromBattlefield_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var dissenter = DoomedDissenterFactory.Create(_alice, triggers, zones);
        dissenter.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(dissenter);

        // Exile: Battlefield → Exile (skips graveyard, not death per CR 700.4).
        zones.MoveCard(dissenter, ZoneType.Battlefield, ZoneType.Exile, _alice);

        triggers.PendingCount.Should().Be(0,
            "dies trigger must not fire on Battlefield → Exile (not death per CR 700.4)");
    }
}
