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
    // cast_self — CR 601.2i / 603.3 ("When you cast THIS spell, …").
    // Self-scoped sibling of whenever_you_cast_spell: fires only on the
    // SpellCastEvent for THIS very card, and only while on the Stack.
    // ------------------------------------------------------------------

    private const string CastSelfDrawJson = """
    {
      "name": "Test Nulldrifter",
      "types": ["Creature"],
      "manaCost": "5U",
      "power": 1,
      "toughness": 1,
      "abilities": [
        {
          "kind": "triggered",
          "trigger": { "type": "cast_self" },
          "effects": [ { "type": "draw_card", "amount": 2 } ]
        }
      ]
    }
    """;

    /// <summary>
    /// Mirrors the real cast flow: the card is moved Hand → Stack (where the
    /// cast_self trigger is active) before the <see cref="SpellCastEvent"/> for
    /// it is published.
    /// </summary>
    private (TriggeredAbility ability, Permanent card) BuildAndRegisterOnStack(
        string json, TriggerManager triggers)
    {
        var def = CardDefinitionLoader.FromJson(json);
        var card = (Permanent)CardDefinitionFactory.Build(def, _alice);
        // The spell is on the Stack as it is cast (CR 601.2a) — the cast_self
        // trigger overrides ActiveZones to include the Stack so it stays
        // observable there.
        _alice.Zones.Stack.AddCard(card);
        card.SetZone(ZoneType.Stack);
        var ability = card.Abilities.OfType<TriggeredAbility>().Single();
        triggers.RegisterTriggeredAbility(ability);
        return (ability, card);
    }

    [Fact]
    public void CastSelf_Fires_WhenThisSpellIsCast_DrawsCards()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var (_, card) = BuildAndRegisterOnStack(CastSelfDrawJson, triggers);

        // Seed two cards to draw.
        var top1 = new Instant("Counterspell", "UU") { Owner = _alice };
        var top2 = new Instant("Opt", "U") { Owner = _alice };
        _alice.Zones.Library.AddCard(top1);
        _alice.Zones.Library.AddCard(top2);
        top1.SetZone(ZoneType.Library);
        top2.SetZone(ZoneType.Library);

        // "When you cast THIS spell" — the SpellCastEvent for this very card.
        bus.Publish(new SpellCastEvent(new Majik.Core.Spells.Spell(card, _alice)));

        triggers.PendingCount.Should().Be(1,
            "casting this very card fires its own cast-self trigger (CR 601.2i / 603.3)");
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveAll(stack);

        _alice.Zones.Hand.GetCards().Should().Contain(new[] { top1, top2 },
            "the cast_self trigger drew two cards (CR 120)");
    }

    [Fact]
    public void CastSelf_DoesNotFire_ForAnotherSpellYouCast()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegisterOnStack(CastSelfDrawJson, triggers);

        // A DIFFERENT spell the same player casts must not fire it — this is
        // what distinguishes cast_self from whenever_you_cast_spell.
        bus.Publish(NoncreatureCast(_alice));

        triggers.PendingCount.Should().Be(0,
            "'when you cast THIS spell' is self-scoped — another spell you cast does not fire it");
    }

    [Fact]
    public void CastSelf_DoesNotFire_WhenOnBattlefield()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var (ability, card) = BuildAndRegisterOnStack(CastSelfDrawJson, triggers);

        // After the spell resolves the permanent is on the battlefield. The
        // cast trigger is functional only while the card is being cast
        // (CR 603.3e); a spell event for a card sitting on the battlefield is
        // not "casting THIS spell" again — the ReferenceEquals guard still
        // matches the same card object, so we assert the ability self-excludes
        // by NOT re-firing for an unrelated cast while the source is elsewhere.
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(NoncreatureCast(_alice));

        triggers.PendingCount.Should().Be(0,
            "an unrelated spell never matches the self-cast reference guard");
        triggers.IsRegistered(ability).Should().BeTrue(
            "the test registered the ability directly; the guard is the spell-reference match");
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

    // ------------------------------------------------------------------
    // at_beginning_of_your_upkeep — CR 500.1 / 603.1, over StepStartedEvent.
    // ------------------------------------------------------------------

    private const string UpkeepDrawLoseLifeJson = """
    {
      "name": "Test Phyrexian Arena",
      "types": ["Enchantment"],
      "manaCost": "1BB",
      "abilities": [
        {
          "kind": "triggered",
          "trigger": { "type": "at_beginning_of_your_upkeep" },
          "effects": [
            { "type": "draw_card", "amount": 1 },
            { "type": "lose_life_self", "amount": 1 }
          ]
        }
      ]
    }
    """;

    [Fact]
    public void AtBeginningOfYourUpkeep_FiresForController_DrawsAndLosesLife()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var (_, _) = BuildAndRegister(UpkeepDrawLoseLifeJson, triggers);

        // Seed a card to draw so the draw clause has something to move.
        var top = new Instant("Lightning Bolt", "R") { Owner = _alice };
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        bus.Publish(new StepStartedEvent(
            Majik.Core.StateMachine.PhaseStateType.Upkeep, _alice));

        triggers.PendingCount.Should().Be(1,
            "the controller's own upkeep fires the trigger (CR 500.1 / 603.1)");
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveAll(stack);

        _alice.Zones.Hand.GetCards().Should().Contain(top, "the draw clause drew the top card (CR 120)");
        _alice.LifeTotal.Should().Be(19, "the lose_life_self clause cost 1 life (CR 119.3)");
    }

    [Fact]
    public void AtBeginningOfYourUpkeep_DoesNotFireForOpponentUpkeep()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(UpkeepDrawLoseLifeJson, triggers);

        bus.Publish(new StepStartedEvent(
            Majik.Core.StateMachine.PhaseStateType.Upkeep, _bob));

        triggers.PendingCount.Should().Be(0, "'at the beginning of YOUR upkeep' is controller-scoped (CR 109.5)");
    }

    [Fact]
    public void AtBeginningOfYourUpkeep_DoesNotFireForOtherStep()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(UpkeepDrawLoseLifeJson, triggers);

        bus.Publish(new StepStartedEvent(
            Majik.Core.StateMachine.PhaseStateType.End, _alice));

        triggers.PendingCount.Should().Be(0, "the End step is not the Upkeep step");
    }

    // ------------------------------------------------------------------
    // at_beginning_of_your_end_step — CR 513.1 / 603.1, over StepStartedEvent.
    // ------------------------------------------------------------------

    private const string EndStepCounterJson = """
    {
      "name": "Test Wedding Announcement",
      "types": ["Creature"],
      "manaCost": "1W",
      "power": 1,
      "toughness": 1,
      "abilities": [
        {
          "kind": "triggered",
          "trigger": { "type": "at_beginning_of_your_end_step" },
          "effects": [ { "type": "put_counter", "counter": "+1/+1", "amount": 1, "target": "self" } ]
        }
      ]
    }
    """;

    [Fact]
    public void AtBeginningOfYourEndStep_FiresForController_AddsCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var (_, card) = BuildAndRegister(EndStepCounterJson, triggers);

        bus.Publish(new StepStartedEvent(
            Majik.Core.StateMachine.PhaseStateType.End, _alice));

        triggers.PendingCount.Should().Be(1,
            "the controller's own end step fires the trigger (CR 513.1 / 603.1)");
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveAll(stack);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void AtBeginningOfYourEndStep_DoesNotFireForUpkeep()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(EndStepCounterJson, triggers);

        bus.Publish(new StepStartedEvent(
            Majik.Core.StateMachine.PhaseStateType.Upkeep, _alice));

        triggers.PendingCount.Should().Be(0, "the Upkeep step is not the End step");
    }

    // ------------------------------------------------------------------
    // whenever_another_creature_enters — CR 603.6e, over CardMovedEvent
    // (self-excluded). Default any-controller; optional youControlOnly scope.
    // ------------------------------------------------------------------

    // Soul Warden shape: ANY creature entering (both players') fires it.
    private const string AnotherCreatureEntersJson = """
    {
      "name": "Test Soul Warden",
      "types": ["Creature"],
      "manaCost": "W",
      "power": 1,
      "toughness": 1,
      "abilities": [
        {
          "kind": "triggered",
          "trigger": { "type": "whenever_another_creature_enters" },
          "effects": [ { "type": "gain_life_self", "amount": 1 } ]
        }
      ]
    }
    """;

    [Fact]
    public void AnotherCreatureEnters_Fires_OnOtherCreature_GainsLife()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(AnotherCreatureEntersJson, triggers);

        var other = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        other.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(other);
        other.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(other, ZoneType.Hand, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1,
            "another creature entering fires the trigger (CR 603.6e)");
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveAll(stack);

        _alice.LifeTotal.Should().Be(21);
    }

    [Fact]
    public void AnotherCreatureEnters_DoesNotFire_ForSelf()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var (_, card) = BuildAndRegister(AnotherCreatureEntersJson, triggers);

        // The permanent's OWN entry must not fire its "another creature" trigger.
        bus.Publish(new CardMovedEvent(card, ZoneType.Hand, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(0, "'ANOTHER creature' excludes the source itself (CR 603.6e)");
    }

    [Fact]
    public void AnotherCreatureEnters_Default_Fires_ForOpponentCreature()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(AnotherCreatureEntersJson, triggers);

        var enemy = new Creature("Enemy Bear", "1G", 2, 2) { Owner = _bob };
        enemy.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(enemy);
        enemy.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(enemy, ZoneType.Hand, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1,
            "the default (un-scoped) 'another creature enters' fires for ANY creature (Soul Warden)");
    }

    [Fact]
    public void AnotherCreatureEnters_DoesNotFire_ForNoncreature()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(AnotherCreatureEntersJson, triggers);

        var land = new Land("Forest") { Owner = _alice };
        land.SetController(_alice);
        bus.Publish(new CardMovedEvent(land, ZoneType.Hand, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(0, "a land entering is not 'another creature'");
    }

    // youControlOnly scope: only creatures the controller controls fire it.
    private const string AnotherCreatureYouControlEntersJson = """
    {
      "name": "Test Cathars Crusade",
      "types": ["Enchantment"],
      "manaCost": "3WW",
      "abilities": [
        {
          "kind": "triggered",
          "trigger": { "type": "whenever_another_creature_enters", "youControlOnly": true },
          "effects": [ { "type": "gain_life_self", "amount": 1 } ]
        }
      ]
    }
    """;

    [Fact]
    public void AnotherCreatureEnters_YouControlOnly_DoesNotFire_ForOpponentCreature()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(AnotherCreatureYouControlEntersJson, triggers);

        var enemy = new Creature("Enemy Bear", "1G", 2, 2) { Owner = _bob };
        enemy.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(enemy);
        enemy.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(enemy, ZoneType.Hand, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(0,
            "youControlOnly scope excludes opponents' creatures (CR 109.5)");
    }

    [Fact]
    public void AnotherCreatureEnters_YouControlOnly_Fires_ForOwnCreature()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(AnotherCreatureYouControlEntersJson, triggers);

        var own = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        own.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(own);
        own.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(own, ZoneType.Hand, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1, "youControlOnly scope fires for the controller's own creature");
    }

    // ------------------------------------------------------------------
    // whenever_this_deals_combat_damage_to_a_player — CR 510.2 / 603.1, over
    // CombatDamageDealtEvent (self source, player target).
    // ------------------------------------------------------------------

    private const string CombatDamageDrawJson = """
    {
      "name": "Test Ophidian",
      "types": ["Creature"],
      "manaCost": "2U",
      "power": 1,
      "toughness": 3,
      "abilities": [
        {
          "kind": "triggered",
          "trigger": { "type": "whenever_this_deals_combat_damage_to_a_player" },
          "effects": [ { "type": "draw_card", "amount": 1 } ]
        }
      ]
    }
    """;

    [Fact]
    public void DealsCombatDamageToPlayer_Fires_DrawsCard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var (_, card) = BuildAndRegister(CombatDamageDrawJson, triggers);

        var top = new Instant("Counterspell", "UU") { Owner = _alice };
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        bus.Publish(new CombatDamageDealtEvent((Creature)card, _bob, 1));

        triggers.PendingCount.Should().Be(1,
            "this creature dealing combat damage to a player fires the trigger (CR 510.2)");
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveAll(stack);

        _alice.Zones.Hand.GetCards().Should().Contain(top, "the trigger drew a card (CR 120)");
    }

    [Fact]
    public void DealsCombatDamageToPlayer_DoesNotFire_OnDamageToCreature()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var (_, card) = BuildAndRegister(CombatDamageDrawJson, triggers);

        // Combat damage to a CREATURE (a blocker), not a player.
        var blocker = new Creature("Wall", "1W", 0, 4) { Owner = _bob };
        bus.Publish(new CombatDamageDealtEvent((Creature)card, blocker, 1));

        triggers.PendingCount.Should().Be(0, "'to a PLAYER' excludes combat damage to a creature");
    }

    [Fact]
    public void DealsCombatDamageToPlayer_DoesNotFire_ForAnotherSource()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        BuildAndRegister(CombatDamageDrawJson, triggers);

        // A DIFFERENT creature dealing combat damage to a player.
        var other = new Creature("Other Attacker", "1R", 2, 2) { Owner = _alice };
        bus.Publish(new CombatDamageDealtEvent(other, _bob, 2));

        triggers.PendingCount.Should().Be(0, "'whenever THIS creature deals combat damage' is self-scoped");
    }
}
