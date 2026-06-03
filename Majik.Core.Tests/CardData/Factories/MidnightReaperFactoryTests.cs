using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Midnight Reaper (Guilds of Ravnica, {2}{B}) — the first card to
/// consume the declarative <c>whenever_another_creature_dies</c> trigger
/// (aristocrat-death mirror of <c>whenever_another_creature_enters</c>) with
/// the <c>youControlOnly</c> + <c>nontokenOnly</c> filters.
///
///   "Whenever a nontoken creature you control dies, this creature deals
///    1 damage to you and you draw a card."
/// </summary>
[Trait("Color", "B")]
public class MidnightReaperFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void MidnightReaper_Identity()
    {
        var c = MidnightReaperFactory.Create(_alice);

        c.Name.Should().Be("Midnight Reaper");
        c.ManaCost.Should().Be("{2}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MidnightReaper_NontokenCreatureYouControlDies_TriggerMatches()
    {
        var reaper = MidnightReaperFactory.Create(_alice);
        reaper.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var trigger = reaper.Abilities.OfType<TriggeredAbility>().Single();
        var dies = new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.Condition.Matches(dies, trigger).Should().BeTrue();
    }

    [Fact]
    public void MidnightReaper_TokenDies_DoesNotTrigger()
    {
        var reaper = MidnightReaperFactory.Create(_alice);
        reaper.SetZone(ZoneType.Battlefield);

        var token = new Creature("Zombie", "", 2, 2);
        token.SetOwner(_alice);
        token.SetController(_alice);
        token.MarkAsToken();

        var trigger = reaper.Abilities.OfType<TriggeredAbility>().Single();
        var dies = new CardMovedEvent(token, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.Condition.Matches(dies, trigger).Should().BeFalse(
            "a token creature dying does NOT fire the nontoken-gated trigger (CR 111.7)");
    }

    [Fact]
    public void MidnightReaper_OpponentCreatureDies_DoesNotTrigger()
    {
        var reaper = MidnightReaperFactory.Create(_alice);
        reaper.SetZone(ZoneType.Battlefield);

        var enemy = new Creature("Bob's Bear", "{1}{G}", 2, 2);
        enemy.SetOwner(_bob);
        enemy.SetController(_bob);

        var trigger = reaper.Abilities.OfType<TriggeredAbility>().Single();
        var dies = new CardMovedEvent(enemy, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.Condition.Matches(dies, trigger).Should().BeFalse(
            "youControlOnly excludes an opponent's creature dying (CR 109.5)");
    }

    [Fact]
    public void MidnightReaper_SelfDies_DoesNotTrigger()
    {
        var reaper = MidnightReaperFactory.Create(_alice);
        reaper.SetZone(ZoneType.Battlefield);

        var trigger = reaper.Abilities.OfType<TriggeredAbility>().Single();
        var dies = new CardMovedEvent(reaper, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.Condition.Matches(dies, trigger).Should().BeFalse(
            "'another creature' excludes the Reaper's own death");
    }

    [Fact]
    public void MidnightReaper_OnResolve_LosesOneLifeAndDrawsACard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var reaper = MidnightReaperFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(reaper);
        reaper.SetZone(ZoneType.Battlefield);

        // Seed the library so the draw has a card.
        var top = new Creature("Drawn Card", "{1}", 1, 1);
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bus.Publish(new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Graveyard));

        triggers.PendingCount.Should().Be(1, "a nontoken creature you control dying fires Midnight Reaper");
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Pop() is { } obj) obj.Resolve();

        _alice.LifeTotal.Should().Be(19, "the Reaper's controller loses 1 life (modelled net −1)");
        _alice.Zones.Hand.GetCards().Should().Contain(top, "and draws a card (CR 120)");
    }
}
