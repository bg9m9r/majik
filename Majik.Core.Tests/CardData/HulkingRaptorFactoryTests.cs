using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="HulkingRaptorFactory"/> (The Lost Caverns of
/// Ixalan, {2}{G}{G}).
///
/// Creature — Dinosaur 5/3. Oracle text (verified against Scryfall):
///   "Ward {2}
///    At the beginning of your first main phase, add {G}{G}."
///
/// Covers the card's UNIQUE behaviour:
///   - Identity ({2}{G}{G}, Creature — Dinosaur, 5/3) — single Identity assert.
///   - Carries the Ward keyword marker (CR 702.21).
///   - First-main-phase mana trigger (CR 500.2 / 603.6a / 106.4):
///       * Fires on the controller's PreCombatMain (the "first" main phase).
///       * Does NOT fire on the opponent's PreCombatMain.
///       * Does NOT fire on the controller's Upkeep / postcombat second main.
///       * Resolution adds {G}{G} to the controller's mana pool.
/// </summary>
[Trait("Color", "G")]
public class HulkingRaptorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void HulkingRaptor_Identity()
    {
        var raptor = HulkingRaptorFactory.Create(_alice);

        raptor.Name.Should().Be("Hulking Raptor");
        raptor.ManaCost.Should().Be("{2}{G}{G}");
        raptor.HasType(CardType.Creature).Should().BeTrue();
        raptor.HasSubtype(CardSubtype.Dinosaur).Should().BeTrue();
        raptor.BasePower.Should().Be(5);
        raptor.BaseToughness.Should().Be(3);
        ManaCost.Parse(raptor.ManaCost).TotalValue.Should().Be(4);
        CardColors.GetColors(raptor).Should().Contain(ManaColor.Green,
            "{2}{G}{G} has two green pips — card is green (CR 105.1)");
        raptor.Owner.Should().BeSameAs(_alice);
        raptor.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ward {2} marker
    // -----------------------------------------------------------------------

    [Fact]
    public void HulkingRaptor_CarriesWardMarker()
    {
        var raptor = HulkingRaptorFactory.Create(_alice);

        raptor.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Ward",
                "CR 702.21 — Ward {2} counters opponent-controlled targeting " +
                "unless its controller pays {2}");
    }

    // -----------------------------------------------------------------------
    // First-main-phase mana trigger (CR 500.2 / 603.6a / 106.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void FirstMainTrigger_FiresOnControllersPreCombatMain()
    {
        var (raptor, triggers, bus, _) = BuildWired(_alice);
        PlaceOnBattlefield(raptor, _alice);

        bus.Publish(new StepStartedEvent(StepStateType.PreCombatMain, _alice));

        triggers.PendingCount.Should().Be(1,
            "the trigger fires at the beginning of the controller's first " +
            "(precombat) main phase — CR 505.1a / 603.6a");
    }

    [Fact]
    public void FirstMainTrigger_DoesNotFireOnOpponentsPreCombatMain()
    {
        var (raptor, triggers, bus, _) = BuildWired(_alice);
        PlaceOnBattlefield(raptor, _alice);

        // "Your first main phase" — scoped to the controller's own turn.
        bus.Publish(new StepStartedEvent(StepStateType.PreCombatMain, _bob));

        triggers.PendingCount.Should().Be(0,
            "the trigger is scoped to the controller's own PreCombatMain, " +
            "not the opponent's (CR 500.2)");
    }

    [Fact]
    public void FirstMainTrigger_DoesNotFireOnUpkeepOrSecondMain()
    {
        var (raptor, triggers, bus, _) = BuildWired(_alice);
        PlaceOnBattlefield(raptor, _alice);

        bus.Publish(new StepStartedEvent(StepStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(0, "upkeep is not the first main phase");

        // The postcombat main is the SECOND main phase, not "your first main
        // phase" (CR 505.1a) — the trigger condition keys on PreCombatMain.
        bus.Publish(new StepStartedEvent(StepStateType.PostCombatMain, _alice));
        triggers.PendingCount.Should().Be(0,
            "the postcombat (second) main phase does not fire 'your first " +
            "main phase' (CR 505.1a)");
    }

    [Fact]
    public void FirstMainTrigger_AddsTwoGreenManaOnResolve()
    {
        // CR 106.4 — resolving the trigger adds {G}{G} to the controller's pool.
        var (raptor, triggers, bus, stack) = BuildWired(_alice);
        PlaceOnBattlefield(raptor, _alice);

        var greenBefore = _alice.ManaPool.Green;

        bus.Publish(new StepStartedEvent(StepStateType.PreCombatMain, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.ManaPool.Green.Should().Be(greenBefore + 2,
            "at the beginning of the controller's first main phase, add {G}{G} " +
            "— two green mana (CR 106.4)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (Creature Raptor,
                    TriggerManager Triggers,
                    EventBus Bus,
                    Majik.Core.Stack.Stack Stack)
        BuildWired(Player owner)
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var raptor = HulkingRaptorFactory.Create(owner, triggers);
        return (raptor, triggers, bus, stack);
    }

    private static void PlaceOnBattlefield(ICard card, Player owner)
    {
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }
}
