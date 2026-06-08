using FluentAssertions;
using Majik.Bot.Search;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Cards;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Bot.Tests.Search;

public class LegalActionEnumeratorTests
{
    /// <summary>
    /// In a sorcery window (active player, PreCombatMain, empty stack) with a
    /// land in hand and a castable 1-mana creature, ForPriority must return at
    /// least Pass, PlayLand, and CastSpell.
    /// </summary>
    [Fact]
    public void ForPriority_InSorceryWindow_ReturnsPass_PlayLand_AndCastSpell()
    {
        var s = new BotTestScenario();
        // Self is active player in PreCombatMain with empty stack — sorcery window.

        // Land in hand (gives us a PlayLand candidate).
        s.AddCardToHand(s.Self, new Land("Forest"));

        // One Forest in play (provides {G} mana).
        s.AddLandToBattlefield(s.Self, "ForestBF");

        // Castable 1-mana creature in hand (CMC 1 ≤ 1 untapped land = affordable).
        var elf = new Creature("Llanowar Elves", manaCost: "{G}", power: 1, toughness: 1);
        s.AddCardToHand(s.Self, elf);

        var actions = LegalActionEnumerator.ForPriority(s.Context, s.Self);

        actions.Should().Contain(a => a is PriorityAction.PassAction,
            because: "Pass is always legal");
        actions.Should().Contain(a => a is PriorityAction.PlayLand,
            because: "there is a land in hand and it's a sorcery window");
        actions.Should().Contain(a => a is PriorityAction.CastSpell,
            because: "there is an affordable castable spell and it's a sorcery window");
    }

    /// <summary>
    /// Outside a sorcery window (opponent's turn), sorcery-speed actions
    /// (PlayLand) must not appear; only Pass and instant-speed casts are legal.
    /// With no instants in hand, the result is Pass-only.
    /// </summary>
    [Fact]
    public void ForPriority_NotSorceryWindow_ReturnsPassOnly_WhenNoInstants()
    {
        var s = new BotTestScenario();
        // Build an opponent's-turn context.
        var oppCtx = new Majik.Core.Game.GameContext(
            s.Self, new[] { s.Self, s.Opponent }, activePlayer: s.Opponent,
            turnNumber: 1, currentPhase: Majik.Core.StateMachine.StepStateType.PreCombatMain,
            stack: s.Stack);

        s.AddCardToHand(s.Self, new Land("Forest"));
        s.AddLandToBattlefield(s.Self, "ForestBF");
        // Sorcery in hand — not castable on opponent's turn.
        s.AddCardToHand(s.Self, new Creature("Llanowar Elves", manaCost: "{G}", power: 1, toughness: 1));

        var actions = LegalActionEnumerator.ForPriority(oppCtx, s.Self);

        actions.Should().Contain(a => a is PriorityAction.PassAction,
            because: "Pass is always legal");
        actions.Should().NotContain(a => a is PriorityAction.PlayLand,
            because: "land plays are sorcery-speed and only legal on our own turn");
        actions.Should().NotContain(a => a is PriorityAction.CastSpell,
            because: "creature is not instant-speed; not castable on opponent's turn");
    }

    /// <summary>
    /// An instant (e.g. Lightning Bolt) is legal even on the opponent's turn.
    /// </summary>
    [Fact]
    public void ForPriority_NotSorceryWindow_IncludesInstantCast_WhenAffordable()
    {
        var s = new BotTestScenario();
        var oppCtx = new Majik.Core.Game.GameContext(
            s.Self, new[] { s.Self, s.Opponent }, activePlayer: s.Opponent,
            turnNumber: 1, currentPhase: Majik.Core.StateMachine.StepStateType.PreCombatMain,
            stack: s.Stack);

        s.AddLandToBattlefield(s.Self, "Mountain1");
        var bolt = new Instant("Lightning Bolt", manaCost: "{R}");
        s.AddCardToHand(s.Self, bolt);

        var actions = LegalActionEnumerator.ForPriority(oppCtx, s.Self);

        actions.Should().Contain(a => a is PriorityAction.CastSpell,
            because: "Lightning Bolt is an Instant and therefore legal at instant speed");
    }

    /// <summary>
    /// When the player has no mana available, an unaffordable spell must not
    /// appear in the legal action set.
    /// </summary>
    [Fact]
    public void ForPriority_ExcludesUnaffordableSpells()
    {
        var s = new BotTestScenario();
        // No lands in play — zero mana available.
        var bigCreature = new Creature("Emrakul", manaCost: "{15}", power: 15, toughness: 15);
        s.AddCardToHand(s.Self, bigCreature);

        var actions = LegalActionEnumerator.ForPriority(s.Context, s.Self);

        actions.Should().Contain(a => a is PriorityAction.PassAction);
        actions.Should().NotContain(a => a is PriorityAction.CastSpell,
            because: "zero mana is insufficient to cast a {15} spell");
    }
}
