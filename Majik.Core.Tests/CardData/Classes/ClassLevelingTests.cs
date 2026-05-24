using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Classes;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Classes;

/// <summary>
/// End-to-end tests for CR 716 — Class enchantment leveling — via
/// Stormchaser's Talent (Modern Horizons 3, {U}{R}).
///
/// Covers:
/// - Level-up activated abilities are present on a freshly-built Class
///   (Level 2 + Level 3 — both <c>sorcerySpeed: true</c>).
/// - Activating the Level-2 ability with sufficient mana advances
///   <see cref="ClassState.CurrentLevel"/> to 2.
/// - CR 716.4 sequential gate — Level-3 from level-1 is rejected (mana not
///   spent, level not advanced).
/// - Level-2 trigger fires on noncreature spell cast after leveling up
///   ("the Mercenary deals 1 damage to any target" — v1 deterministic
///   resolver) — at level 1 the same cast does NOT trigger.
/// - Level-3 trigger fires on noncreature spell cast after leveling to 3
///   (loot body — draw + discard) and not before.
/// - Sorcery-speed gate — <see cref="ActionValidator"/> rejects a level-up
///   activation when <c>sorcerySpeedAvailable</c> is false (opponent's turn
///   / non-empty stack).
/// - Insufficient mana → <see cref="ActivatedAbility.Costs"/>.CanPay rejects.
/// - <see cref="ClassLevelUpEvent"/> is published on level-up when an event
///   bus is wired through.
/// </summary>
public class ClassLevelingTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Bolt")
    {
        var instant = new Instant(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name = "Bear")
    {
        var creature = new Creature(name, "1G", 2, 2) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    private (Enchantment Card, ClassState State, Majik.Core.Stack.Stack Stack, TriggerManager Triggers)
        WireStormchasersTalent(Func<Player>? opponentResolver = null)
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var card = StormchasersTalentFactory.Create(
            _alice,
            zoneService: zones,
            triggers: triggers,
            eventBus: _bus,
            opponentResolver: opponentResolver ?? (() => _bob));

        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);
        triggers.BindCard(card);

        var state = ((Permanent)card).ClassState!;
        state.Should().NotBeNull();
        return (card, state, stack, triggers);
    }

    // -------------------------------------------------------------------
    // Shape
    // -------------------------------------------------------------------

    [Fact]
    public void StormchasersTalent_HasTwoLevelUpActivatedAbilities_BothSorcerySpeed()
    {
        var (card, _, _, _) = WireStormchasersTalent();

        var levelUps = card.Abilities.OfType<ActivatedAbility>().ToList();
        levelUps.Should().HaveCount(2,
            "CR 716 — one level-up activated ability per level above 1 (Levels 2 and 3)");
        levelUps.Should().OnlyContain(a => a.IsSorcerySpeed,
            "CR 716.3 — Class level-up activated abilities are sorcery-speed only");
    }

    [Fact]
    public void StormchasersTalent_ClassState_StartsAtLevelOne_With_3MaxLevel()
    {
        var (_, state, _, _) = WireStormchasersTalent();

        state.CurrentLevel.Should().Be(1, "CR 716.2 — Class enchantments enter as level 1");
        state.MaxLevel.Should().Be(3, "Stormchaser's Talent prints Level 2 and Level 3");
        state.LevelUpCosts.Should().HaveCount(2);
        state.CostFor(2).Should().Be(ManaCost.Parse("{1}{U}{R}"));
        state.CostFor(3).Should().Be(ManaCost.Parse("{3}{U}{R}"));
    }

    // -------------------------------------------------------------------
    // Level-up — happy path
    // -------------------------------------------------------------------

    [Fact]
    public void ActivatingLevelTwo_With_OneUR_Mana_AdvancesCurrentLevelToTwo()
    {
        var (card, state, _, _) = WireStormchasersTalent();
        var levelTwo = card.Abilities.OfType<ActivatedAbility>().First();

        // {1}{U}{R} — pay mana via the pool, then run the resolution body
        // exactly like AbilityActivationFlow does after cost payment.
        _alice.AddManaToPool(ManaCost.Parse("{1}{U}{R}"));
        foreach (var cost in levelTwo.Costs) cost.Pay(_alice);
        levelTwo.Resolve();

        state.CurrentLevel.Should().Be(2,
            "CR 716.4 — the level-up activated ability advances the Class one level");
    }

    [Fact]
    public void ActivatingLevelTwo_PublishesClassLevelUpEvent()
    {
        ClassLevelUpEvent? captured = null;
        _bus.Subscribe<ClassLevelUpEvent>(e => captured = e);

        var (card, _, _, _) = WireStormchasersTalent();
        var levelTwo = card.Abilities.OfType<ActivatedAbility>().First();

        _alice.AddManaToPool(ManaCost.Parse("{1}{U}{R}"));
        foreach (var cost in levelTwo.Costs) cost.Pay(_alice);
        levelTwo.Resolve();

        captured.Should().NotBeNull("OnLevelUp must publish a ClassLevelUpEvent when a bus is wired");
        captured!.FromLevel.Should().Be(1);
        captured.ToLevel.Should().Be(2);
        captured.Source.Should().BeSameAs(card);
        captured.Controller.Should().BeSameAs(_alice);
    }

    // -------------------------------------------------------------------
    // CR 716.4 — sequential gate
    // -------------------------------------------------------------------

    [Fact]
    public void ActivatingLevelThree_FromLevelOne_DoesNotAdvance_SequentialGate()
    {
        var (card, state, _, _) = WireStormchasersTalent();
        var levelThree = card.Abilities.OfType<ActivatedAbility>().Skip(1).First();

        // Pay {3}{U}{R} — the cost would resolve, but the resolution body
        // re-checks ClassState.CanLevelUpTo(3) which fails when
        // CurrentLevel == 1. The Class stays at 1.
        _alice.AddManaToPool(ManaCost.Parse("{3}{U}{R}"));
        foreach (var cost in levelThree.Costs) cost.Pay(_alice);
        levelThree.Resolve();

        state.CurrentLevel.Should().Be(1,
            "CR 716.4 — can't skip from level 1 to level 3; the resolution no-ops");
    }

    [Fact]
    public void ClassState_LevelUpTo_RejectsSkippingLevels()
    {
        var state = new ClassState(maxLevel: 3, levelUpCosts: new[]
        {
            ManaCost.Parse("{1}{U}{R}"),
            ManaCost.Parse("{3}{U}{R}"),
        });

        state.CanLevelUpTo(3).Should().BeFalse(
            "CurrentLevel == 1 — only level 2 is reachable in one step");
        state.LevelUpTo(3).Should().BeFalse();
        state.CurrentLevel.Should().Be(1, "skipping must not mutate state");
    }

    // -------------------------------------------------------------------
    // Level-2 trigger — "the Mercenary deals 1 damage to any target"
    // -------------------------------------------------------------------

    [Fact]
    public void Level2CastTrigger_FiresAfterLevelUp_DealsOneToOpponent()
    {
        var (card, state, stack, triggers) = WireStormchasersTalent();

        // Resolve ETB so the Mercenary token spawns.
        var etbTrigger = card.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etbTrigger.Effects) effect.Execute();

        // Level up to 2.
        var levelTwo = card.Abilities.OfType<ActivatedAbility>().First();
        _alice.AddManaToPool(ManaCost.Parse("{1}{U}{R}"));
        foreach (var cost in levelTwo.Costs) cost.Pay(_alice);
        levelTwo.Resolve();
        state.CurrentLevel.Should().Be(2);

        var lifeBefore = _bob.LifeTotal;

        // Publish a noncreature spell cast — the level-2 trigger should fire.
        _bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));
        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
            "the level-2 noncreature-spell trigger must queue");

        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Pop() is { } obj) obj.Resolve();

        _bob.LifeTotal.Should().Be(lifeBefore - 1,
            "the Mercenary deals 1 damage to the opponent (v1 deterministic any-target resolver)");
    }

    [Fact]
    public void Level2CastTrigger_DoesNotFire_AtLevelOne_InterveningIfGate()
    {
        var (_, state, stack, triggers) = WireStormchasersTalent();
        state.CurrentLevel.Should().Be(1);

        var lifeBefore = _bob.LifeTotal;

        _bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));

        // The condition matches but the interveningIf returns false → the
        // trigger does NOT queue. (CR 603.4 — intervening-if checked on
        // event delivery.)
        triggers.PendingCount.Should().Be(0,
            "interveningIf gates the level-2 trigger to CurrentLevel >= 2");
        _bob.LifeTotal.Should().Be(lifeBefore);
    }

    [Fact]
    public void Level2CastTrigger_DoesNotFire_OnCreatureSpell()
    {
        var (card, state, _, triggers) = WireStormchasersTalent();

        // Level up so the interveningIf gate passes.
        var levelTwo = card.Abilities.OfType<ActivatedAbility>().First();
        _alice.AddManaToPool(ManaCost.Parse("{1}{U}{R}"));
        foreach (var cost in levelTwo.Costs) cost.Pay(_alice);
        levelTwo.Resolve();
        state.CurrentLevel.Should().Be(2);

        _bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0,
            "the trigger filter is 'noncreature spell' — creature spells must not queue it");
    }

    // -------------------------------------------------------------------
    // Level-3 trigger — "draw a card, then discard a card"
    // -------------------------------------------------------------------

    [Fact]
    public void Level3CastTrigger_FiresAfterLevelingToThree_LootsOneCard()
    {
        var (card, state, stack, triggers) = WireStormchasersTalent();

        // Level 1 → 2 → 3.
        var levelTwo = card.Abilities.OfType<ActivatedAbility>().First();
        _alice.AddManaToPool(ManaCost.Parse("{1}{U}{R}"));
        foreach (var cost in levelTwo.Costs) cost.Pay(_alice);
        levelTwo.Resolve();

        var levelThree = card.Abilities.OfType<ActivatedAbility>().Skip(1).First();
        _alice.AddManaToPool(ManaCost.Parse("{3}{U}{R}"));
        foreach (var cost in levelThree.Costs) cost.Pay(_alice);
        levelThree.Resolve();
        state.CurrentLevel.Should().Be(3);

        // Seed library with one card so the draw resolves; seed hand with
        // a discardable card so the loot's discard step has a victim.
        var libCard = new Card("LibraryCard", "");
        libCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(libCard);
        libCard.SetZone(ZoneType.Library);

        var handCard = new Card("HandCard", "");
        handCard.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(handCard);
        handCard.SetZone(ZoneType.Hand);

        _bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Bolt")));

        // Both the level-2 and level-3 triggers' interveningIf are
        // satisfied at level 3 — both queue.
        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(2);
        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Pop() is { } obj) obj.Resolve();

        // After loot: the library is empty (the one card got drawn) and
        // *some* card has been discarded into the graveyard.
        _alice.Zones.Library.GetCards().Should().BeEmpty(
            "Level-3 loot draws one card from the library");
        _alice.Zones.Graveyard.GetCards().Should().NotBeEmpty(
            "Level-3 loot discards one card after the draw");
    }

    // -------------------------------------------------------------------
    // Sorcery-speed gate (PR #460 — ActionValidator)
    // -------------------------------------------------------------------

    [Fact]
    public void LevelUpActivation_RejectedOnOpponentsTurn_BySorcerySpeedGate()
    {
        var (card, _, _, _) = WireStormchasersTalent();
        var levelTwo = card.Abilities.OfType<ActivatedAbility>().First();

        // sorcerySpeedAvailable: false models "opponent's turn / non-empty
        // stack" — CR 117.1a / 307.5 closes the sorcery-speed window.
        var action = new ActivateAbilityAction(levelTwo, _alice, sorcerySpeedAvailable: false);
        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeFalse(
            "Class level-up activated abilities are sorcery-speed only (CR 716.3 / 307.5)");
        result.Violation!.RuleNumber.Should().Be("307.5");
    }

    [Fact]
    public void LevelUpActivation_LegalAtSorcerySpeed()
    {
        var (card, _, _, _) = WireStormchasersTalent();
        var levelTwo = card.Abilities.OfType<ActivatedAbility>().First();

        var action = new ActivateAbilityAction(levelTwo, _alice, sorcerySpeedAvailable: true);
        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeTrue(
            "the controller's main phase + empty stack opens the sorcery-speed window");
    }

    // -------------------------------------------------------------------
    // Insufficient mana
    // -------------------------------------------------------------------

    [Fact]
    public void LevelUpActivation_Rejected_WhenManaCannotBePaid()
    {
        var (card, _, _, _) = WireStormchasersTalent();
        var levelTwo = card.Abilities.OfType<ActivatedAbility>().First();

        // Empty mana pool — the cost's CanPay must reject.
        levelTwo.Costs.Should().ContainSingle();
        var cost = levelTwo.Costs.Single();
        cost.CanPay(_alice).Should().BeFalse(
            "{1}{U}{R} can't be paid from an empty pool");
    }

    [Fact]
    public void LevelUpActivation_LegalWhen_ExactManaIsAvailable()
    {
        var (card, _, _, _) = WireStormchasersTalent();
        var levelTwo = card.Abilities.OfType<ActivatedAbility>().First();

        _alice.AddManaToPool(ManaCost.Parse("{1}{U}{R}"));
        levelTwo.Costs.Single().CanPay(_alice).Should().BeTrue();
    }
}
