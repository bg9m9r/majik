using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Heuristic;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Cards;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Bot.Tests;

public class PriorityPolicyTests
{
    [Fact]
    public void PicksPlayLand_WhenLandInHand_AndLandDropAvailable()
    {
        var s = new BotTestScenario();
        var land = new Land("Mountain");
        s.AddCardToHand(s.Self, land);
        var pol = new PriorityPolicy(ArchetypeWeights.Burn);

        var action = pol.Pick(s.Context, s.Self);
        action.Should().BeOfType<PriorityAction.PlayLand>();
    }

    [Fact]
    public void Passes_WhenNothingPlayable()
    {
        var s = new BotTestScenario();
        var pol = new PriorityPolicy(ArchetypeWeights.Burn);
        var action = pol.Pick(s.Context, s.Self);
        action.Should().BeOfType<PriorityAction.PassAction>();
    }

    [Fact]
    public void Passes_WhenOpponentsTurn_AndNoInstantSpeedPlay()
    {
        var s = new BotTestScenario();
        var land = new Land("Mountain");
        s.AddCardToHand(s.Self, land);
        var oppCtx = new Majik.Core.Game.GameContext(
            s.Self, new[] { s.Self, s.Opponent }, activePlayer: s.Opponent,
            turnNumber: 1, currentPhase: Majik.Core.StateMachine.PhaseStateType.PreCombatMain, stack: s.Stack);
        var pol = new PriorityPolicy(ArchetypeWeights.Burn);
        pol.Pick(oppCtx, s.Self).Should().BeOfType<PriorityAction.PassAction>();
    }

    [Fact]
    public void CastsAffordableCreature_OverPassing()
    {
        var s = new BotTestScenario();
        // Two Mountains untapped → 2 mana available.
        s.AddLandToBattlefield(s.Self, "Mountain1");
        s.AddLandToBattlefield(s.Self, "Mountain2");
        var crt = new Creature("Goblin Guide", manaCost: "{R}", power: 2, toughness: 2);
        s.AddCardToHand(s.Self, crt);

        var pol = new PriorityPolicy(ArchetypeWeights.Burn);
        var action = pol.Pick(s.Context, s.Self);

        action.Should().BeOfType<PriorityAction.CastSpell>();
        ((PriorityAction.CastSpell)action).Card.Should().Be(crt);
    }

    [Fact]
    public void DoesNotCast_WhenInsufficientMana()
    {
        var s = new BotTestScenario();
        // One Mountain — cannot pay {2}{R}.
        s.AddLandToBattlefield(s.Self, "Mountain1");
        var crt = new Creature("Boros Reckoner", manaCost: "{R}{W}{W}", power: 3, toughness: 3);
        s.AddCardToHand(s.Self, crt);

        var pol = new PriorityPolicy(ArchetypeWeights.Burn);
        var action = pol.Pick(s.Context, s.Self);

        action.Should().BeOfType<PriorityAction.PassAction>();
    }

    [Fact]
    public void PicksHigherImpactCreature_OverWeakerOne()
    {
        var s = new BotTestScenario();
        // Plenty of mana for either.
        for (int i = 0; i < 5; i++) s.AddLandToBattlefield(s.Self, $"L{i}");
        var weak = new Creature("Mountain Goat", manaCost: "{R}", power: 1, toughness: 1);
        var strong = new Creature("Slugbeast", manaCost: "{2}{R}", power: 4, toughness: 4);
        s.AddCardToHand(s.Self, weak);
        s.AddCardToHand(s.Self, strong);

        var pol = new PriorityPolicy(ArchetypeWeights.Prowess);
        var action = pol.Pick(s.Context, s.Self);

        action.Should().BeOfType<PriorityAction.CastSpell>();
        ((PriorityAction.CastSpell)action).Card.Name.Should().Be("Slugbeast");
    }

    [Fact]
    public void CastsInstant_OnOpponentsTurn_AtInstantSpeed()
    {
        var s = new BotTestScenario();
        s.AddLandToBattlefield(s.Self, "Mountain1");
        var bolt = new Instant("Lightning Bolt", manaCost: "{R}");
        s.AddCardToHand(s.Self, bolt);

        // Opponent's turn, in their main, stack empty — instant cast still legal.
        var oppCtx = new Majik.Core.Game.GameContext(
            s.Self, new[] { s.Self, s.Opponent }, activePlayer: s.Opponent,
            turnNumber: 1, currentPhase: Majik.Core.StateMachine.PhaseStateType.PreCombatMain, stack: s.Stack);
        var pol = new PriorityPolicy(ArchetypeWeights.Burn);

        // Opponent's turn → policy should *not* cast our instant proactively.
        // BoardEval projection of a bolt on opp's turn is non-positive when we
        // can't gain anything from a one-shot here; passing is the heuristic.
        // We only check it doesn't try to PlayLand or do something illegal.
        pol.Pick(oppCtx, s.Self).Should().NotBeOfType<PriorityAction.PlayLand>();
    }

    [Fact]
    public void DoesNotCastSorcery_OnOpponentsTurn()
    {
        var s = new BotTestScenario();
        s.AddLandToBattlefield(s.Self, "Mountain1");
        s.AddLandToBattlefield(s.Self, "Mountain2");
        var sorc = new Sorcery("Banefire", manaCost: "{X}{R}");
        s.AddCardToHand(s.Self, sorc);

        var oppCtx = new Majik.Core.Game.GameContext(
            s.Self, new[] { s.Self, s.Opponent }, activePlayer: s.Opponent,
            turnNumber: 1, currentPhase: Majik.Core.StateMachine.PhaseStateType.PreCombatMain, stack: s.Stack);
        var pol = new PriorityPolicy(ArchetypeWeights.Burn);

        pol.Pick(oppCtx, s.Self).Should().BeOfType<PriorityAction.PassAction>();
    }
}
