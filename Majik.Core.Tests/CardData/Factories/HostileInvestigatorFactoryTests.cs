using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HostileInvestigatorFactory"/>.
///
/// Hostile Investigator (Murders at Karlov Manor, {3}{B}). Creature — Ogre
/// Rogue Detective 4/3. Oracle text (verified against Scryfall):
///   "When this creature enters, target opponent discards a card.
///    Whenever one or more players discard one or more cards, investigate.
///    This ability triggers only once each turn. (Create a Clue token. It's
///    an artifact with '{2}, Sacrifice this token: Draw a card.')"
///
/// Covers the card's UNIQUE behaviour:
/// - Identity ({3}{B} Creature — Ogre Rogue Detective, 4/3, mono-B).
/// - ETB: target opponent discards a card (CR 701.16), routed through the
///   central discard chokepoint so a DiscardedEvent fires.
/// - Investigate-on-any-discard trigger (CR 701.39): fires on a DiscardedEvent
///   from any player and banks a Clue, only ONCE per turn (CR 603.2c),
///   re-arming on a new turn (CR 500.1).
/// - The two-trigger shape (both battlefield-active).
/// </summary>
[Trait("Color", "B")]
public class HostileInvestigatorFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose() => AgentRegistry.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void HostileInvestigator_Identity()
    {
        var c = HostileInvestigatorFactory.Create(_alice);

        c.Name.Should().Be("Hostile Investigator");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Ogre).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        c.HasSubtype(CardSubtype.Detective).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(3);
        c.ManaCost.Should().Be("{3}{B}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Black);
        colors.Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HostileInvestigator_HasTwoTriggers_BattlefieldActive()
    {
        var c = HostileInvestigatorFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2,
            "the ETB target-opponent-discard trigger + the investigate-on-discard trigger");
        triggers.Should().OnlyContain(t => t.ActiveZones.Contains(ZoneType.Battlefield));
    }

    [Fact]
    public void EtbTrigger_HasTargetOpponentRequest()
    {
        var c = HostileInvestigatorFactory.Create(_alice);

        var etb = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        etb.TargetRequests.Should().HaveCount(1);
        etb.TargetRequests[0].MinTargets.Should().Be(1);
        etb.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Test harness
    // -----------------------------------------------------------------------

    private (EventBus bus, Majik.Core.Stack.Stack stack, TriggerManager tm, Creature inv)
        Setup()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var tm = new TriggerManager(stack, bus);

        var inv = HostileInvestigatorFactory.Create(
            _alice, eventBus: bus, triggers: tm, zoneService: null);
        inv.SetZone(ZoneType.Battlefield);

        return (bus, stack, tm, inv);
    }

    private static void DrainStack(Majik.Core.Stack.Stack stack, TriggerManager tm, Player p)
    {
        tm.PutPendingTriggersOnStack(p);
        while (stack.Count > 0) stack.Pop()!.Resolve();
    }

    private static int CluesOf(Player p) =>
        p.Zones.Battlefield.GetCards().Count(c => c.HasSubtype(CardSubtype.Clue));

    private void GiveBobHand(int count)
    {
        for (var i = 0; i < count; i++)
            _bob.Zones.Hand.AddCard(new Creature($"BobCard{i}", "{1}", 1, 1));
    }

    // -----------------------------------------------------------------------
    // ETB — target opponent discards a card
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbDiscard_TargetOpponentDiscardsACard()
    {
        GiveBobHand(2);
        var inv = HostileInvestigatorFactory.Create(_alice);
        inv.SetZone(ZoneType.Battlefield);

        var etb = inv.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        etb.SetChosenTargets(new[] { new object[] { _bob } });

        foreach (var effect in etb.Effects) effect.Execute();

        _bob.Zones.Hand.GetCards().Should().HaveCount(1,
            "target opponent discards a card on the ETB (CR 701.16)");
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public void EtbDiscard_NoTargetChosen_IsNoOp()
    {
        GiveBobHand(2);
        var inv = HostileInvestigatorFactory.Create(_alice);
        inv.SetZone(ZoneType.Battlefield);

        var etb = inv.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        // No ChosenTargets populated.

        foreach (var effect in etb.Effects) effect.Execute();

        _bob.Zones.Hand.GetCards().Should().HaveCount(2,
            "with no legal target the ETB does nothing");
    }

    // -----------------------------------------------------------------------
    // Investigate — once per turn, on any discard
    // -----------------------------------------------------------------------

    [Fact]
    public void Investigate_AnyDiscard_BanksAClue()
    {
        var (bus, stack, tm, inv) = Setup();
        CluesOf(_alice).Should().Be(0);

        // Any player's discard fires a DiscardedEvent.
        var discarded = new Creature("Pitched", "{1}", 1, 1);
        bus.Publish(new DiscardedEvent(_bob, discarded, wasCost: false));

        DrainStack(stack, tm, _alice);

        CluesOf(_alice).Should().Be(1,
            "a discard makes Hostile Investigator investigate (CR 701.39)");
    }

    [Fact]
    public void Investigate_OnlyOncePerTurn_SecondDiscardDoesNotInvestigateUntilNextTurn()
    {
        var (bus, stack, tm, inv) = Setup();

        void Discard(string name, Player who)
        {
            bus.Publish(new DiscardedEvent(who, new Creature(name, "{1}", 1, 1), wasCost: false));
            DrainStack(stack, tm, _alice);
        }

        Discard("First", _bob);
        Discard("Second", _alice); // same turn — must NOT investigate again

        CluesOf(_alice).Should().Be(1,
            "the ability triggers only once each turn (CR 603.2c)");

        // CR 500.1 — a new turn re-arms the once-per-turn investigate.
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));
        Discard("Third", _bob);

        CluesOf(_alice).Should().Be(2,
            "a new turn re-arms the once-per-turn investigate");
    }

    [Fact]
    public void Investigate_OwnEtbDiscard_TriggersOnce()
    {
        // The ETB discard funnels through the central discard chokepoint, which
        // fires a DiscardedEvent — so Hostile Investigator's own ETB discard
        // satisfies its investigate ability the turn it enters.
        GiveBobHand(2);
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var tm = new TriggerManager(stack, bus);
        // Fx.DiscardCard looks up the discarder's (Bob's) registered bus to
        // publish the DiscardedEvent — register it so the investigate trigger
        // observes the ETB discard.
        EventBusRegistry.Set(_bob, bus);
        try
        {
            var inv = HostileInvestigatorFactory.Create(
                _alice, eventBus: bus, triggers: tm, zoneService: null);
            inv.SetZone(ZoneType.Battlefield);

            var etb = inv.Abilities.OfType<TriggeredAbility>()
                .Single(t => t.TargetRequests.Count > 0);
            etb.SetChosenTargets(new[] { new object[] { _bob } });

            foreach (var effect in etb.Effects) effect.Execute();
            DrainStack(stack, tm, _alice);

            _bob.Zones.Graveyard.GetCards().Should().HaveCount(1, "the ETB discard happened");
            CluesOf(_alice).Should().Be(1,
                "the ETB discard fires a DiscardedEvent → investigate (CR 701.39)");
        }
        finally
        {
            EventBusRegistry.Remove(_bob);
        }
    }
}
