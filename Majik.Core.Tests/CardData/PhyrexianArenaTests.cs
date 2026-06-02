using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Phyrexian Arena (Apocalypse, {1}{B}{B}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "At the beginning of your upkeep, you draw a card and you lose 1 life."
///
/// Covers:
///   - Card identity (name, type, mana cost).
///   - Upkeep trigger structure (filtered to controller's own upkeep,
///     active only on the battlefield).
///   - Mechanic: upkeep draws the top of library into hand and loses
///     exactly 1 life (a flat 1, NOT mana-value based — that is the key
///     difference from the Dark Confidant analogue).
///   - Empty-library edge: still loses 1 life and the draw-from-empty flag
///     is set (CR 120.3 — the life loss is independent of the draw).
///   - Live wiring: when registered with a TriggerManager, an Upkeep
///     StepStartedEvent for the controller surfaces the trigger as pending.
///   - NamedCardFactory dispatch.
/// </summary>
public class PhyrexianArenaTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void PhyrexianArena_IsEnchantment_AtCost1BB()
    {
        var arena = PhyrexianArenaFactory.Create(_alice);

        arena.Name.Should().Be("Phyrexian Arena");
        arena.ManaCost.Should().Be("{1}{B}{B}");
        arena.HasType(CardType.Enchantment).Should().BeTrue();
        arena.Owner.Should().BeSameAs(_alice);
        arena.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PhyrexianArena_HasUpkeepTrigger_OnlyOnBattlefield()
    {
        var arena = PhyrexianArenaFactory.Create(_alice);

        var triggers = arena.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var trigger = triggers[0];
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
    }

    [Fact]
    public void PhyrexianArena_Upkeep_DrawsTopLibrary_LosesExactlyOneLife()
    {
        // A high-mana-value spell on top proves the loss is a FLAT 1, not
        // its mana value (the Dark Confidant difference).
        var bolt = new Instant("Cruel Ultimatum", "{U}{U}{B}{B}{B}{R}{R}") { Owner = _alice };
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var arena = PhyrexianArenaFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(arena);
        arena.SetZone(ZoneType.Battlefield);

        var trigger = arena.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        // Card drawn into hand.
        _alice.Zones.Hand.GetCards().Should().Contain(bolt);
        _alice.Zones.Library.GetCards().Should().NotContain(bolt);

        // Flat 1 life loss regardless of the drawn card's mana value.
        _alice.LifeTotal.Should().Be(19);
    }

    [Fact]
    public void PhyrexianArena_Upkeep_EmptyLibrary_StillLosesOneLife_MarksDrawFromEmpty()
    {
        // Library is empty. Per CR 120.3 the life loss is a separate event
        // from the draw, so Phyrexian Arena's controller still loses 1 life,
        // and the "tried to draw from empty library" flag is set (CR 704.5b
        // hands the loss to SBAs on the next pass).
        _alice.Zones.Library.GetCards().Should().BeEmpty();

        var arena = PhyrexianArenaFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(arena);
        arena.SetZone(ZoneType.Battlefield);

        var trigger = arena.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(19);
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }

    [Fact]
    public void PhyrexianArena_LiveWiring_UpkeepStepRegistersPendingTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var arena = PhyrexianArenaFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(arena);
        arena.SetZone(ZoneType.Battlefield);

        // Bob's upkeep — Alice's Arena does NOT trigger (only her own).
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _bob));
        triggers.PendingCount.Should().Be(0,
            "Phyrexian Arena only triggers on its controller's own upkeep");

        // Alice's upkeep — trigger surfaces as pending.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(1);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PhyrexianArena()
    {
        var card = NamedCardFactory.Create("Phyrexian Arena", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Phyrexian Arena");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }
}
