using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SwordOfTheMeekFactory"/>.
///
/// Card: Sword of the Meek — Artifact — Equipment {2} (Future Sight).
///   "Equipped creature gets +1/+2."
///   "Equip {2}"
///   "Whenever a 1/1 creature you control enters, you may return this card
///    from your graveyard to the battlefield, then attach it to that
///    creature."
///
/// Static +1/+2 boost + Equip {2} mirror <see cref="BonesplitterFactory"/> /
/// <see cref="SwordOfFireAndIceFactory"/>; the graveyard-resident
/// return-and-attach trigger mirrors <see cref="BloodghastFactory"/>'s
/// landfall return with an added attach step (CR 701.3).
/// </summary>
[Trait("Color", "C")]
public class SwordOfTheMeekFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_ArtifactEquipment_CostTwo()
    {
        var c = SwordOfTheMeekFactory.Create(_alice);

        c.Name.Should().Be("Sword of the Meek");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Sword of the Meek is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EquipAbility_HasGenericTwoCost()
    {
        var c = SwordOfTheMeekFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2,
            "Equip {2} is the printed activation cost");
    }

    // -----------------------------------------------------------------------
    // Static +1/+2 boost — CR 613 Layer 7c
    // -----------------------------------------------------------------------

    [Fact]
    public void Equipped_Bear_Becomes_3_4()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var sword = SwordOfTheMeekFactory.Create(_alice, svc, zoneService: null, triggers: null, agent: null);
        sword.Zone = ZoneType.Battlefield;

        sword.AttachTo(bear);

        bear.GetPower().Should().Be(3, "+1 power from Sword of the Meek");
        bear.GetToughness().Should().Be(4, "+2 toughness from Sword of the Meek");
    }

    [Fact]
    public void Detach_RestoresPT()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var sword = SwordOfTheMeekFactory.Create(_alice, svc, zoneService: null, triggers: null, agent: null);
        sword.Zone = ZoneType.Battlefield;
        sword.AttachTo(bear);

        bear.GetPower().Should().Be(3);

        sword.Unattach();

        bear.GetPower().Should().Be(2,
            "boost lapses on detach — AttachedTo gate flips IsActive to false");
        bear.GetToughness().Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Graveyard-resident return-and-attach trigger — CR 603.6d / 701.3
    // -----------------------------------------------------------------------

    [Fact]
    public void OneOneEnters_ReturnsSwordFromGraveyard_AndAttaches()
    {
        var bus = new EventBus();
        var zoneService = new ZoneService(bus);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var svc = new ContinuousEffectsService();

        var sword = SwordOfTheMeekFactory.Create(_alice, svc, zoneService, triggers, agent: null);
        _alice.Zones.Graveyard.AddCard(sword);
        sword.SetZone(ZoneType.Graveyard);

        // A 1/1 creature Alice controls enters.
        var token = new Creature("Servo", "", 1, 1)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        _alice.Zones.Battlefield.AddCard(token);

        var returnTrigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        var etbEvent = new CardMovedEvent(token, ZoneType.Stack, ZoneType.Battlefield);

        returnTrigger.IsTriggered(etbEvent).Should().BeTrue(
            "a 1/1 creature Alice controls entered the battlefield");

        foreach (var effect in returnTrigger.Effects) effect.Execute();

        sword.Zone.Should().Be(ZoneType.Battlefield,
            "CR 603.6d — the Sword returns from graveyard to the battlefield");
        _alice.Zones.Battlefield.GetCards().Should().Contain(sword);
        sword.AttachedTo.Should().BeSameAs(token,
            "CR 701.3 — 'then attach it to that creature'");
        token.GetPower().Should().Be(2, "+1/+2 boost once attached");
        token.GetToughness().Should().Be(3);
    }

    [Fact]
    public void NonOneOneCreatureEntering_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var sword = SwordOfTheMeekFactory.Create(_alice, continuousEffects: null, zoneService: null, triggers, agent: null);
        _alice.Zones.Graveyard.AddCard(sword);
        sword.SetZone(ZoneType.Graveyard);

        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };

        var returnTrigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        var etbEvent = new CardMovedEvent(bear, ZoneType.Stack, ZoneType.Battlefield);

        returnTrigger.IsTriggered(etbEvent).Should().BeFalse(
            "a 2/2 is not a 1/1 — the trigger only watches 1/1 creatures");
    }

    [Fact]
    public void OpponentsOneOneEntering_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var sword = SwordOfTheMeekFactory.Create(_alice, continuousEffects: null, zoneService: null, triggers, agent: null);
        _alice.Zones.Graveyard.AddCard(sword);
        sword.SetZone(ZoneType.Graveyard);

        var enemyToken = new Creature("Servo", "", 1, 1)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };

        var returnTrigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        var etbEvent = new CardMovedEvent(enemyToken, ZoneType.Stack, ZoneType.Battlefield);

        returnTrigger.IsTriggered(etbEvent).Should().BeFalse(
            "'a 1/1 creature you control' — Bob's token is not under Alice's control");
    }

    [Fact]
    public void Trigger_ActiveOnlyFromGraveyard()
    {
        var sword = SwordOfTheMeekFactory.Create(_alice);

        var returnTrigger = sword.Abilities.OfType<TriggeredAbility>().Single();

        returnTrigger.ActiveZones.Should().ContainSingle()
            .Which.Should().Be(ZoneType.Graveyard,
                "CR 603.6d — the return trigger is a graveyard-resident ability");
    }
}
