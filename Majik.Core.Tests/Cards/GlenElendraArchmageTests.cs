using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// Tests for Glen Elendra Archmage (Eventide, {1}{U}{U}):
///   - 2/2 Faerie Wizard shape with Flying.
///   - Activated ability "{U}, Sacrifice ~: Counter target noncreature spell"
///     counters a noncreature spell, does NOT counter a creature spell
///     (CR 608.2b illegal-on-resolution).
///   - Persist (CR 702.79) — dies without -1/-1 counter → returns with one.
/// </summary>
public class GlenElendraArchmageTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Shape_Is2_2_FaerieWizard_WithFlying()
    {
        var archmage = GlenElendraArchmageFactory.Create(_alice);

        archmage.Name.Should().Be(GlenElendraArchmageFactory.CardName);
        archmage.Power.Should().Be(GlenElendraArchmageFactory.Power);
        archmage.Toughness.Should().Be(GlenElendraArchmageFactory.Toughness);
        archmage.Subtypes.Should().Contain(CardSubtype.Faerie).And.Contain(CardSubtype.Wizard);

        archmage.Abilities.OfType<KeywordAbility>().Should()
            .Contain(k => k.Keyword == "Flying", "Glen Elendra Archmage has Flying");
        archmage.Abilities.OfType<KeywordAbility>().Should()
            .Contain(k => k.Keyword == "Persist", "PersistFactory attaches the keyword marker");
    }

    [Fact]
    public void Shape_AttachesActivatedCounterAbilityAndPersistTrigger()
    {
        var archmage = GlenElendraArchmageFactory.Create(_alice);

        archmage.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {U}, Sacrifice counter ability");
        archmage.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the Persist death trigger");
    }

    [Fact]
    public void Activated_CountersNoncreatureSpell_RemovesFromStackToGraveyard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var archmage = GlenElendraArchmageFactory.Create(_alice, stack);
        _alice.Zones.Battlefield.AddCard(archmage);
        archmage.SetZone(ZoneType.Battlefield);

        // A noncreature spell on the stack (a sorcery card).
        var sorceryCard = new Majik.Core.Cards.Sorcery("Big Spell", "{2}{U}");
        sorceryCard.SetOwner(_bob);
        sorceryCard.SetController(_bob);
        var spell = new Majik.Core.Spells.Spell(sorceryCard, _bob);
        stack.Push(spell);
        sorceryCard.SetZone(ZoneType.Stack);

        // Wire the chosen target then execute the activated ability's effect
        // (skipping cost-payment so we directly verify the resolve body).
        var activated = archmage.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new[] { new[] { (object)spell } });

        foreach (var e in activated.Effects) e.Execute();

        // Spell is countered → off the stack, in graveyard.
        stack.IsEmpty.Should().BeTrue("countered spell is removed from the stack");
        sorceryCard.Zone.Should().Be(ZoneType.Graveyard);
        // Archmage was sacrificed.
        archmage.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activated_CreatureSpellTarget_NoOpAtResolve()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var archmage = GlenElendraArchmageFactory.Create(_alice, stack);
        _alice.Zones.Battlefield.AddCard(archmage);
        archmage.SetZone(ZoneType.Battlefield);

        // A creature spell on the stack.
        var creatureCard = new Creature("Big Creature", "{2}{U}", 3, 3);
        creatureCard.SetOwner(_bob);
        creatureCard.SetController(_bob);
        var spell = new Majik.Core.Spells.Spell(creatureCard, _bob);
        stack.Push(spell);
        creatureCard.SetZone(ZoneType.Stack);

        var activated = archmage.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new[] { new[] { (object)spell } });

        foreach (var e in activated.Effects) e.Execute();

        // Spell is NOT countered — still on the stack.
        stack.IsEmpty.Should().BeFalse(
            "CR 608.2b — creature-spell target is illegal-on-resolution; counter does nothing");
        creatureCard.Zone.Should().Be(ZoneType.Stack);
        // Archmage still gets sacrificed (cost was paid).
        archmage.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Persist_DiesWithNoCounter_ReturnsWithMinusOneOneCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var archmage = GlenElendraArchmageFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(archmage);
        archmage.SetZone(ZoneType.Battlefield);
        triggers.BindCard(archmage);

        zones.MoveCardTo(archmage, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(1, "Persist death trigger must queue");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        archmage.Zone.Should().Be(ZoneType.Battlefield);
        archmage.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1);
    }
}
