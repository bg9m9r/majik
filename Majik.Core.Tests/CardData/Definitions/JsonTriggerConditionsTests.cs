using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// End-to-end coverage for the declarative <see cref="TriggerDefinition"/>
/// condition variants added on top of the EXISTING engine events — the lever
/// that unblocks trigger-gated cards from hand-rolled C# (mirrors the
/// effect-verb path in <see cref="JsonTargetingEffectsTests"/>).
///
/// Each test parses a throwaway JSON <see cref="CardDefinition"/>, materialises
/// it through the production <see cref="CardDefinitionFactory.Build(CardDefinition, Player)"/>
/// path, registers the built <see cref="TriggeredAbility"/> with a live
/// <see cref="TriggerManager"/>, then raises the REAL game event the variant
/// listens on and asserts the trigger fires (PendingCount) and its declarative
/// effect resolves off the stack. CR numbers cited per variant.
///
/// Variants covered:
/// <list type="bullet">
///   <item><c>whenever_you_gain_life</c> (CR 119.3) over
///   <see cref="LifeChangedEvent"/> — controller-scoped, strict positive
///   delta.</item>
///   <item><c>whenever_you_cast_spell</c> (CR 601.2 / 603.1) over
///   <see cref="SpellCastEvent"/> — controller-scoped, optional
///   <c>noncreatureOnly</c> + <c>spellTypes</c> filter.</item>
///   <item><c>attacks_self</c> (CR 508.1f) over
///   <see cref="CreatureAttacksEvent"/> — per-attacker self match.</item>
///   <item><c>dies_self</c> (CR 700.4) over
///   <see cref="CardMovedEvent"/> — Battlefield → Graveyard self move; the
///   ability stays active in the Graveyard so it is observable after the
///   zone stamp.</item>
/// </list>
/// </summary>
public class JsonTriggerConditionsTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private (TriggeredAbility ability, Permanent card) BuildAndRegister(
        string json, TriggerManager triggers)
    {
        var def = CardDefinitionLoader.FromJson(json);
        var card = (Permanent)CardDefinitionFactory.Build(def, _alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        var ability = card.Abilities.OfType<TriggeredAbility>().Single();
        triggers.RegisterTriggeredAbility(ability);
        return (ability, card);
    }

    private static void ResolveAll(Majik.Core.Stack.Stack stack)
    {
        while (true)
        {
            var top = stack.Pop();
            if (top == null) break;
            top.Resolve();
        }
    }

    // ------------------------------------------------------------------
    // whenever_you_gain_life — CR 119.3.
    // ------------------------------------------------------------------

    private const string GainLifeCounterJson = """
    {
      "name": "Test Pridemate",
      "types": ["Creature"],
      "manaCost": "1W",
      "power": 2,
      "toughness": 2,
      "abilities": [
        {
          "kind": "triggered",
          "trigger": { "type": "whenever_you_gain_life" },
          "effects": [ { "type": "put_counter", "counter": "+1/+1", "amount": 1, "target": "self" } ]
        }
      ]
    }
    """;

    [Fact]
    public void WheneverYouGainLife_FiresForController_AddsCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var (_, card) = BuildAndRegister(GainLifeCounterJson, triggers);

        // Controller gains life (CR 119.3 — strict NewLife > PreviousLife).
        bus.Publish(new LifeChangedEvent(_alice, 20, 23));

        triggers.PendingCount.Should().Be(1, "controller life GAIN fires the trigger (CR 119.3)");
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveAll(stack);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the put_counter self effect resolves once per life-gain event (CR 122.1)");
    }

    [Fact]
    public void WheneverYouGainLife_DoesNotFireForOpponentGain()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(GainLifeCounterJson, triggers);

        bus.Publish(new LifeChangedEvent(_bob, 20, 25));

        triggers.PendingCount.Should().Be(0, "'whenever YOU gain life' is controller-scoped");
    }

    [Fact]
    public void WheneverYouGainLife_DoesNotFireForLifeLoss()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(GainLifeCounterJson, triggers);

        // Life LOSS (NewLife < PreviousLife) — not a gain.
        bus.Publish(new LifeChangedEvent(_alice, 20, 17));

        triggers.PendingCount.Should().Be(0, "life LOSS is not life gain (strict positive delta)");
    }

    // ------------------------------------------------------------------
    // whenever_you_cast_spell — CR 601.2 / 603.1.
    // ------------------------------------------------------------------

    private const string NoncreatureCastJson = """
    {
      "name": "Test Spellgorger",
      "types": ["Creature"],
      "manaCost": "2R",
      "power": 2,
      "toughness": 2,
      "abilities": [
        {
          "kind": "triggered",
          "trigger": { "type": "whenever_you_cast_spell", "noncreatureOnly": true },
          "effects": [ { "type": "put_counter", "counter": "+1/+1", "amount": 1, "target": "self" } ]
        }
      ]
    }
    """;

    private static SpellCastEvent NoncreatureCast(Player controller)
    {
        var instant = new Instant("Shock", "R") { Owner = controller };
        return new SpellCastEvent(new Majik.Core.Spells.Spell(instant, controller));
    }

    private static SpellCastEvent CreatureCast(Player controller)
    {
        var creature = new Creature("Bear", "1G", 2, 2) { Owner = controller };
        return new SpellCastEvent(new Majik.Core.Spells.Spell(creature, controller));
    }

    [Fact]
    public void WheneverYouCastNoncreature_Fires_AddsCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var (_, card) = BuildAndRegister(NoncreatureCastJson, triggers);

        bus.Publish(NoncreatureCast(_alice));

        triggers.PendingCount.Should().Be(1, "casting a noncreature spell fires the trigger (CR 603.1)");
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveAll(stack);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void WheneverYouCastNoncreature_DoesNotFireForCreatureSpell()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(NoncreatureCastJson, triggers);

        bus.Publish(CreatureCast(_alice));

        triggers.PendingCount.Should().Be(0, "a creature spell is not a noncreature spell (CR 112.1)");
    }

    [Fact]
    public void WheneverYouCastNoncreature_DoesNotFireForOpponentCast()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(NoncreatureCastJson, triggers);

        bus.Publish(NoncreatureCast(_bob));

        triggers.PendingCount.Should().Be(0, "'whenever YOU cast' is controller-scoped (CR 109.5)");
    }

    private const string AnyCastJson = """
    {
      "name": "Test Storyteller",
      "types": ["Creature"],
      "manaCost": "1R",
      "power": 2,
      "toughness": 2,
      "abilities": [
        {
          "kind": "triggered",
          "trigger": { "type": "whenever_you_cast_spell" },
          "effects": [ { "type": "put_counter", "counter": "+1/+1", "amount": 1, "target": "self" } ]
        }
      ]
    }
    """;

    [Fact]
    public void WheneverYouCastSpell_NoFilter_FiresForCreatureSpellToo()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(AnyCastJson, triggers);

        bus.Publish(CreatureCast(_alice));

        triggers.PendingCount.Should().Be(1, "with no filter, 'whenever you cast a spell' fires on any spell");
    }

    private const string EnchantmentCastJson = """
    {
      "name": "Test Generous Visitor",
      "types": ["Creature"],
      "manaCost": "1G",
      "power": 1,
      "toughness": 1,
      "abilities": [
        {
          "kind": "triggered",
          "trigger": { "type": "whenever_you_cast_spell", "spellTypes": ["Enchantment"] },
          "effects": [ { "type": "put_counter", "counter": "+1/+1", "amount": 1, "target": "self" } ]
        }
      ]
    }
    """;

    [Fact]
    public void WheneverYouCastTypedSpell_Fires_OnlyForMatchingType()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(EnchantmentCastJson, triggers);

        // An instant — not an enchantment — must not fire.
        bus.Publish(NoncreatureCast(_alice));
        triggers.PendingCount.Should().Be(0, "an instant is not an enchantment spell");

        // An enchantment spell — fires.
        var enchantment = new Enchantment("Test Aura", "1G") { Owner = _alice };
        bus.Publish(new SpellCastEvent(new Majik.Core.Spells.Spell(enchantment, _alice)));
        triggers.PendingCount.Should().Be(1, "an enchantment spell matches the spellTypes filter");
    }

    // ------------------------------------------------------------------
    // attacks_self — CR 508.1f.
    // ------------------------------------------------------------------

    private const string AttacksCounterJson = """
    {
      "name": "Test Ravine",
      "types": ["Creature"],
      "manaCost": "2R",
      "power": 3,
      "toughness": 3,
      "abilities": [
        {
          "kind": "triggered",
          "trigger": { "type": "attacks_self" },
          "effects": [ { "type": "put_counter", "counter": "+1/+1", "amount": 1, "target": "self" } ]
        }
      ]
    }
    """;

    [Fact]
    public void AttacksSelf_Fires_WhenThisAttacks_AddsCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var (_, card) = BuildAndRegister(AttacksCounterJson, triggers);

        bus.Publish(new CreatureAttacksEvent((Creature)card, _bob));

        triggers.PendingCount.Should().Be(1, "the creature attacking fires its own attack trigger (CR 508.1f)");
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveAll(stack);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void AttacksSelf_DoesNotFire_WhenAnotherCreatureAttacks()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(AttacksCounterJson, triggers);

        var other = new Creature("Other Attacker", "1R", 2, 2) { Owner = _alice };
        bus.Publish(new CreatureAttacksEvent(other, _bob));

        triggers.PendingCount.Should().Be(0, "'whenever THIS creature attacks' is per-attacker self-scoped");
    }

    // ------------------------------------------------------------------
    // dies_self — CR 700.4.
    // ------------------------------------------------------------------

    private const string DiesGainLifeJson = """
    {
      "name": "Test Haywire",
      "types": ["Artifact", "Creature"],
      "manaCost": "1",
      "power": 1,
      "toughness": 1,
      "abilities": [
        {
          "kind": "triggered",
          "trigger": { "type": "dies_self" },
          "effects": [ { "type": "gain_life_self", "amount": 2 } ]
        }
      ]
    }
    """;

    [Fact]
    public void DiesSelf_Fires_OnBattlefieldToGraveyard_GainsLife()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var (_, card) = BuildAndRegister(DiesGainLifeJson, triggers);

        // "Dies" = Battlefield → Graveyard (CR 700.4). ZoneService stamps the
        // card's zone to Graveyard before publishing, so the ability must stay
        // active in the Graveyard to be observable.
        card.SetZone(ZoneType.Graveyard);
        bus.Publish(new CardMovedEvent(card, ZoneType.Battlefield, ZoneType.Graveyard));

        triggers.PendingCount.Should().Be(1, "a Battlefield → Graveyard self-move is a death (CR 700.4)");
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveAll(stack);

        _alice.LifeTotal.Should().Be(22, "the dies trigger gains its controller 2 life (CR 119.3)");
    }

    [Fact]
    public void DiesSelf_DoesNotFire_OnBounceToHand()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var (_, card) = BuildAndRegister(DiesGainLifeJson, triggers);

        // Battlefield → Hand is NOT a death.
        card.SetZone(ZoneType.Hand);
        bus.Publish(new CardMovedEvent(card, ZoneType.Battlefield, ZoneType.Hand));

        triggers.PendingCount.Should().Be(0, "returning to hand is not dying (CR 700.4)");
    }
}
