using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Events;

/// <summary>
/// Tests for the last-known-information controller snapshot on
/// <see cref="CardMovedEvent"/> (CR 603.10).
///
/// When a permanent leaves the battlefield the engine resets
/// <see cref="ICard.Controller"/> back to the owner (CR 110.2 — a card not on
/// the battlefield or stack is controlled by its owner). Dies /
/// leaves-the-battlefield triggers — and any trigger that branches on "a
/// creature you control" vs "a creature an opponent controls" — must read the
/// dying object's controller from last-known information at the instant of
/// death, NOT from the post-reset live card. <see cref="CardMovedEvent.LkiController"/>
/// carries that snapshot.
///
/// The aristocrats death-drain cycle and The Meathook Massacre's controller
/// branches all key off this snapshot: a creature you control via a
/// control-change effect (Act of Treason / Threaten) that dies must trigger
/// YOUR "a creature you control dies" payoffs, not its owner's.
/// </summary>
public class CardMovedEventLkiControllerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Snapshot capture through the production ZoneService path
    // -----------------------------------------------------------------------

    [Fact]
    public void ZoneService_BattlefieldToGraveyard_SnapshotsPreResetController()
    {
        // Bob OWNS the creature but Alice CONTROLS it (Act of Treason posture).
        var stolen = new Creature("Goblin", "{R}", 2, 2);
        stolen.SetOwner(_bob);
        stolen.SetController(_alice);
        _bob.Zones.Battlefield.AddCard(stolen);
        stolen.SetZone(ZoneType.Battlefield);

        var bus = new EventBus();
        CardMovedEvent? captured = null;
        bus.Subscribe<CardMovedEvent>(e => captured = e);

        var zoneService = new ZoneService(bus);

        // The creature dies — Battlefield → Graveyard. ZoneService resets the
        // live Controller back to the owner (Bob) BEFORE publishing the event.
        zoneService.MoveCard(stolen, ZoneType.Battlefield, ZoneType.Graveyard);

        // Post-move the LIVE controller is reset to the owner (CR 110.2)...
        stolen.Controller.Should().BeSameAs(_bob,
            "CR 110.2 — a card not on the battlefield/stack is controlled by its owner");

        // ...but the LKI snapshot preserves the controller at the moment of
        // death (CR 603.10).
        captured.Should().NotBeNull();
        captured!.LkiController.Should().BeSameAs(_alice,
            "CR 603.10 — dies triggers read the controller from LKI at the instant of death");
    }

    [Fact]
    public void DirectConstruction_DefaultsLkiToLiveController()
    {
        // The shape / dispatcher test path constructs CardMovedEvent directly
        // before any reset, so LkiController defaults to the live controller.
        var bear = new Creature("Runeclaw Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);

        var e = new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Graveyard);

        e.LkiController.Should().BeSameAs(_bob);
    }

    // -----------------------------------------------------------------------
    // Aristocrats branch on LKI, not the post-reset live controller
    // -----------------------------------------------------------------------

    [Fact]
    public void CruelCelebrant_StolenCreatureDies_FiresForControllerAtDeath()
    {
        // Alice has Cruel Celebrant. A creature Bob owns but Alice controls
        // (Act of Treason) dies. "A creature you control dies" must fire for
        // ALICE because she controlled it at the instant of death (CR 603.10),
        // even though its live controller has reset to Bob.
        var celebrant = CruelCelebrantFactory.Create(_alice, eventBus: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(celebrant);
        celebrant.SetZone(ZoneType.Battlefield);

        var stolen = new Creature("Goblin", "{R}", 2, 2);
        stolen.SetOwner(_bob);
        stolen.SetController(_bob); // live controller already reset to owner

        // Death event whose LKI controller is Alice (she controlled it at death).
        var diesEvent = new CardMovedEvent(
            stolen, ZoneType.Battlefield, ZoneType.Graveyard, lkiController: _alice);

        var diesTrigger = celebrant.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(diesEvent));

        Majik.Core.Tests.Helpers.ContextResolve.Resolve(diesTrigger, _alice, _alice, _bob);

        _alice.LifeTotal.Should().Be(21, "controller gains 1 life");
        _bob.LifeTotal.Should().Be(19, "each opponent loses 1 life");
    }

    [Fact]
    public void CruelCelebrant_OwnCreatureStolenByOpponentDies_DoesNotFire()
    {
        // Mirror case: a creature Alice owns but BOB controls (Bob stole it)
        // dies. Alice's Cruel Celebrant must NOT fire — Alice did not control
        // it at the instant of death, even though its live controller resets
        // back to Alice (the owner).
        var celebrant = CruelCelebrantFactory.Create(_alice, eventBus: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(celebrant);
        celebrant.SetZone(ZoneType.Battlefield);

        var aliceOwned = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceOwned.SetOwner(_alice);
        aliceOwned.SetController(_alice); // live controller reset to owner = Alice

        // LKI controller is Bob — he controlled it at death.
        var diesEvent = new CardMovedEvent(
            aliceOwned, ZoneType.Battlefield, ZoneType.Graveyard, lkiController: _bob);

        var fired = celebrant.Abilities.OfType<TriggeredAbility>()
            .Any(t => t.IsTriggered(diesEvent));

        fired.Should().BeFalse(
            "CR 603.10 — Alice did not control the creature at the instant of death, so her 'a creature you control dies' trigger does not fire");
    }

    [Fact]
    public void Meathook_StolenCreatureDies_TreatedAsOwnCreatureForControllerAtDeath()
    {
        // Alice has The Meathook Massacre. A creature Bob owns but Alice
        // controls dies. "Whenever a creature you control dies, each opponent
        // loses 1 life" must fire (Alice controlled it at death) — the
        // opponent-creature branch ("an opponent controls") must NOT.
        var massacre = TheMeathookMassacreFactory.Create(_alice, eventBus: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(massacre);
        massacre.SetZone(ZoneType.Battlefield);

        var stolen = new Creature("Goblin", "{R}", 2, 2);
        stolen.SetOwner(_bob);
        stolen.SetController(_bob);

        var diesEvent = new CardMovedEvent(
            stolen, ZoneType.Battlefield, ZoneType.Graveyard, lkiController: _alice);

        var firedTriggers = massacre.Abilities.OfType<TriggeredAbility>()
            .Where(t => t.IsTriggered(diesEvent))
            .ToList();

        firedTriggers.Should().HaveCount(1,
            "only the 'a creature you control dies' branch fires for a creature Alice controlled at death");

        Majik.Core.Tests.Helpers.ContextResolve.Resolve(firedTriggers[0], _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(19, "each opponent loses 1 life when an own-creature dies");
        _alice.LifeTotal.Should().Be(20, "Alice (controller) is not drained, and does not gain from the opp branch");
    }

    [Fact]
    public void Meathook_OwnCreatureStolenByOpponentDies_FiresOpponentBranch()
    {
        // A creature Alice owns but Bob controls dies. From Alice's Massacre's
        // perspective this is "a creature an opponent controls" → Alice gains 1
        // life. The own-creature branch must NOT fire.
        var massacre = TheMeathookMassacreFactory.Create(_alice, eventBus: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(massacre);
        massacre.SetZone(ZoneType.Battlefield);

        var aliceOwned = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceOwned.SetOwner(_alice);
        aliceOwned.SetController(_alice); // live controller resets to owner = Alice

        var diesEvent = new CardMovedEvent(
            aliceOwned, ZoneType.Battlefield, ZoneType.Graveyard, lkiController: _bob);

        var firedTriggers = massacre.Abilities.OfType<TriggeredAbility>()
            .Where(t => t.IsTriggered(diesEvent))
            .ToList();

        firedTriggers.Should().HaveCount(1,
            "only the opponent-creature branch fires for a creature Bob controlled at death");

        foreach (var e in firedTriggers[0].Effects) e.Execute();

        _alice.LifeTotal.Should().Be(21, "controller gains 1 life when an opponent-creature dies");
    }
}
