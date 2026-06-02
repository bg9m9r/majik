using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="EnduringCuriosityFactory"/>.
///
/// Enduring Curiosity (Foundations, {2}{U}{U}). Enchantment Creature — Cat
/// Glimmer 4/3. Oracle text (verified against Scryfall):
///   "Flash
///    Whenever a creature you control deals combat damage to a player, draw a card.
///    When Enduring Curiosity dies, if it was a creature, return it to the
///    battlefield under its owner's control. It's an enchantment. (It's not a
///    creature.)"
///
/// Covers:
/// - Identity ({2}{U}{U} Enchantment Creature — Cat Glimmer, 4/3, mono-U).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Flash keyword marker (CR 702.8).
/// - Combat-damage-to-a-player draw trigger fires for ANY creature the
///   controller controls (CR 510 / CR 603.1), and not for damage to a creature.
/// - Dies → return-to-battlefield + Layer-4 type-strip (CR 603.6c / 701.20 /
///   613.1d): after the return the card is an enchantment but no longer a
///   creature; a subsequent death does not re-return it.
/// </summary>
[Trait("Color", "U")]
public class EnduringCuriosityFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose() => AgentRegistry.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void EnduringCuriosity_Identity()
    {
        var c = EnduringCuriosityFactory.Create(_alice);

        c.Name.Should().Be("Enduring Curiosity");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        c.HasSubtype(CardSubtype.Glimmer).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(3);
        c.ManaCost.Should().Be("{2}{U}{U}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EnduringCuriosity_IsMonoBlue()
    {
        var c = EnduringCuriosityFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Blue);
        colors.Should().HaveCount(1);
    }

    [Fact]
    public void EnduringCuriosity_NamedFactoryDispatch_ProducesTheCard()
    {
        var c = NamedCardFactory.Create("Enduring Curiosity", _alice);

        c.Should().NotBeNull();
        c.Name.Should().Be("Enduring Curiosity");
        c.Should().BeOfType<Creature>();
    }

    // -----------------------------------------------------------------------
    // Keyword
    // -----------------------------------------------------------------------

    [Fact]
    public void EnduringCuriosity_HasFlash()
    {
        var c = EnduringCuriosityFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Flash", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("CR 702.8 — Flash");
    }

    // -----------------------------------------------------------------------
    // Trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void EnduringCuriosity_HasTwoTriggers_BattlefieldActive()
    {
        var c = EnduringCuriosityFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2, "the combat-damage draw trigger + the dies-return trigger");
        triggers.Should().OnlyContain(t => t.ActiveZones.Contains(ZoneType.Battlefield));
    }

    // -----------------------------------------------------------------------
    // Combat-damage-to-a-player draw trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void DrawTrigger_AnyCreatureYouControlHitsPlayer_DrawsACard()
    {
        var alice = new Player("Alice", 20);
        var topCard = new Creature("CardA", "{U}", 1, 1);
        alice.Zones.Library.AddCard(topCard);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggerManager = new TriggerManager(stack, bus);

        var curiosity = EnduringCuriosityFactory.Create(
            alice, triggers: triggerManager, continuousEffects: null, zoneService: null);
        curiosity.SetZone(ZoneType.Battlefield);

        // A DIFFERENT creature Alice controls deals combat damage to Bob.
        var attacker = new Creature("Other Attacker", "{U}", 2, 2);
        attacker.SetOwner(alice);
        attacker.SetController(alice);
        attacker.SetZone(ZoneType.Battlefield);

        bus.Publish(new CombatDamageDealtEvent(attacker, _bob, amount: 2));

        triggerManager.PutPendingTriggersOnStack(alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        alice.Zones.Hand.GetCards().Should().Contain(topCard,
            "any creature you control dealing combat damage to a player draws a card");
    }

    [Fact]
    public void DrawTrigger_OpponentsCreatureHitsPlayer_DoesNotDraw()
    {
        var alice = new Player("Alice", 20);
        alice.Zones.Library.AddCard(new Creature("CardA", "{U}", 1, 1));

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggerManager = new TriggerManager(stack, bus);

        var curiosity = EnduringCuriosityFactory.Create(
            alice, triggers: triggerManager, continuousEffects: null, zoneService: null);
        curiosity.SetZone(ZoneType.Battlefield);

        // Bob's creature deals combat damage to a player — not "a creature YOU
        // control", so Alice's trigger must NOT fire.
        var bobsAttacker = new Creature("Bob's Brute", "{R}", 3, 3);
        bobsAttacker.SetOwner(_bob);
        bobsAttacker.SetController(_bob);
        bobsAttacker.SetZone(ZoneType.Battlefield);

        bus.Publish(new CombatDamageDealtEvent(bobsAttacker, alice, amount: 3));

        triggerManager.PutPendingTriggersOnStack(alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "the trigger only fires for a creature the controller controls (CR 109.5)");
    }

    [Fact]
    public void DrawTrigger_DamageToCreature_DoesNotDraw()
    {
        var alice = new Player("Alice", 20);
        alice.Zones.Library.AddCard(new Creature("CardA", "{U}", 1, 1));

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggerManager = new TriggerManager(stack, bus);

        var curiosity = EnduringCuriosityFactory.Create(
            alice, triggers: triggerManager, continuousEffects: null, zoneService: null);
        curiosity.SetZone(ZoneType.Battlefield);

        // Alice's creature hits a CREATURE, not a player.
        var blocker = new Creature("Wall", "{1}", 0, 4);
        bus.Publish(new CombatDamageDealtEvent(curiosity, (ICard)blocker, amount: 4));

        triggerManager.PutPendingTriggersOnStack(alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "combat damage to a creature (not a player) does not trigger the draw (CR 603.1)");
    }

    // -----------------------------------------------------------------------
    // Dies → return as a (non-creature) enchantment
    // -----------------------------------------------------------------------

    [Fact]
    public void DiesTrigger_ReturnsToBattlefield_UnderOwnersControl()
    {
        var service = new ContinuousEffectsService();
        var curiosity = EnduringCuriosityFactory.Create(
            _alice, triggers: null, continuousEffects: service, zoneService: null);

        // It dies: battlefield → graveyard.
        curiosity.SetOwner(_alice);
        curiosity.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(curiosity);
        curiosity.SetZone(ZoneType.Graveyard);

        var trig = curiosity.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);
        foreach (var effect in trig.Effects) effect.Execute();

        curiosity.Zone.Should().Be(ZoneType.Battlefield, "it returns to the battlefield (CR 701.20)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(curiosity);
        curiosity.Controller.Should().BeSameAs(_alice, "under its owner's control");
    }

    [Fact]
    public void AfterReturn_ItsAnEnchantmentNotACreature()
    {
        var service = new ContinuousEffectsService();
        var curiosity = EnduringCuriosityFactory.Create(
            _alice, triggers: null, continuousEffects: service, zoneService: null);

        curiosity.SetOwner(_alice);
        curiosity.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(curiosity);
        curiosity.SetZone(ZoneType.Graveyard);

        // Before the return the type-strip is gated OFF; it is still a creature.
        // (It's in the graveyard, but the printed creature type is intact.)

        var diesTrigger = curiosity.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);
        foreach (var effect in diesTrigger.Effects) effect.Execute();

        // CR 613.1d — after the return its layered characteristics lose the
        // Creature type but keep the printed Enchantment type.
        var chars = service.Compute((Permanent)curiosity);
        chars.Types.Should().NotContain(CardType.Creature,
            "after returning, it's an enchantment, not a creature (CR 613.1d)");
        chars.Types.Should().Contain(CardType.Enchantment,
            "the printed Enchantment type is preserved (the strip is creature-only)");
    }

    [Fact]
    public void DiesTrigger_OnlyReturnsOnce_SecondDeathDoesNotReturn()
    {
        var service = new ContinuousEffectsService();
        var curiosity = EnduringCuriosityFactory.Create(
            _alice, triggers: null, continuousEffects: service, zoneService: null);

        curiosity.SetOwner(_alice);
        curiosity.SetController(_alice);

        var diesTrigger = curiosity.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

        // First death → return.
        _alice.Zones.Graveyard.AddCard(curiosity);
        curiosity.SetZone(ZoneType.Graveyard);
        foreach (var effect in diesTrigger.Effects) effect.Execute();
        curiosity.Zone.Should().Be(ZoneType.Battlefield);

        // Second death (now a non-creature enchantment) → intervening-if
        // "if it was a creature" fails; it stays in the graveyard.
        _alice.Zones.Battlefield.RemoveCard(curiosity);
        _alice.Zones.Graveyard.AddCard(curiosity);
        curiosity.SetZone(ZoneType.Graveyard);
        foreach (var effect in diesTrigger.Effects) effect.Execute();

        curiosity.Zone.Should().Be(ZoneType.Graveyard,
            "once it has returned as a non-creature enchantment, dying again does not re-return it");
    }
}
