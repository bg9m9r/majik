using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BomatCourierFactory"/>.
///
/// Card: Bomat Courier (Kaladesh, {1}). Artifact Creature — Construct. 1/1.
///
/// Oracle text (Scryfall-verified):
/// <list type="number">
///   <item>Haste</item>
///   <item>"Whenever this creature attacks, exile the top card of your
///       library face down. (You can't look at it.)"</item>
///   <item>"{R}, Discard your hand, Sacrifice this creature: Put all cards
///       exiled with this creature into their owners' hands."</item>
/// </list>
///
/// Mirrors <see cref="EmperorOfBonesFactory"/>'s "exiled-with-this-creature"
/// ledger pattern + <see cref="BedlamRevelerFactory"/>'s discard-hand /
/// draw-into-hand handling.
/// </summary>
[Trait("Color", "C")]
public class BomatCourierFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private record CourierRig(
        Creature Courier,
        TriggerManager Triggers,
        Majik.Core.Stack.Stack Stack,
        ZoneService Zones,
        EventBus Bus);

    private CourierRig MakeCourier()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var reps = new ReplacementBus();
        var zones = new ZoneService(bus, reps);
        var triggers = new TriggerManager(stack, bus);
        var courier = BomatCourierFactory.Create(_alice, bus, triggers, zones);
        courier.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(courier);
        triggers.BindCard(courier);
        return new CourierRig(courier, triggers, stack, zones, bus);
    }

    private static Card TopLibraryCard(string name, Player owner)
    {
        var c = new Artifact(name, "{0}") { Owner = owner, Controller = owner };
        c.SetZone(ZoneType.Library);
        owner.Zones.Library.AddCard(c);
        return c;
    }

    [Fact]
    public void BomatCourier_Identity()
    {
        var courier = BomatCourierFactory.Create(_alice);

        courier.Name.Should().Be("Bomat Courier");
        courier.ManaCost.Should().Be("{1}");
        courier.BasePower.Should().Be(1);
        courier.BaseToughness.Should().Be(1);
        courier.HasType(CardType.Artifact).Should().BeTrue();
        courier.HasType(CardType.Creature).Should().BeTrue();
        courier.Subtypes.Should().Contain(CardSubtype.Construct);
        courier.Owner.Should().BeSameAs(_alice);
        courier.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void BomatCourier_HasHasteMarker()
    {
        var courier = BomatCourierFactory.Create(_alice);

        courier.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Haste");
    }

    [Fact]
    public void BomatCourier_AbilityShape()
    {
        var courier = BomatCourierFactory.Create(_alice);

        // One attacks trigger.
        courier.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);

        // One activated ability ({R}, Discard hand, Sacrifice).
        courier.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void AttacksTrigger_ExilesTopOfLibrary_FaceDown_AndTracksIt()
    {
        var rig = MakeCourier();

        var top = TopLibraryCard("Secret Card", _alice);

        rig.Bus.Publish(new CreatureAttacksEvent(rig.Courier, _bob));

        rig.Triggers.PendingCount.Should().BeGreaterThan(0,
            "the attacks trigger should queue when the Courier attacks");

        rig.Triggers.PutPendingTriggersOnStack(_alice);
        while (!rig.Stack.IsEmpty) rig.Stack.Pop()!.Resolve();

        top.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(top);

        var state = BomatCourierFactory.GetState(rig.Courier);
        state.Should().NotBeNull();
        state!.ExiledWith.Should().Contain(top);
    }

    [Fact]
    public void AttacksTrigger_DoesNotFire_ForADifferentAttacker()
    {
        var rig = MakeCourier();
        var top = TopLibraryCard("Secret Card", _alice);

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _alice, Controller = _alice };
        other.SetZone(ZoneType.Battlefield);

        rig.Bus.Publish(new CreatureAttacksEvent(other, _bob));
        rig.Triggers.PutPendingTriggersOnStack(_alice);

        rig.Stack.IsEmpty.Should().BeTrue();
        top.Zone.Should().Be(ZoneType.Library);
        BomatCourierFactory.GetState(rig.Courier)!.ExiledWith.Should().BeEmpty();
    }

    [Fact]
    public void ActivatedAbility_DiscardsHand_Sacrifices_AndReturnsExiledCards()
    {
        var rig = MakeCourier();

        // Two cards exiled with the Courier across two attacks.
        var c1 = new Artifact("Exiled One", "{0}") { Owner = _alice, Controller = _alice };
        c1.SetZone(ZoneType.Exile);
        _alice.Zones.Exile.AddCard(c1);
        var c2 = new Artifact("Exiled Two", "{0}") { Owner = _alice, Controller = _alice };
        c2.SetZone(ZoneType.Exile);
        _alice.Zones.Exile.AddCard(c2);
        BomatCourierFactory.GetState(rig.Courier)!.AddExiledWith(c1);
        BomatCourierFactory.GetState(rig.Courier)!.AddExiledWith(c2);

        // A card in hand that the discard cost should toss.
        var handCard = new Artifact("Hand Card", "{0}") { Owner = _alice, Controller = _alice };
        handCard.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(handCard);

        var ability = rig.Courier.Abilities.OfType<ActivatedAbility>().Single();

        // Pay the non-mana costs declared on the ability (discard hand +
        // sacrifice). Mana cost ({R}) is the engine's job at activation time.
        foreach (var cost in ability.Costs)
        {
            if (cost is ManaCostCost) continue;
            cost.Pay(_alice);
        }

        // Resolve the effect — return all exiled cards to owners' hands.
        foreach (var eff in ability.Effects) eff.Execute();

        // Hand was discarded.
        _alice.Zones.Hand.GetCards().Should().NotContain(handCard);
        handCard.Zone.Should().Be(ZoneType.Graveyard);

        // Courier sacrificed.
        rig.Courier.Zone.Should().Be(ZoneType.Graveyard);

        // Exiled cards returned to owner's hand.
        c1.Zone.Should().Be(ZoneType.Hand);
        c2.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(c1);
        _alice.Zones.Hand.GetCards().Should().Contain(c2);

        // Ledger consumed.
        BomatCourierFactory.GetState(rig.Courier)!.ExiledWith.Should().BeEmpty();
    }

    [Fact]
    public void ExiledCards_ReturnToTheirOwnersHands_NotTheControllers()
    {
        var rig = MakeCourier();

        // A card owned by Bob exiled with Alice's Courier returns to Bob.
        var bobsCard = new Artifact("Bob's Thing", "{0}") { Owner = _bob, Controller = _bob };
        bobsCard.SetZone(ZoneType.Exile);
        _bob.Zones.Exile.AddCard(bobsCard);
        BomatCourierFactory.GetState(rig.Courier)!.AddExiledWith(bobsCard);

        var ability = rig.Courier.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in ability.Effects) eff.Execute();

        bobsCard.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(bobsCard,
            because: "the spell says 'into their owners' hands' (CR 109.5)");
        _alice.Zones.Hand.GetCards().Should().NotContain(bobsCard);
    }
}
