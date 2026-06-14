using FluentAssertions;
using Majik.Core.Abilities;
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
/// Unit tests for <see cref="EnduringInnocenceFactory"/>.
///
/// Enduring Innocence (Duskmourn, {1}{W}{W}). Enchantment Creature — Sheep
/// Glimmer 2/1. Oracle text (verified against Scryfall):
///   "Lifelink
///    Whenever one or more other creatures you control with power 2 or less
///    enter, draw a card. This ability triggers only once each turn.
///    When Enduring Innocence dies, if it was a creature, return it to the
///    battlefield under its owner's control. It's an enchantment. (It's not a
///    creature.)"
///
/// Covers the card's UNIQUE behaviour:
/// - Identity ({1}{W}{W} Enchantment Creature — Sheep Glimmer, 2/1, mono-W).
/// - Lifelink keyword marker (CR 702.15).
/// - Small-creature ETB draw trigger: fires for another creature you control
///   with power ≤ 2 (CR 603.6a); not for your own ETB ("other", CR 109.5), not
///   for an opponent's creature, not for a power-3+ creature, and only ONCE per
///   turn (CR 603.2c) — re-arming on a new turn.
/// - Dies → return-to-battlefield + Layer-4 type-strip (CR 603.6c / 701.20 /
///   613.1d).
/// </summary>
[Trait("Color", "W")]
public class EnduringInnocenceFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose() => AgentRegistry.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void EnduringInnocence_Identity()
    {
        var c = EnduringInnocenceFactory.Create(_alice);

        c.Name.Should().Be("Enduring Innocence");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Sheep).Should().BeTrue();
        c.HasSubtype(CardSubtype.Glimmer).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.ManaCost.Should().Be("{1}{W}{W}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.White);
        colors.Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Keyword
    // -----------------------------------------------------------------------

    [Fact]
    public void EnduringInnocence_HasLifelink()
    {
        var c = EnduringInnocenceFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Lifelink", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("CR 702.15 — Lifelink");
    }

    [Fact]
    public void EnduringInnocence_HasTwoTriggers_BattlefieldActive()
    {
        var c = EnduringInnocenceFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2, "the small-creature ETB draw trigger + the dies-return trigger");
        triggers.Should().OnlyContain(t => t.ActiveZones.Contains(ZoneType.Battlefield));
    }

    // -----------------------------------------------------------------------
    // Small-creature ETB draw trigger
    // -----------------------------------------------------------------------

    private (Player alice, EventBus bus, Majik.Core.Stack.Stack stack, TriggerManager tm, Creature innocence)
        Setup(int libraryCards = 4)
    {
        var alice = new Player("Alice", 20);
        for (var i = 0; i < libraryCards; i++)
            alice.Zones.Library.AddCard(new Creature($"Lib{i}", "{W}", 1, 1));

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var tm = new TriggerManager(stack, bus);

        var innocence = EnduringInnocenceFactory.Create(
            alice, eventBus: bus, triggers: tm, continuousEffects: null, zoneService: null);
        innocence.SetZone(ZoneType.Battlefield);

        return (alice, bus, stack, tm, innocence);
    }

    private static void DrainStack(Majik.Core.Stack.Stack stack, TriggerManager tm, Player p)
    {
        tm.PutPendingTriggersOnStack(p);
        while (stack.Count > 0) stack.Pop()!.Resolve();
    }

    // Both triggers key off CardMovedEvent; the dies trigger is the one whose
    // ActiveZones include the Graveyard (so it survives the death zone-move).
    private static TriggeredAbility DiesTriggerOf(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Contains(ZoneType.Graveyard));

    [Fact]
    public void DrawTrigger_AnotherSmallCreatureEnters_DrawsACard()
    {
        var (alice, bus, stack, tm, _) = Setup();
        var before = alice.Zones.Hand.GetCards().Count();

        var token = new Creature("Soldier", "{W}", 1, 1);
        token.SetOwner(alice);
        token.SetController(alice);
        token.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(token, ZoneType.Stack, ZoneType.Battlefield));

        DrainStack(stack, tm, alice);

        alice.Zones.Hand.GetCards().Should().HaveCount(before + 1,
            "another creature you control with power ≤ 2 entering draws a card");
    }

    [Fact]
    public void DrawTrigger_OnlyOncePerTurn_SecondEntryDoesNotDrawUntilNextTurn()
    {
        var (alice, bus, stack, tm, _) = Setup();
        var before = alice.Zones.Hand.GetCards().Count();

        void EnterSmallCreature(string name)
        {
            var t = new Creature(name, "{W}", 1, 1);
            t.SetOwner(alice);
            t.SetController(alice);
            t.SetZone(ZoneType.Battlefield);
            bus.Publish(new CardMovedEvent(t, ZoneType.Stack, ZoneType.Battlefield));
            DrainStack(stack, tm, alice);
        }

        EnterSmallCreature("First");
        EnterSmallCreature("Second"); // same turn — must NOT draw again

        alice.Zones.Hand.GetCards().Should().HaveCount(before + 1,
            "the ability triggers only once each turn (CR 603.2c)");

        // CR 500.1 — a new turn re-arms the ability.
        bus.Publish(new TurnStartedEvent(alice, turnNumber: 2));
        EnterSmallCreature("Third");

        alice.Zones.Hand.GetCards().Should().HaveCount(before + 2,
            "a new turn re-arms the once-per-turn draw");
    }

    [Fact]
    public void DrawTrigger_OwnEtb_DoesNotDraw()
    {
        var (alice, bus, stack, tm, innocence) = Setup();
        var before = alice.Zones.Hand.GetCards().Count();

        // Enduring Innocence itself entering is "another" — its own ETB must
        // not draw (CR 109.5). 2/1 also satisfies power ≤ 2, isolating the
        // "other" guard.
        bus.Publish(new CardMovedEvent(innocence, ZoneType.Stack, ZoneType.Battlefield));
        DrainStack(stack, tm, alice);

        alice.Zones.Hand.GetCards().Should().HaveCount(before,
            "its own ETB does not trigger — \"other\" creatures only (CR 109.5)");
    }

    [Fact]
    public void DrawTrigger_OpponentsCreature_DoesNotDraw()
    {
        var (alice, bus, stack, tm, _) = Setup();
        var before = alice.Zones.Hand.GetCards().Count();

        var bobsToken = new Creature("Bob's Goblin", "{R}", 1, 1);
        bobsToken.SetOwner(_bob);
        bobsToken.SetController(_bob);
        bobsToken.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(bobsToken, ZoneType.Stack, ZoneType.Battlefield));
        DrainStack(stack, tm, alice);

        alice.Zones.Hand.GetCards().Should().HaveCount(before,
            "a creature an opponent controls does not trigger (CR 109.5 — \"you control\")");
    }

    [Fact]
    public void DrawTrigger_PowerThreeCreature_DoesNotDraw()
    {
        var (alice, bus, stack, tm, _) = Setup();
        var before = alice.Zones.Hand.GetCards().Count();

        var big = new Creature("Big Bear", "{1}{G}", 3, 3);
        big.SetOwner(alice);
        big.SetController(alice);
        big.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(big, ZoneType.Stack, ZoneType.Battlefield));
        DrainStack(stack, tm, alice);

        alice.Zones.Hand.GetCards().Should().HaveCount(before,
            "a power-3 creature exceeds the \"power 2 or less\" gate");
    }

    // -----------------------------------------------------------------------
    // Dies → return as a (non-creature) enchantment
    // -----------------------------------------------------------------------

    [Fact]
    public void DiesTrigger_ReturnsToBattlefield_UnderOwnersControl()
    {
        var service = new ContinuousEffectsService();
        var innocence = EnduringInnocenceFactory.Create(
            _alice, eventBus: null, triggers: null, continuousEffects: service, zoneService: null);

        innocence.SetOwner(_alice);
        innocence.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(innocence);
        innocence.SetZone(ZoneType.Graveyard);

        var trig = DiesTriggerOf(innocence);
        foreach (var effect in trig.Effects) effect.Execute();

        innocence.Zone.Should().Be(ZoneType.Battlefield, "it returns to the battlefield (CR 701.20)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(innocence);
        innocence.Controller.Should().BeSameAs(_alice, "under its owner's control");
    }

    [Fact]
    public void AfterReturn_ItsAnEnchantmentNotACreature()
    {
        var service = new ContinuousEffectsService();
        var innocence = EnduringInnocenceFactory.Create(
            _alice, eventBus: null, triggers: null, continuousEffects: service, zoneService: null);

        innocence.SetOwner(_alice);
        innocence.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(innocence);
        innocence.SetZone(ZoneType.Graveyard);

        var diesTrigger = DiesTriggerOf(innocence);
        foreach (var effect in diesTrigger.Effects) effect.Execute();

        // CR 613.1d — after the return its layered characteristics lose the
        // Creature type but keep the printed Enchantment type.
        var chars = service.Compute((Permanent)innocence);
        chars.Types.Should().NotContain(CardType.Creature,
            "after returning, it's an enchantment, not a creature (CR 613.1d)");
        chars.Types.Should().Contain(CardType.Enchantment,
            "the printed Enchantment type is preserved (the strip is creature-only)");
    }

    [Fact]
    public void DiesTrigger_OnlyReturnsOnce_SecondDeathDoesNotReturn()
    {
        var service = new ContinuousEffectsService();
        var innocence = EnduringInnocenceFactory.Create(
            _alice, eventBus: null, triggers: null, continuousEffects: service, zoneService: null);

        innocence.SetOwner(_alice);
        innocence.SetController(_alice);

        var diesTrigger = DiesTriggerOf(innocence);

        // First death → return.
        _alice.Zones.Graveyard.AddCard(innocence);
        innocence.SetZone(ZoneType.Graveyard);
        foreach (var effect in diesTrigger.Effects) effect.Execute();
        innocence.Zone.Should().Be(ZoneType.Battlefield);

        // Second death (now a non-creature enchantment) → intervening-if
        // "if it was a creature" fails; it stays in the graveyard.
        _alice.Zones.Battlefield.RemoveCard(innocence);
        _alice.Zones.Graveyard.AddCard(innocence);
        innocence.SetZone(ZoneType.Graveyard);
        foreach (var effect in diesTrigger.Effects) effect.Execute();

        innocence.Zone.Should().Be(ZoneType.Graveyard,
            "once it has returned as a non-creature enchantment, dying again does not re-return it");
    }
}
