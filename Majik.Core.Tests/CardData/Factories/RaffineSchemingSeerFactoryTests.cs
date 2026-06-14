using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="RaffineSchemingSeerFactory"/> (Streets of New Capenna,
/// {W}{U}{B}). Legendary Creature — Sphinx Demon 1/4.
///
/// Oracle (Scryfall-confirmed):
///   "Flying, ward {1}
///    Whenever you attack, target attacking creature connives X, where X is
///    the number of attacking creatures."
///
/// Covers identity, keyword markers, attack-trigger gating, and the dynamic-X
/// connive (X = attacking creatures declared this turn, read off the live
/// GameContext.TurnState).
/// </summary>
[Trait("Color", "WUB")]
public class RaffineSchemingSeerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeAttacker(Player owner, string name)
    {
        var c = new Creature(name, "{1}{W}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static TriggeredAbility GetAttackTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<AttackersDeclaredEvent>);

    [Fact]
    public void Identity_LegendarySphinxDemon_1_4()
    {
        var card = RaffineSchemingSeerFactory.Create(_alice);
        card.Name.Should().Be("Raffine, Scheming Seer");
        card.ManaCost.Should().Be("{W}{U}{B}");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Sphinx).Should().BeTrue();
        card.HasSubtype(CardSubtype.Demon).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(4);
    }

    [Fact]
    public void DispatchesViaNamedCardFactory_WithFlyingAndWard()
    {
        var card = (Creature)NamedCardFactory.Create("Raffine, Scheming Seer", _alice);
        card.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain(new[] { "Flying", "Ward" });
        card.Abilities.OfType<TriggeredAbility>().Should().ContainSingle();
    }

    [Fact]
    public void AttackTrigger_FiresWhenYouAttack()
    {
        var card = (Creature)NamedCardFactory.Create("Raffine, Scheming Seer", _alice);
        card.SetZone(ZoneType.Battlefield);
        var trigger = GetAttackTrigger(card);

        var atk = MakeAttacker(_alice, "Soldier");
        var combat = new Majik.Core.Combat.Combat(_alice, _bob);
        combat.AddAttacker(new Majik.Core.Combat.Attacker(atk, _bob));

        trigger.IsTriggered(new AttackersDeclaredEvent(combat)).Should().BeTrue();
    }

    [Fact]
    public void AttackTrigger_DoesNotFireOnOpponentAttack()
    {
        var card = (Creature)NamedCardFactory.Create("Raffine, Scheming Seer", _alice);
        card.SetZone(ZoneType.Battlefield);
        var trigger = GetAttackTrigger(card);

        var bobAtk = MakeAttacker(_bob, "Goblin");
        var combat = new Majik.Core.Combat.Combat(_bob, _alice);
        combat.AddAttacker(new Majik.Core.Combat.Attacker(bobAtk, _alice));

        trigger.IsTriggered(new AttackersDeclaredEvent(combat)).Should().BeFalse();
    }

    [Fact]
    public void ConnivesX_EqualToAttackersDeclaredThisTurn_ReadFromLiveTurnState()
    {
        var card = (Creature)NamedCardFactory.Create("Raffine, Scheming Seer", _alice);
        card.SetZone(ZoneType.Battlefield);
        var trigger = GetAttackTrigger(card);

        // Two attackers declared this turn.
        var a1 = MakeAttacker(_alice, "Soldier A");
        var a2 = MakeAttacker(_alice, "Soldier B");
        var combat = new Majik.Core.Combat.Combat(_alice, _bob);
        combat.AddAttacker(new Majik.Core.Combat.Attacker(a1, _bob));
        combat.AddAttacker(new Majik.Core.Combat.Attacker(a2, _bob));

        // Fire the trigger condition so it captures the combat (the default
        // target picks the first controller-controlled attacker).
        trigger.IsTriggered(new AttackersDeclaredEvent(combat)).Should().BeTrue();

        // Library: 2 nonlands so the X=2 connive can draw + discard them.
        for (var i = 0; i < 2; i++)
        {
            var spell = new Creature("Spell", "{1}", 1, 1);
            spell.SetOwner(_alice);
            spell.SetController(_alice);
            spell.SetZone(ZoneType.Library);
            _alice.Zones.Library.AddCard(spell);
        }

        // Live TurnState records 2 attackers declared this turn (= X).
        var ts = new TurnState();
        ts.RecordAttackersDeclared(2);
        var ctx = new GameContext(
            self: _alice,
            allPlayers: new[] { _alice, _bob },
            activePlayer: _alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(),
            landPlayAvailable: true,
            turnState: ts);

        ResolveTrigger(trigger, ctx);

        // X = 2: the chosen/first attacker (a1) connives 2 nonlands → 2 counters.
        a1.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "connive X = 2 attacking creatures (read live off rc.Game.TurnState)");
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void ConnivesX_OnExtraCombat_UsesCurrentCombatCount_NotTurnSum()
    {
        // CR 508.1 — "X = the number of attacking creatures" is scoped to the
        // CURRENT combat. On an extra-combat turn (Aggravated Assault etc.) the
        // second trigger must connive X = the second combat's attacker count,
        // NOT the cumulative sum of both combats (which would over-count).
        var card = (Creature)NamedCardFactory.Create("Raffine, Scheming Seer", _alice);
        card.SetZone(ZoneType.Battlefield);
        var trigger = GetAttackTrigger(card);

        // Second combat declares a single attacker.
        var a1 = MakeAttacker(_alice, "Soldier A");
        var combat = new Majik.Core.Combat.Combat(_alice, _bob);
        combat.AddAttacker(new Majik.Core.Combat.Attacker(a1, _bob));
        trigger.IsTriggered(new AttackersDeclaredEvent(combat)).Should().BeTrue();

        // Library: 3 nonlands — enough to over-draw if X mistakenly read the
        // turn sum (3) instead of the current-combat count (1).
        for (var i = 0; i < 3; i++)
        {
            var spell = new Creature("Spell", "{1}", 1, 1);
            spell.SetOwner(_alice);
            spell.SetController(_alice);
            spell.SetZone(ZoneType.Library);
            _alice.Zones.Library.AddCard(spell);
        }

        // TurnState models an extra-combat turn: first combat had 2 attackers
        // (turn sum = 3), but the current combat (after BeginCombat reset) has 1.
        var ts = new TurnState();
        ts.BeginCombat();
        ts.RecordAttackersDeclared(2); // first combat
        ts.BeginCombat();              // CR 506.4 — extra combat begins, reset per-combat
        ts.RecordAttackersDeclared(1); // second combat
        ts.AttackersDeclaredThisTurn.Should().Be(3);
        ts.AttackersDeclaredThisCombat.Should().Be(1);

        var ctx = new GameContext(
            self: _alice,
            allPlayers: new[] { _alice, _bob },
            activePlayer: _alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(),
            landPlayAvailable: true,
            turnState: ts);

        ResolveTrigger(trigger, ctx);

        // X = 1 (current combat), NOT 3 (turn sum): a1 connives 1 → 1 counter.
        a1.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "connive X = attackers in the CURRENT combat (CR 508.1), not the turn sum");
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(1);
        _alice.Zones.Library.GetCards().Should().HaveCount(2,
            "only 1 card drawn+discarded, not the over-counted 3");
    }

    private static void ResolveTrigger(TriggeredAbility trigger, GameContext ctx) =>
        trigger.ResolveAsync(agent: null, game: ctx).AsTask().GetAwaiter().GetResult();
}
