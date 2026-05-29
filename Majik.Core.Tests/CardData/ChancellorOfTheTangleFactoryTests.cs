using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ChancellorOfTheTangleFactory"/> (New Phyrexia,
/// {4}{G}{G}{G}).
///
/// Creature — Phyrexian Beast 6/7. Oracle text:
///   "You may reveal this card from your opening hand. If you do, at the
///    beginning of your first main phase of the game, add {G}."
///   "Vigilance, reach"
///
/// Covers:
///   - Identity ({4}{G}{G}{G}, Creature — Phyrexian Beast, 6/7, green).
///   - MV 7, <see cref="NamedCardFactory"/> dispatch.
///   - Carries opening-hand reveal marker + Vigilance + Reach keywords.
///   - Opening-hand reveal subscriber (<see cref="OpeningHandRevealAddManaTrigger"/>):
///       * Yes-answer schedules a delayed first-PreCombatMain trigger.
///       * No-answer schedules nothing.
///       * Trigger fires on revealer's FIRST PreCombatMain; adds {G} to pool.
///       * Trigger fires ONCE (CR 603.7d — auto-unregisters after firing).
///       * Trigger scoped to revealer's own turn, not opponent's.
/// </summary>
public class ChancellorOfTheTangleFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Chancellor_Identity()
    {
        var chancellor = ChancellorOfTheTangleFactory.Create(_alice);

        chancellor.Name.Should().Be("Chancellor of the Tangle");
        chancellor.ManaCost.Should().Be("{4}{G}{G}{G}");
        chancellor.HasType(CardType.Creature).Should().BeTrue();
        chancellor.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        chancellor.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        chancellor.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        chancellor.BasePower.Should().Be(6);
        chancellor.BaseToughness.Should().Be(7);
        chancellor.Owner.Should().BeSameAs(_alice);
        chancellor.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Chancellor_IsGreen()
    {
        // CR 105.1 — {G} pip makes the card green.
        var chancellor = ChancellorOfTheTangleFactory.Create(_alice);
        CardColors.GetColors(chancellor).Should().Contain(ManaColor.Green,
            "{4}{G}{G}{G} has three green pips — card is green");
    }

    [Fact]
    public void Chancellor_ManaValue_Is7()
    {
        // CR 202.3 — {4}{G}{G}{G} = 4 + 3 = MV 7.
        var chancellor = ChancellorOfTheTangleFactory.Create(_alice);
        ManaCost.Parse(chancellor.ManaCost).TotalValue.Should().Be(7);
    }

    [Fact]
    public void Chancellor_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Chancellor of the Tangle", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Chancellor of the Tangle");
        card.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        card.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(6);
        ((Creature)card).BaseToughness.Should().Be(7);
        card.ManaCost.Should().Be("{4}{G}{G}{G}");
    }

    // -----------------------------------------------------------------------
    // Keyword markers
    // -----------------------------------------------------------------------

    [Fact]
    public void Chancellor_CarriesOpeningHandRevealMarker()
    {
        var chancellor = ChancellorOfTheTangleFactory.Create(_alice);

        chancellor.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword ==
                ChancellorOfTheTangleFactory.RevealMarkerKeyword,
                "the shared OpeningHandRevealAddManaTrigger subscriber " +
                "scans for this marker on game start");
    }

    [Fact]
    public void Chancellor_CarriesVigilanceMarker()
    {
        var chancellor = ChancellorOfTheTangleFactory.Create(_alice);

        chancellor.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Vigilance",
                "CR 702.20 — Vigilance prevents tapping on attack");
    }

    [Fact]
    public void Chancellor_CarriesReachMarker()
    {
        var chancellor = ChancellorOfTheTangleFactory.Create(_alice);

        chancellor.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Reach",
                "CR 702.17 — Reach allows blocking flying creatures");
    }

    // -----------------------------------------------------------------------
    // Opening-hand reveal subscriber (OpeningHandRevealAddManaTrigger)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RevealSubscriber_AcceptedPrompt_RegistersDelayedFirstMainTrigger()
    {
        var chancellor = ChancellorOfTheTangleFactory.Create(_alice);
        PlaceInHand(chancellor, _alice);

        var (subscriber, triggers, bus, _) = BuildRevealSubscriber(_alice, YesAgent());

        await subscriber.HandleAsync(new OpeningHandCheckEvent(
            _alice, _alice.Zones.Hand.GetCards().ToList()));

        // Drive Alice's PreCombatMain — the scheduled delayed trigger fires.
        bus.Publish(new StepStartedEvent(PhaseStateType.PreCombatMain, _alice));
        triggers.PendingCount.Should().Be(1,
            "yes-revealing must register a one-shot first-PreCombatMain trigger");
    }

    [Fact]
    public async Task RevealSubscriber_DeclinedPrompt_RegistersNothing()
    {
        var chancellor = ChancellorOfTheTangleFactory.Create(_alice);
        PlaceInHand(chancellor, _alice);

        var (subscriber, triggers, bus, _) = BuildRevealSubscriber(_alice, NoAgent());

        await subscriber.HandleAsync(new OpeningHandCheckEvent(
            _alice, _alice.Zones.Hand.GetCards().ToList()));

        bus.Publish(new StepStartedEvent(PhaseStateType.PreCombatMain, _alice));
        triggers.PendingCount.Should().Be(0,
            "declined reveal must not schedule any first-main trigger");
    }

    [Fact]
    public async Task RevealSubscriber_Trigger_AddsGreenManaOnResolve()
    {
        // CR 103.6 / CR 605.1a — revealing Chancellor from opening hand adds
        // {G} to the revealer's mana pool at the beginning of their first
        // main phase.
        var chancellor = ChancellorOfTheTangleFactory.Create(_alice);
        PlaceInHand(chancellor, _alice);

        var (subscriber, triggers, bus, stack) =
            BuildRevealSubscriber(_alice, YesAgent());

        await subscriber.HandleAsync(new OpeningHandCheckEvent(
            _alice, _alice.Zones.Hand.GetCards().ToList()));

        var greenBefore = _alice.ManaPool.Green;

        // Advance to Alice's first PreCombatMain — trigger goes pending.
        bus.Publish(new StepStartedEvent(PhaseStateType.PreCombatMain, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        // Resolve the trigger off the stack (same pattern as DevourerOfDestiny
        // tests — pop + resolve executes the effect closure).
        stack.Pop()!.Resolve();

        _alice.ManaPool.Green.Should().Be(greenBefore + 1,
            "revealing Chancellor adds {G} — one green mana — to the pool " +
            "(CR 605.1a / CR 103.6)");
    }

    [Fact]
    public async Task RevealSubscriber_Trigger_OnlyFires_OnRevealersOwnPreCombatMain()
    {
        // The delayed trigger is scoped to the REVEALER's PreCombatMain
        // (CR 500.2 — each player has their own beginning-of-precombat-main).
        var chancellor = ChancellorOfTheTangleFactory.Create(_alice);
        PlaceInHand(chancellor, _alice);

        var (subscriber, triggers, bus, _) = BuildRevealSubscriber(_alice, YesAgent());

        await subscriber.HandleAsync(new OpeningHandCheckEvent(
            _alice, _alice.Zones.Hand.GetCards().ToList()));

        // Bob's PreCombatMain doesn't fire it.
        bus.Publish(new StepStartedEvent(PhaseStateType.PreCombatMain, _bob));
        triggers.PendingCount.Should().Be(0,
            "trigger is scoped to revealer's own PreCombatMain, not opponent's");

        // Alice's Upkeep doesn't fire it.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(0,
            "trigger is scoped to PreCombatMain, not upkeep");

        // Alice's PreCombatMain fires it exactly once.
        bus.Publish(new StepStartedEvent(PhaseStateType.PreCombatMain, _alice));
        triggers.PendingCount.Should().Be(1);
    }

    [Fact]
    public async Task RevealSubscriber_Trigger_FiresOnce_AutoUnregisters()
    {
        // CR 603.7d — delayed triggered abilities auto-unregister after
        // firing. The trigger must fire exactly once (first main phase of
        // the game), not on every subsequent main phase.
        var chancellor = ChancellorOfTheTangleFactory.Create(_alice);
        PlaceInHand(chancellor, _alice);

        var (subscriber, triggers, bus, stack) =
            BuildRevealSubscriber(_alice, YesAgent());

        await subscriber.HandleAsync(new OpeningHandCheckEvent(
            _alice, _alice.Zones.Hand.GetCards().ToList()));

        // First PreCombatMain: trigger fires.
        bus.Publish(new StepStartedEvent(PhaseStateType.PreCombatMain, _alice));
        triggers.PendingCount.Should().Be(1, "first PreCombatMain fires the trigger");

        // Drain onto stack and resolve so the delayed trigger unregisters.
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Second PreCombatMain: trigger must NOT fire again.
        bus.Publish(new StepStartedEvent(PhaseStateType.PreCombatMain, _alice));
        triggers.PendingCount.Should().Be(0,
            "CR 603.7d — delayed trigger auto-unregisters after firing once");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private (OpeningHandRevealAddManaTrigger Subscriber,
             TriggerManager Triggers,
             EventBus Bus,
             Majik.Core.Stack.Stack Stack)
        BuildRevealSubscriber(Player player, IPlayerAgent agent)
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var agents = new Dictionary<Player, IPlayerAgent> { [player] = agent };
        return (new OpeningHandRevealAddManaTrigger(agents, triggers), triggers, bus, stack);
    }

    private static ScriptedAgent YesAgent()
    {
        var a = new ScriptedAgent();
        a.QueueYesNo(true);
        return a;
    }

    private static ScriptedAgent NoAgent()
    {
        var a = new ScriptedAgent();
        a.QueueYesNo(false);
        return a;
    }

    private static void PlaceInHand(ICard card, Player owner)
    {
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
    }
}
