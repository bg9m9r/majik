using System.Threading.Tasks;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// Unit tests for <see cref="WardTriggerFactory"/> — the reusable Ward
/// triggered-ability primitive (CR 702.21e/f). Verifies the shared shape fires
/// the Ward trigger off <see cref="Majik.Core.Domain.DomainEvents.TargetsChosenEvent"/>
/// only for an opponent's spell/ability targeting the warded permanent, and on
/// resolution counters that spell unless the targeting player pays the ward
/// cost — across a mana ward and a non-mana (pay-life) ward.
/// </summary>
public class WardTriggerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static GameContext LiveContext(
        Player self, Player opp, Majik.Core.Stack.Stack stack) =>
        new(self, new[] { self, opp }, self, 1, StepStateType.PreCombatMain, stack);

    private Creature WardedCreature(Majik.Core.Stack.Stack? stack = null, TriggerManager? triggers = null)
    {
        var c = new Creature("Warded Bear", "{1}{G}", 2, 2);
        c.SetOwner(_alice);
        c.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(Majik.Core.Zones.ZoneType.Battlefield);

        var ward = new WardEffect(c, ManaCost.Parse("{2}"));
        var trigger = WardTriggerFactory.Build(ward, stack);
        c.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
        return c;
    }

    [Fact]
    public void Build_ProducesBattlefieldTrigger()
    {
        var c = WardedCreature();
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(Majik.Core.Zones.ZoneType.Battlefield);
        ReferenceEquals(trigger.Source, c).Should().BeTrue();
    }

    [Fact]
    public void OpponentSpellTargetsWarded_Triggers()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var c = WardedCreature(stack, triggers);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(c) });
        bus.Publish(new Majik.Core.Domain.DomainEvents.TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(1,
            "an opponent's spell targeting the warded permanent fires Ward (CR 702.21e)");
    }

    [Fact]
    public void OwnSpellTargetsWarded_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var c = WardedCreature(stack, triggers);

        var growth = new Instant("Giant Growth", "G") { Owner = _alice };
        var spell = new Majik.Core.Spells.Spell(growth, _alice, new[] { Target.Permanent(c) });
        bus.Publish(new Majik.Core.Domain.DomainEvents.TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(0,
            "Ward fires only for a spell an OPPONENT controls (CR 702.21e)");
    }

    [Fact]
    public void OpponentSpellTargetsSomethingElse_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var c = WardedCreature(stack, triggers);

        var other = new Creature("Other", "{1}", 1, 1);
        other.SetOwner(_alice);
        other.SetController(_alice);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(other) });
        bus.Publish(new Majik.Core.Domain.DomainEvents.TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(0,
            "Ward fires only when the warded permanent itself is targeted");
    }

    [Fact]
    public async Task Resolution_CannotPay_Counters()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var c = WardedCreature(stack, triggers);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(c) });
        bolt.SetZone(Majik.Core.Zones.ZoneType.Stack);
        stack.Push(spell);

        // Bob has no mana available → cannot pay the {2} ward → countered.
        bus.Publish(new Majik.Core.Domain.DomainEvents.TargetsChosenEvent(spell, spell.Targets));
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        await trigger.ResolveAsync(agent: null, LiveContext(_alice, _bob, stack));

        stack.GetAll().Should().NotContain(spell, "Bob can't pay {2}, so his spell is countered");
        bolt.Zone.Should().Be(Majik.Core.Zones.ZoneType.Graveyard,
            "a countered spell goes to its owner's graveyard (CR 701.5b)");
    }

    // ─── card wiring: the previously-inert Ward cards now carry a real trigger ───

    [Theory]
    [InlineData("Kappa Cannoneer")]
    [InlineData("Tolarian Terror")]
    [InlineData("Colossal Skyturtle")]
    [InlineData("Sire of Seven Deaths")]
    public void NamedDispatch_WardCard_CarriesWardTrigger(string name)
    {
        var card = NamedCardFactory.Create(name, _alice);
        card.Abilities.OfType<TriggeredAbility>().Should().NotBeEmpty(
            $"{name} ships Ward as a real triggered ability (CR 702.21e)");
        card.Abilities.OfType<KeywordAbility>().Should().Contain(k => k.Keyword == "Ward");
    }

    [Fact]
    public async Task TolarianTerror_ProdPath_CannotPay_Counters()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var terror = (Creature)NamedCardFactory.Create("Tolarian Terror", _alice);
        terror.SetController(_alice);
        terror.SetZone(Majik.Core.Zones.ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(terror);
        bus.Publish(new CardMovedEvent(
            terror, Majik.Core.Zones.ZoneType.Hand, Majik.Core.Zones.ZoneType.Battlefield));

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(terror) });
        bolt.SetZone(Majik.Core.Zones.ZoneType.Stack);
        stack.Push(spell);

        bus.Publish(new Majik.Core.Domain.DomainEvents.TargetsChosenEvent(spell, spell.Targets));
        triggers.PendingCount.Should().Be(1,
            "the ward trigger must auto-register on the prod build path");

        var trigger = terror.Abilities.OfType<TriggeredAbility>().Single();
        await trigger.ResolveAsync(agent: null, LiveContext(_alice, _bob, stack));

        stack.GetAll().Should().NotContain(spell,
            "Bob can't pay {3}, so Tolarian Terror's ward counters his spell");
        bolt.Zone.Should().Be(Majik.Core.Zones.ZoneType.Graveyard);
    }

    [Fact]
    public async Task SireOfSevenDeaths_ProdPath_PaysLife_NotCountered()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var sire = (Creature)NamedCardFactory.Create("Sire of Seven Deaths", _alice);
        sire.SetController(_alice);
        sire.SetZone(Majik.Core.Zones.ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sire);
        bus.Publish(new CardMovedEvent(
            sire, Majik.Core.Zones.ZoneType.Hand, Majik.Core.Zones.ZoneType.Battlefield));

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(sire) });
        bolt.SetZone(Majik.Core.Zones.ZoneType.Stack);
        stack.Push(spell);

        var lifeBefore = _bob.LifeTotal;
        bus.Publish(new Majik.Core.Domain.DomainEvents.TargetsChosenEvent(spell, spell.Targets));
        var trigger = sire.Abilities.OfType<TriggeredAbility>().Single();
        await trigger.ResolveAsync(agent: null, LiveContext(_alice, _bob, stack));

        stack.GetAll().Should().Contain(spell,
            "Bob pays 7 life to satisfy Sire's Ward — his spell survives (CR 702.21f)");
        _bob.LifeTotal.Should().Be(lifeBefore - SireOfSevenDeathsFactory.WardLifeAmount,
            "the pay-life ward cost was charged");
    }
}
