using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Simulation;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Zones;

/// <summary>
/// CR 406.3 — a card exiled face down can be looked at only by players an
/// effect allows. The engine carries that as a flag on the card, set by the
/// exiling effect, cleared when the card leaves exile (CR 400.7), and kept by
/// a simulation clone.
/// </summary>
public class FaceDownExileTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Card InLibrary(string name)
    {
        var c = new Artifact(name, "{0}");
        c.SetOwner(_alice);
        c.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(c);
        return c;
    }

    [Fact]
    public void ExileFaceDown_FlagsTheCard_ForItsLookersOnly_AndTheMoveEventSeesIt()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var card = InLibrary("Secret");
        bool flaggedAtPublish = false;
        bus.Subscribe<CardMovedEvent>(e => flaggedAtPublish = ((Card)e.Card).IsExiledFaceDown);

        zones.ExileFaceDown(card, ZoneType.Library, new[] { _alice }, _alice);

        card.Zone.Should().Be(ZoneType.Exile);
        card.IsExiledFaceDown.Should().BeTrue();
        flaggedAtPublish.Should().BeTrue("payload builders read the flag while the move event is published");
        card.MayLookAtFaceDownExile(_alice).Should().BeTrue();
        card.MayLookAtFaceDownExile(_bob).Should().BeFalse();
        card.MayLookAtFaceDownExile(null).Should().BeTrue("the all-revealed (spectator/debug) view");
    }

    [Fact]
    public void LeavingExile_ClearsTheFlag_AndTheMoveEventRemembersWhoMayLook()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var card = InLibrary("Secret");
        zones.ExileFaceDown(card, ZoneType.Library, Array.Empty<Player>(), _alice);
        CardMovedEvent? leaving = null;
        bus.Subscribe<CardMovedEvent>(e => leaving = e);

        zones.MoveCard(card, ZoneType.Exile, ZoneType.Hand, _alice);

        card.IsExiledFaceDown.Should().BeFalse("a card that leaves exile is a new object (CR 400.7)");
        card.FaceDownExileLookers.Should().BeEmpty();
        leaving!.LeftFaceDownExile.Should().BeTrue();
    }

    [Fact]
    public void AnOrdinaryExile_IsFaceUp()
    {
        var zones = new ZoneService(new EventBus());
        var card = InLibrary("Open");
        zones.MoveCard(card, ZoneType.Library, ZoneType.Exile, _alice);
        card.IsExiledFaceDown.Should().BeFalse();
        card.MayLookAtFaceDownExile(_bob).Should().BeTrue();
    }

    [Fact]
    public void ASimulationClone_KeepsTheFlag_WithItsLookersRemapped()
    {
        var zones = new ZoneService(new EventBus());
        var card = InLibrary("Secret");
        zones.ExileFaceDown(card, ZoneType.Library, new[] { _alice }, _alice);

        var clone = GameStateCloner.Clone(new[] { _alice, _bob });
        var cloned = (Card)clone.CardMap[card.InstanceId];

        cloned.IsExiledFaceDown.Should().BeTrue();
        cloned.MayLookAtFaceDownExile(clone.PlayerMap[_alice]).Should().BeTrue();
        cloned.MayLookAtFaceDownExile(clone.PlayerMap[_bob]).Should().BeFalse();
    }

    [Fact]
    public void BomatCourier_ExilesFaceDown_AndNobodyMayLook()
    {
        // "exile the top card of your library face down. (You can't look at it.)"
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus, new ReplacementBus());
        var triggers = new TriggerManager(stack, bus);
        var courier = BomatCourierFactory.Create(_alice, bus, triggers, zones);
        courier.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(courier);
        triggers.BindCard(courier);
        var top = InLibrary("Secret");

        bus.Publish(new CreatureAttacksEvent(courier, _bob));
        triggers.PutPendingTriggersOnStack(_alice);
        while (!stack.IsEmpty) stack.Pop()!.Resolve();

        top.IsExiledFaceDown.Should().BeTrue();
        top.MayLookAtFaceDownExile(_alice).Should().BeFalse("(You can't look at it.)");
        top.MayLookAtFaceDownExile(_bob).Should().BeFalse();
    }

    [Fact]
    public void VivienMinus2_ExilesFaceDown_AndItsControllerMayLook()
    {
        // "Exile one face down ... For as long as it remains exiled, you may
        // cast it if it's a creature spell": casting needs looking (CR 406.3).
        var vivien = VivienChampionOfTheWildsFactory.Create(_alice);
        vivien.SetController(_alice);
        var creature = new Creature("Goblin Bear", "{2}{R}", 2, 2);
        creature.SetOwner(_alice);
        _alice.Zones.Library.AddCard(creature);
        creature.SetZone(ZoneType.Library);

        vivien.Abilities.OfType<LoyaltyAbility>().First(a => a.LoyaltyChange == -2).Activate();

        creature.Zone.Should().Be(ZoneType.Exile);
        creature.IsExiledFaceDown.Should().BeTrue();
        creature.MayLookAtFaceDownExile(_alice).Should().BeTrue();
        creature.MayLookAtFaceDownExile(_bob).Should().BeFalse();
    }

    [Fact]
    public void Necropotence_ExilesFaceDown_AndNobodyMayLook()
    {
        // "Pay 1 life: Exile the top card of your library face down."
        var necro = NecropotenceFactory.Create(_alice);
        necro.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(necro);
        var top = InLibrary("Secret");

        var ability = necro.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        top.Zone.Should().Be(ZoneType.Exile);
        top.IsExiledFaceDown.Should().BeTrue();
        top.MayLookAtFaceDownExile(_alice).Should().BeFalse();
    }
}
