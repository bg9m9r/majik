using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// Tests for Putrid Goblin (Shadows over Innistrad, {1}{B}):
///   - 2/2 Zombie Goblin shape with the printed cost.
///   - Persist (CR 702.79) returns the Goblin on death-without-counter with a
///     -1/-1 counter, and does NOT return it after the post-Persist death.
///
/// Putrid Goblin is a pure near-vanilla Persist body (no ETB / activated
/// ability), so it mirrors Kitchen Finks / Murderous Redcap minus the extra
/// trigger — the whole behaviour is the shared <see cref="Majik.Core.Keywords.PersistFactory"/>
/// primitive layered on the JSON base shape.
/// </summary>
public class PutridGoblinTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Shape_Is2_2_ZombieGoblin_With2ManaCost()
    {
        var goblin = PutridGoblinFactory.Create(_alice);

        goblin.Name.Should().Be(PutridGoblinFactory.CardName);
        goblin.Power.Should().Be(2);
        goblin.Toughness.Should().Be(2);
        goblin.Subtypes.Should().Contain(CardSubtype.Zombie).And.Contain(CardSubtype.Goblin);
        goblin.ManaCost.Should().NotBeNull();
    }

    [Fact]
    public void Shape_AttachesOnlyThePersistTrigger()
    {
        var goblin = PutridGoblinFactory.Create(_alice);

        // Exactly one TriggeredAbility: the Persist death trigger (no ETB).
        goblin.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Putrid Goblin is a pure Persist body — only the Persist death trigger");

        var persist = goblin.Abilities.OfType<TriggeredAbility>().Single();
        persist.TargetRequests.Should().BeEmpty("Persist declares no targets");
        // Persist trigger has Graveyard in ActiveZones (Undying-shape) so it
        // survives the death zone-move.
        persist.ActiveZones.Should().Contain(ZoneType.Graveyard);

        // The "Persist" keyword marker is present for inspectors / tooltips.
        goblin.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Persist");
    }

    [Fact]
    public void Persist_DiesWithNoCounter_ReturnsWithMinusOneOneCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var goblin = PutridGoblinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield);
        triggers.BindCard(goblin);

        zones.MoveCardTo(goblin, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(1, "Persist death trigger must queue");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        goblin.Zone.Should().Be(ZoneType.Battlefield);
        goblin.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1,
            "Persist places one -1/-1 counter on the returning Goblin");
    }

    [Fact]
    public void Persist_DiesWithCounter_DoesNotReturn()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var goblin = PutridGoblinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield);
        // It already bears a -1/-1 counter — the interveningIf (CR 603.4)
        // suppresses the Persist return.
        goblin.Counters.Add(CounterType.MinusOneMinusOne, 1);
        triggers.BindCard(goblin);

        zones.MoveCardTo(goblin, ZoneType.Graveyard);

        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0)
        {
            stack.Pop()!.Resolve();
        }

        goblin.Zone.Should().Be(ZoneType.Graveyard,
            "a Persist creature that died with a -1/-1 counter stays dead");
    }
}
