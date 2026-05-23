using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Bridge from Below (Future Sight, {B}, Enchantment).
///
/// Coverage:
/// - Identity (name / type / mana cost) + NamedCardFactory dispatch.
/// - Both triggered abilities attached with activeZones = {Graveyard}
///   (CR 603.6d).
/// - Zombie-token trigger fires when a nontoken creature moves from
///   battlefield → controller's graveyard; effect creates a 2/2
///   Zombie token on Bridge's controller's battlefield.
/// - Trigger condition rejects token creatures + creatures dying to
///   opponent's graveyard (those route to the self-exile trigger).
/// - Self-exile trigger fires when a creature moves from battlefield →
///   opponent's graveyard; effect moves Bridge graveyard → exile.
/// - Intervening-if gates the Zombie trigger on Bridge being in
///   controller's graveyard (CR 603.4).
/// </summary>
public class BridgeFromBelowFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeCreature(string name, Player owner, bool isToken = false)
    {
        var c = new Creature(name, manaCost: "1G", power: 2, toughness: 2);
        c.SetOwner(owner);
        c.SetController(owner);
        if (isToken) c.MarkAsToken();
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Bridge_Identity()
    {
        var c = BridgeFromBelowFactory.Create(_alice);

        c.Name.Should().Be("Bridge from Below");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.ManaCost.Should().Be("{B}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // Both triggered abilities are attached on the card.
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "Bridge from Below carries the Zombie-token trigger and the self-exile trigger");
    }

    [Fact]
    public void Bridge_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Bridge from Below", _alice);

        c.Should().BeOfType<Enchantment>();
        c.Name.Should().Be("Bridge from Below");
        c.HasType(CardType.Enchantment).Should().BeTrue();
    }

    [Fact]
    public void Bridge_TriggersAreActiveInGraveyardZoneOnly()
    {
        var c = BridgeFromBelowFactory.Create(_alice);
        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();

        triggers.Should().AllSatisfy(t =>
        {
            t.ActiveZones.Should().BeEquivalentTo(new[] { ZoneType.Graveyard },
                "CR 603.6d — Bridge's abilities function while it is in its owner's graveyard");
        });
    }

    // -----------------------------------------------------------------------
    // Zombie-token trigger — fires on nontoken creature → controller's graveyard
    // -----------------------------------------------------------------------

    [Fact]
    public void Bridge_NontokenCreatureDies_ToControllersGraveyard_FiresZombieTrigger()
    {
        var bridge = BridgeFromBelowFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(bridge);
        bridge.SetZone(ZoneType.Graveyard);

        var creature = MakeCreature("Bear", _alice);
        // Simulate the dies move: Battlefield → Graveyard.
        creature.SetZone(ZoneType.Graveyard);

        // Pick the Zombie-token trigger (the one with an intervening-if;
        // the self-exile trigger has no intervening-if).
        var zombieTrigger = bridge.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf != null);

        var movedEvent = new CardMovedEvent(
            creature, ZoneType.Battlefield, ZoneType.Graveyard);
        zombieTrigger.IsTriggered(movedEvent).Should().BeTrue(
            "nontoken creature → Bridge controller's graveyard satisfies the trigger condition");

        // Run the effect — should create a 2/2 Zombie token on Alice's battlefield.
        foreach (var fx in zombieTrigger.Effects) fx.Execute();

        var zombies = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Zombie")
            .ToList();
        zombies.Should().HaveCount(1);
        zombies[0].BasePower.Should().Be(2);
        zombies[0].BaseToughness.Should().Be(2);
        zombies[0].HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        zombies[0].Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Bridge_TokenCreatureDying_DoesNotFireZombieTrigger()
    {
        var bridge = BridgeFromBelowFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(bridge);
        bridge.SetZone(ZoneType.Graveyard);

        // Token creature dies — printed text gates on "nontoken creature".
        var tokenCreature = MakeCreature("Saproling Token", _alice, isToken: true);

        var zombieTrigger = bridge.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf != null);

        var movedEvent = new CardMovedEvent(
            tokenCreature, ZoneType.Battlefield, ZoneType.Graveyard);
        zombieTrigger.IsTriggered(movedEvent).Should().BeFalse(
            "token creature deaths must not fire the Zombie trigger (CR 111.3 — \"nontoken creature\")");
    }

    [Fact]
    public void Bridge_InterveningIf_GatesOnBridgeBeingInControllersGraveyard()
    {
        var bridge = BridgeFromBelowFactory.Create(_alice);

        // Bridge is NOT in the graveyard — intervening-if must reject.
        bridge.Zone.Should().NotBe(ZoneType.Graveyard);
        var zombieTrigger = bridge.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf != null);
        zombieTrigger.CanBePutOnStack().Should().BeFalse(
            "CR 603.4 — intervening-if checks 'Bridge is in your graveyard' at trigger evaluation");

        // Place Bridge in graveyard — intervening-if now passes.
        _alice.Zones.Graveyard.AddCard(bridge);
        bridge.SetZone(ZoneType.Graveyard);
        zombieTrigger.CanBePutOnStack().Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Self-exile trigger — fires on creature → opponent's graveyard
    // -----------------------------------------------------------------------

    [Fact]
    public void Bridge_CreatureToOpponentsGraveyard_FiresSelfExileTrigger_AndExilesBridge()
    {
        var bridge = BridgeFromBelowFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(bridge);
        bridge.SetZone(ZoneType.Graveyard);

        // Bob's creature dies into Bob's graveyard.
        var bobsCreature = MakeCreature("Wolf", _bob);
        bobsCreature.SetZone(ZoneType.Graveyard);

        // Pick the self-exile trigger (no intervening-if).
        var exileTrigger = bridge.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf == null);

        var movedEvent = new CardMovedEvent(
            bobsCreature, ZoneType.Battlefield, ZoneType.Graveyard);
        exileTrigger.IsTriggered(movedEvent).Should().BeTrue(
            "creature → opponent's graveyard satisfies the self-exile trigger condition");

        // Resolve the effect — Bridge should move graveyard → exile.
        foreach (var fx in exileTrigger.Effects) fx.Execute();

        _alice.Zones.Graveyard.GetCards().Should().NotContain(bridge);
        _alice.Zones.Exile.GetCards().Should().Contain(bridge);
        bridge.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Bridge_OwnCreatureDying_DoesNotFireSelfExileTrigger()
    {
        var bridge = BridgeFromBelowFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(bridge);
        bridge.SetZone(ZoneType.Graveyard);

        // Alice's own creature dies — self-exile trigger should NOT fire
        // (that trigger is gated on opponent's graveyard).
        var alicesCreature = MakeCreature("Bear", _alice);

        var exileTrigger = bridge.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf == null);

        var movedEvent = new CardMovedEvent(
            alicesCreature, ZoneType.Battlefield, ZoneType.Graveyard);
        exileTrigger.IsTriggered(movedEvent).Should().BeFalse(
            "creature → controller's own graveyard does not exile Bridge — the Zombie trigger fires instead");
    }

    [Fact]
    public void Bridge_NonCreatureLeavingBattlefield_DoesNotFireEitherTrigger()
    {
        var bridge = BridgeFromBelowFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(bridge);
        bridge.SetZone(ZoneType.Graveyard);

        // A noncreature artifact dying — neither trigger should match
        // (both gated on Creature card type).
        var artifact = new Artifact("Sol Ring", "1");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);

        var movedEvent = new CardMovedEvent(
            artifact, ZoneType.Battlefield, ZoneType.Graveyard);

        foreach (var trigger in bridge.Abilities.OfType<TriggeredAbility>())
        {
            trigger.IsTriggered(movedEvent).Should().BeFalse(
                "Bridge's triggers gate on Creature card type — non-creature deaths are ignored");
        }
    }
}
