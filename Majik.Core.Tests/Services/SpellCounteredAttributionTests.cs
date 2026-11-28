using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.Tests.Services;

/// <summary>
/// CR 701.6 / 608.2b — a SpellCounteredEvent says what countered the spell:
/// the resolving spell or ability whose effect countered it (with that
/// object's stack entry id), or nothing when the rules countered it because
/// every target had become illegal.
/// </summary>
public class SpellCounteredAttributionTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ACounterspell_IsTheCounteringSource()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var events = new List<SpellCounteredEvent>();
        _bus.Subscribe<SpellCounteredEvent>(events.Add);

        var bolt = new Spell(new Instant("Lightning Bolt", "R") { Owner = _bob, Zone = ZoneType.Stack }, _bob);
        stack.Push(bolt);
        Spell? counterspell = null;
        counterspell = new Spell(new Instant("Counterspell", "UU") { Owner = _alice, Zone = ZoneType.Stack }, _alice,
            effects: new[] { new Effect("Counter target spell.", () => Majik.Core.CardData.OracleSpellBinder.RemoveFromStack(stack, bolt)) });
        stack.Push(counterspell);
        var counterEntry = stack.TopEntryId;

        new StackResolver(_bus).ResolveTop(stack);

        var e = events.Should().ContainSingle().Subject;
        e.CounteredSpell.Should().BeSameAs(bolt);
        e.CounteringController.Should().BeSameAs(_alice);
        e.CounteringObject.Should().BeSameAs(counterspell);
        e.CounteringEntryId.Should().Be(counterEntry);
        e.Reason.Should().Be(SpellCounterReason.CounteredByEffect);
    }

    [Fact]
    public void ASpellWhoseTargetsAreAllIllegal_IsCounteredByTheRules_WithNoSource()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var events = new List<SpellCounteredEvent>();
        _bus.Subscribe<SpellCounteredEvent>(events.Add);

        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        var bolt = new Spell(new Instant("Lightning Bolt", "R") { Owner = _alice, Zone = ZoneType.Stack }, _alice,
            effects: new[] { new Effect("dmg", () => bear.TakeDamage(3)) });
        bolt.ChosenTargets.Add(bear);
        bolt.TargetLegalityPredicate = t => t is Creature c && c.Zone == ZoneType.Battlefield;
        stack.Push(bolt);
        bear.SetZone(ZoneType.Graveyard);

        new StackResolver(_bus).ResolveTop(stack);

        var e = events.Should().ContainSingle().Subject;
        e.CounteredSpell.Should().BeSameAs(bolt);
        e.CounteringController.Should().BeNull("no spell or ability countered it (CR 608.2b)");
        e.CounteringObject.Should().BeNull();
        e.CounteringEntryId.Should().BeNull();
        e.Reason.Should().Be(SpellCounterReason.AllTargetsIllegal);
    }

    [Fact]
    public void ARepeatedTriggerRemovedByItsInterveningIf_NamesItsOwnStackEntry()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var events = new List<TriggeredAbilityCounteredEvent>();
        _bus.Subscribe<TriggeredAbilityCounteredEvent>(events.Add);

        var source = new Creature("Pinger", "R", 1, 1) { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        var trigger = new TriggeredAbility(source, _bob, Triggers.OnEnterBattlefieldSelf(source),
            interveningIf: () => false);
        stack.Push(trigger);
        stack.Push(trigger);
        var top = stack.TopEntryId;
        top.Should().NotBe(trigger.Id, "a repeat push has its own entry id");

        new StackResolver(_bus).ResolveTop(stack);

        events.Should().ContainSingle().Which.EntryId.Should().Be(top);
    }
}
