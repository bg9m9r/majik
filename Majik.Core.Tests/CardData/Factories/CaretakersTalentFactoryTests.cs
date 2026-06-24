using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Classes;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CaretakersTalentFactory"/> (Bloomburrow, {2}{W}).
///
/// Enchantment — Class {2}{W}. Oracle text (verified against Scryfall):
///   "(Gain the next level as a sorcery to add its ability.)
///    Whenever one or more tokens you control enter, draw a card. This
///      ability triggers only once each turn.
///    {W}: Level 2
///    When this Class becomes level 2, create a token that's a copy of
///      target token you control.
///    {3}{W}: Level 3
///    Creature tokens you control get +2/+2."
///
/// Covers the card's UNIQUE behaviour:
/// - Class state binder: Level 1, MaxLevel 3, per-level costs {W} / {3}{W}.
/// - Level-1 token-ETB draw trigger (once each turn): a token you control
///   entering draws a card; a SECOND token the same turn does not re-trigger;
///   a new turn re-arms it. Opponent tokens never trigger it.
/// - Level-2 "becomes level 2" copy-token trigger (CR 716.2d): only queues
///   when the Class advances TO level 2; copies the chosen token you control.
/// - Level-3 anthem: creature tokens you control get +2/+2 only at level 3;
///   nontoken creatures + opponents' tokens are unaffected.
/// </summary>
[Trait("Color", "W")]
public class CaretakersTalentFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity (single *_Identity assert — non-vanilla mana cost + subtype)
    // -----------------------------------------------------------------------

    [Fact]
    public void CaretakersTalent_Identity_EnchantmentClass_TwoWhite()
    {
        var c = CaretakersTalentFactory.Create(_alice);
        c.Name.Should().Be("Caretaker's Talent");
        c.HasType(CardType.Enchantment).Should().BeTrue("printed oracle is Enchantment — Class");
        c.HasSubtype(CardSubtype.Class).Should().BeTrue(
            "CR 205.3h — Class is an enchantment subtype (CR 716)");
        c.ManaCost.Should().Be("{2}{W}");

        var state = ((Permanent)c).ClassState;
        state.Should().NotBeNull("CR 716 — Class enchantments carry a leveling tracker");
        state!.CurrentLevel.Should().Be(1);
        state.MaxLevel.Should().Be(3);
        state.CostFor(2).Should().Be(ManaCost.Parse("{W}"));
        state.CostFor(3).Should().Be(ManaCost.Parse("{3}{W}"));
    }

    // -----------------------------------------------------------------------
    // Shape
    // -----------------------------------------------------------------------

    [Fact]
    public void CaretakersTalent_HasTwoLevelUpActivatedAbilities_BothSorcerySpeed()
    {
        var c = CaretakersTalentFactory.Create(_alice);
        var levelUps = c.Abilities.OfType<ActivatedAbility>().ToList();
        levelUps.Should().HaveCount(2,
            "CR 716 — one level-up activated ability per level above 1 ({W}: Level 2 / {3}{W}: Level 3)");
        levelUps.Should().OnlyContain(a => a.IsSorcerySpeed,
            "CR 716.3 — Class level-up activations are sorcery-speed only");
    }

    // -----------------------------------------------------------------------
    // Level-1 token-ETB draw trigger (once each turn)
    // -----------------------------------------------------------------------

    [Fact]
    public void CaretakersTalent_LevelOne_TokenEnter_DrawsACard()
    {
        var (card, _, stack, triggers) = Wire();
        SeedLibrary(_alice, 3);

        EnterToken(_alice, triggers, stack);

        _alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "a token you control entering draws one card (CR 603.1)");
    }

    [Fact]
    public void CaretakersTalent_LevelOne_OnlyOncePerTurn_SecondTokenDoesNotDraw()
    {
        var (card, _, stack, triggers) = Wire();
        SeedLibrary(_alice, 3);

        EnterToken(_alice, triggers, stack);
        _alice.Zones.Hand.GetCards().Should().HaveCount(1);

        // Second token the SAME turn — no re-trigger (CR 603.2c "only once each turn").
        triggers.PendingCount.Should().Be(0, "the ability already fired this turn");
        EnterToken(_alice, triggers, stack);
        _alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "the once-per-turn clause suppresses the second token's draw");
    }

    [Fact]
    public void CaretakersTalent_LevelOne_NewTurn_ReArmsTheOncePerTurnTrigger()
    {
        var (card, _, stack, triggers) = Wire();
        SeedLibrary(_alice, 3);

        EnterToken(_alice, triggers, stack);
        _alice.Zones.Hand.GetCards().Should().HaveCount(1);

        // CR 500.1 — a new turn re-arms the once-per-turn ability.
        _bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        EnterToken(_alice, triggers, stack);
        _alice.Zones.Hand.GetCards().Should().HaveCount(2,
            "the new turn re-armed the trigger so the next token draws again");
    }

    [Fact]
    public void CaretakersTalent_LevelOne_OpponentToken_DoesNotTrigger()
    {
        var (_, _, _, triggers) = Wire();

        var token = MakeToken(_bob);
        token.SetZone(ZoneType.Library);
        _bus.Publish(new CardMovedEvent(token, ZoneType.Library, ZoneType.Battlefield, _bob));

        triggers.PendingCount.Should().Be(0,
            "only tokens YOU control trigger the draw (CR 603.1 'you control')");
    }

    // -----------------------------------------------------------------------
    // Level-2 "becomes level 2" copy-token trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void CaretakersTalent_BecomesLevelTwo_CopiesTargetToken()
    {
        var (card, state, stack, triggers) = Wire();

        // A token already on Alice's battlefield to copy.
        var original = MakeToken(_alice, name: "Squirrel", power: 1, toughness: 1);
        original.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(original);

        state.LevelUpTo(2); // publishes ClassLevelUpEvent(to: 2)

        triggers.PendingCount.Should().Be(1,
            "CR 716.2d — 'when this Class becomes level 2' queues its triggered ability");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var squirrels = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Squirrel")
            .ToList();
        squirrels.Should().HaveCount(2,
            "the level-2 trigger creates a token copy of the chosen token you control");
    }

    [Fact]
    public void CaretakersTalent_BecomesLevelThree_DoesNotQueueTheCopyTrigger()
    {
        var (card, state, _, triggers) = Wire();

        var original = MakeToken(_alice);
        original.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(original);

        state.LevelUpTo(2);
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice); // drain the level-2 trigger

        state.LevelUpTo(3);
        triggers.PendingCount.Should().Be(0,
            "the copy trigger is gated to 'becomes level 2' — leveling to 3 doesn't re-fire it");
    }

    // -----------------------------------------------------------------------
    // Level-3 anthem
    // -----------------------------------------------------------------------

    [Fact]
    public void CaretakersTalent_LevelThree_CreatureTokensGetPlusTwo_OnlyAtLevelThree()
    {
        var continuous = new ContinuousEffectsService();
        var (card, state, _, _) = Wire(continuous);

        var token = MakeToken(_alice, name: "Beast", power: 2, toughness: 2);
        token.SetZone(ZoneType.Battlefield);
        token.ActiveEffects = continuous;
        _alice.Zones.Battlefield.AddCard(token);

        // Level 1/2 — no anthem yet.
        token.Power.Should().Be(2, "the +2/+2 anthem is gated on level 3");

        state.LevelUpTo(2);
        state.LevelUpTo(3);

        token.Power.Should().Be(4, "at level 3, creature tokens you control get +2/+2");
        token.Toughness.Should().Be(4);
    }

    [Fact]
    public void CaretakersTalent_LevelThree_DoesNotBuffNontokenCreatures()
    {
        var continuous = new ContinuousEffectsService();
        var (card, state, _, _) = Wire(continuous);
        state.LevelUpTo(2);
        state.LevelUpTo(3);

        var real = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        real.SetZone(ZoneType.Battlefield);
        real.ActiveEffects = continuous;
        _alice.Zones.Battlefield.AddCard(real);

        real.Power.Should().Be(2,
            "the anthem only buffs creature TOKENS (CR 111), not nontoken creatures");
    }

    [Fact]
    public void CaretakersTalent_LevelThree_DoesNotBuffOpponentTokens()
    {
        var continuous = new ContinuousEffectsService();
        var (card, state, _, _) = Wire(continuous);
        state.LevelUpTo(2);
        state.LevelUpTo(3);

        var enemy = MakeToken(_bob, name: "Goblin", power: 1, toughness: 1);
        enemy.SetZone(ZoneType.Battlefield);
        enemy.ActiveEffects = continuous;
        _bob.Zones.Battlefield.AddCard(enemy);

        enemy.Power.Should().Be(1,
            "the anthem is scoped to tokens YOU control, not an opponent's");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature MakeToken(
        Player controller, string name = "Token", int power = 1, int toughness = 1)
    {
        return new Creature(name, manaCost: "", power: power, toughness: toughness)
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
        };
    }

    private void EnterToken(Player controller, TriggerManager triggers, Majik.Core.Stack.Stack stack)
    {
        var token = MakeToken(controller);
        token.SetZone(ZoneType.Library);
        controller.Zones.Battlefield.AddCard(token);
        token.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(token, ZoneType.Library, ZoneType.Battlefield, controller));

        if (triggers.PendingCount > 0)
        {
            triggers.PutPendingTriggersOnStack(controller);
            stack.Pop()!.Resolve();
        }
    }

    private static void SeedLibrary(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Majik.Core.Cards.Instant($"Card{i}", "U") { Owner = p };
            c.SetZone(ZoneType.Library);
            p.Zones.Library.AddCard(c);
        }
    }

    private (Enchantment Card, ClassState State, Majik.Core.Stack.Stack Stack, TriggerManager Triggers) Wire(
        ContinuousEffectsService? continuous = null)
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var card = CaretakersTalentFactory.Create(_alice, triggers, _bus, continuous, zoneService: null);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);
        triggers.BindCard(card);

        var state = ((Permanent)card).ClassState!;
        return (card, state, stack, triggers);
    }
}
