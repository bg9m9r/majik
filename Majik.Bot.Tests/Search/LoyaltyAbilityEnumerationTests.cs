using FluentAssertions;
using Majik.Bot.Search;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// CR 606.3 — planeswalker loyalty abilities are enumerated as legal priority
/// actions ONLY in the sorcery window (active player, main phase, empty stack)
/// while their controller holds priority, subject to the once-per-turn +
/// sufficient-loyalty gate. These tests exercise the enumeration gate that
/// makes loyalty abilities playable through the priority loop.
/// </summary>
public class LoyaltyAbilityEnumerationTests
{
    private static bool HasLoyaltyAction(IReadOnlyList<PriorityAction> actions)
        => actions.OfType<PriorityAction.ActivateLoyaltyAbility>().Any();

    private static Planeswalker MakeWalkerOnBattlefield(Player owner, int loyalty)
    {
        var pw = new Planeswalker("Test Walker", "{2}{U}", loyalty);
        pw.ChangeOwner(owner);
        pw.ChangeController(owner);
        owner.Zones.Battlefield.AddCard(pw);
        return pw;
    }

    [Fact]
    public void LoyaltyAbility_InSorceryWindow_IsEnumerated()
    {
        var s = new BotTestScenario();
        var pw = MakeWalkerOnBattlefield(s.Self, loyalty: 3);
        pw.AddAbility(new LoyaltyAbility(pw, +1, () => { }));

        var actions = LegalActionEnumerator.ForPriority(s.Context, s.Self);

        actions.OfType<PriorityAction.ActivateLoyaltyAbility>()
            .Should().Contain(la => la.Ability.LoyaltyChange == +1,
                because: "a +1 loyalty ability is legal in the sorcery window");
    }

    [Fact]
    public void LoyaltyAbility_MinusAbility_IllegalWhenLoyaltyTooLow()
    {
        var s = new BotTestScenario();
        var pw = MakeWalkerOnBattlefield(s.Self, loyalty: 1);
        pw.AddAbility(new LoyaltyAbility(pw, -2, () => { }));

        var actions = LegalActionEnumerator.ForPriority(s.Context, s.Self);

        HasLoyaltyAction(actions).Should().BeFalse(
            "a −2 ability needs at least 2 loyalty (CR 606.5)");
    }

    [Fact]
    public void LoyaltyAbility_NotEnumerated_AfterActivatedThisTurn()
    {
        var s = new BotTestScenario();
        var pw = MakeWalkerOnBattlefield(s.Self, loyalty: 3);
        var plus1 = new LoyaltyAbility(pw, +1, () => { });
        pw.AddAbility(plus1);

        // CR 606.3 — activating sets the once-per-turn flag on the walker.
        plus1.Activate();

        var actions = LegalActionEnumerator.ForPriority(s.Context, s.Self);

        HasLoyaltyAction(actions).Should().BeFalse(
            "only one loyalty ability per walker per turn (CR 606.3)");
    }

    [Fact]
    public void LoyaltyAbility_NotEnumerated_OnOpponentsTurn()
    {
        var s = new BotTestScenario();
        var pw = MakeWalkerOnBattlefield(s.Self, loyalty: 3);
        pw.AddAbility(new LoyaltyAbility(pw, +1, () => { }));

        var oppCtx = new Majik.Core.Game.GameContext(
            s.Self, new[] { s.Self, s.Opponent }, activePlayer: s.Opponent,
            turnNumber: 1, currentPhase: Majik.Core.StateMachine.StepStateType.PreCombatMain,
            stack: s.Stack);

        var actions = LegalActionEnumerator.ForPriority(oppCtx, s.Self);

        HasLoyaltyAction(actions).Should().BeFalse(
            "loyalty abilities are sorcery-speed — not legal on the opponent's turn");
    }

    [Fact]
    public void LoyaltyAbility_NotEnumerated_WhenStackNonEmpty()
    {
        var s = new BotTestScenario();
        var pw = MakeWalkerOnBattlefield(s.Self, loyalty: 3);
        pw.AddAbility(new LoyaltyAbility(pw, +1, () => { }));

        // Put a dummy object on the stack so the sorcery window closes.
        var dummy = new ActivatedAbility(source: pw, controller: s.Self);
        s.Stack.Push(dummy);

        var actions = LegalActionEnumerator.ForPriority(s.Context, s.Self);

        HasLoyaltyAction(actions).Should().BeFalse(
            "loyalty abilities require an empty stack (CR 116.2a / 606.3)");
    }

    [Fact]
    public void LoyaltyAbility_NotEnumerated_OutsideMainPhase()
    {
        var s = new BotTestScenario();
        var pw = MakeWalkerOnBattlefield(s.Self, loyalty: 3);
        pw.AddAbility(new LoyaltyAbility(pw, +1, () => { }));

        var combatCtx = new Majik.Core.Game.GameContext(
            s.Self, new[] { s.Self, s.Opponent }, activePlayer: s.Self,
            turnNumber: 1, currentPhase: Majik.Core.StateMachine.StepStateType.DeclareAttackers,
            stack: s.Stack);

        var actions = LegalActionEnumerator.ForPriority(combatCtx, s.Self);

        HasLoyaltyAction(actions).Should().BeFalse(
            "loyalty abilities are sorcery-speed — main phase only (CR 606.3)");
    }
}
