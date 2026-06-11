using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Heuristic;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Bot.Tests;

/// <summary>
/// CR 606 — the heuristic priority policy scores and activates planeswalker
/// loyalty abilities so the bot actually uses its planeswalkers, and the
/// once-per-turn memo prevents a re-proposal spin loop.
/// </summary>
public class LoyaltyAbilityPolicyTests
{
    private static Planeswalker MakeWalkerOnBattlefield(Player owner, int loyalty)
    {
        var pw = new Planeswalker("Test Walker", "{2}{U}", loyalty);
        pw.ChangeOwner(owner);
        pw.ChangeController(owner);
        owner.Zones.Battlefield.AddCard(pw);
        return pw;
    }

    [Fact]
    public void Bot_ActivatesLoyaltyAbility_WhenItIsTheBestAction()
    {
        var s = new BotTestScenario();
        var pw = MakeWalkerOnBattlefield(s.Self, loyalty: 3);
        // A +1 "draw a card" — broadly favourable, beats Pass.
        pw.AddAbility(new LoyaltyAbility(pw, +1,
            new[] { Majik.Core.Primitives.Fx.Inline("draw a card", () => { }) }));

        var pol = new PriorityPolicy(ArchetypeWeights.Default);
        var action = pol.Pick(s.Context, s.Self);

        action.Should().BeOfType<PriorityAction.ActivateLoyaltyAbility>(
            "a favourable loyalty ability should beat Pass");
    }

    [Fact]
    public void Bot_DoesNotReproposeLoyaltyAbility_AfterProposingItThisTurn()
    {
        // Anti-spin: once the bot proposes a loyalty activation, the per-turn
        // memo stops it re-offering the same walker (the walker's
        // once-per-turn flag isn't yet set in this pure-policy test because no
        // engine dispatch runs, so the memo is what prevents the spin).
        var s = new BotTestScenario();
        var pw = MakeWalkerOnBattlefield(s.Self, loyalty: 3);
        pw.AddAbility(new LoyaltyAbility(pw, +1,
            new[] { Majik.Core.Primitives.Fx.Inline("draw a card", () => { }) }));

        var pol = new PriorityPolicy(ArchetypeWeights.Default);

        pol.Pick(s.Context, s.Self).Should().BeOfType<PriorityAction.ActivateLoyaltyAbility>();
        pol.Pick(s.Context, s.Self).Should().NotBeOfType<PriorityAction.ActivateLoyaltyAbility>(
            "the loyalty ability is treated as spent once proposed this turn");
    }
}
