using FluentAssertions;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Cards;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Bot.Tests.Frozen;

/// <summary>
/// FB1 CHARACTERIZATION PINS — the drift alarm for the frozen baseline.
///
/// <para>These pins define FB1. They must NEVER change. Each test pins the
/// EXACT decision <c>Majik.Bot.Frozen.FB1.HeuristicStrategy</c> returned on a
/// fixed board at vendor time (cut 2026-06-12, commit 38547ffb3 — values
/// MEASURED, not derived). A failing pin means an edit changed frozen
/// behavior — revert the edit; only mechanical API-rename patches
/// (behavior-preserving) are allowed in <c>Majik.Bot/Frozen/FB1/</c>.</para>
///
/// <para>If the live heuristic evolves these pins stay green — FB1 is a
/// snapshot, not a mirror. When FB1 is consistently stomped, cut FB2 as a new
/// rung beside it; do not edit FB1.</para>
/// </summary>
public class FB1CharacterizationTests
{
    private static Majik.Bot.IBotStrategy Fb1(string archetype = "Burn")
        => new Majik.Bot.Frozen.FB1.HeuristicStrategy(new Majik.Bot.BotConfig(archetype));

    private static IReadOnlyList<ICard> Hand(int lands, int spells)
    {
        var h = new List<ICard>();
        for (int i = 0; i < lands; i++) h.Add(new Land($"Land{i}"));
        for (int i = 0; i < spells; i++) h.Add(new Creature($"Creature{i}", "", 2, 2));
        return h;
    }

    [Fact]
    public void Pin1_Attack_DeclinesUnprofitableSwing_IntoBiggerBlockers()
    {
        // Prowess seat: 4/4 + 2/2 vs opp 2/2 + 3/3. Measured: FB1 declines
        // the attack entirely (opp's optimal block trades up).
        var s = new BotTestScenario();
        var a1 = s.AddCreatureToBattlefield(s.Self, "BigGuy", 4, 4);
        var a2 = s.AddCreatureToBattlefield(s.Self, "SmallGuy", 2, 2);
        s.AddCreatureToBattlefield(s.Opponent, "Bear", 2, 2);
        s.AddCreatureToBattlefield(s.Opponent, "Hill Giant", 3, 3);

        var plan = Fb1("Prowess").PickAttackers(s.Context, s.Self, new Creature[] { a1, a2 });

        plan.Attackers.Should().BeEmpty("pinned FB1 decision (measured 2026-06-12)");
    }

    [Fact]
    public void Pin2_Attack_FullSwing_IntoEmptyBoard()
    {
        // Burn seat, no opposing blockers. Measured: FB1 attacks with both.
        var s = new BotTestScenario();
        var goblin = s.AddCreatureToBattlefield(s.Self, "Goblin", 2, 1);
        var bear   = s.AddCreatureToBattlefield(s.Self, "Bear", 2, 2);

        var plan = Fb1().PickAttackers(s.Context, s.Self, new Creature[] { goblin, bear });

        plan.Attackers.Select(a => a.Attacker.Name)
            .Should().BeEquivalentTo(new[] { "Goblin", "Bear" },
                "pinned FB1 decision (measured 2026-06-12)");
    }

    [Fact]
    public void Pin3_Block_WallEatsRunner_BruteUnblocked()
    {
        // Burn seat defends: opp attacks 4/4 Brute + 2/1 Runner; we hold
        // 0/4 Wall + 2/2 Bear. Measured: Wall blocks Runner; Brute is let
        // through unblocked; Bear stays back.
        var s = new BotTestScenario();
        var brute  = s.AddCreatureToBattlefield(s.Opponent, "Brute", 4, 4);
        var runner = s.AddCreatureToBattlefield(s.Opponent, "Runner", 2, 1);
        var wall = s.AddCreatureToBattlefield(s.Self, "Wall", 0, 4);
        var bear = s.AddCreatureToBattlefield(s.Self, "Bear", 2, 2);

        var plan = Fb1().PickBlockers(s.Context, s.Self,
            new Creature[] { brute, runner }, new Creature[] { wall, bear });

        plan.Blockers.Should().ContainSingle("pinned FB1 decision (measured 2026-06-12)");
        plan.Blockers[0].Blocker.Should().BeSameAs(wall);
        plan.Blockers[0].Attacker.Should().BeSameAs(runner);
    }

    [Fact]
    public void Pin4_Priority_CastsBiggerCreature_WithAmpleMana()
    {
        // Prowess seat: 5 untapped lands; hand = {R} 1/1 + {2}{R} 4/4.
        // Measured: FB1 casts Slugbeast (the 4/4).
        var s = new BotTestScenario();
        for (int i = 0; i < 5; i++) s.AddLandToBattlefield(s.Self, $"L{i}");
        var weak = new Creature("Mountain Goat", manaCost: "{R}", power: 1, toughness: 1);
        var strong = new Creature("Slugbeast", manaCost: "{2}{R}", power: 4, toughness: 4);
        s.AddCardToHand(s.Self, weak);
        s.AddCardToHand(s.Self, strong);

        var action = Fb1("Prowess").PickPriorityAction(s.Context, s.Self);

        var cast = action.Should().BeOfType<PriorityAction.CastSpell>(
            "pinned FB1 decision (measured 2026-06-12)").Subject;
        cast.Card.Should().BeSameAs(strong);
    }

    [Fact]
    public void Pin5_Mulligan_OneLanderShips_ThreeLanderKeeps()
    {
        // Measured: 1 land / 6 spells → Mulligan; 3 lands / 4 spells → Keep.
        Fb1().PickMulligan(Hand(1, 6), mulligansTaken: 0)
            .Should().Be(MulliganDecision.Mulligan, "pinned FB1 decision (measured 2026-06-12)");
        Fb1().PickMulligan(Hand(3, 4), mulligansTaken: 0)
            .Should().Be(MulliganDecision.Keep, "pinned FB1 decision (measured 2026-06-12)");
    }

    [Fact]
    public void Pin6_Target_PicksBiggestCreature()
    {
        // "destroy target creature" with a 1/1 and a 6/6 legal. Measured:
        // FB1 picks the Wurm (6/6).
        var s = new BotTestScenario();
        var goblin = s.AddCreatureToBattlefield(s.Opponent, "Goblin", 1, 1);
        var wurm   = s.AddCreatureToBattlefield(s.Opponent, "Wurm", 6, 6);
        var req = new TargetRequest("destroy target creature", 1, 1, new object[] { goblin, wurm });

        var picked = Fb1().PickTargets(s.Context, s.Self, req);

        picked.Should().ContainSingle("pinned FB1 decision (measured 2026-06-12)")
            .Which.Should().BeSameAs(wurm);
    }

    [Fact]
    public void Pin7_Mana_EmptyPayment_EngineAutoTaps()
    {
        // {1}{R} with two untapped lands available. Measured: FB1 returns an
        // EMPTY payment (no explicit sources) — the engine's
        // ManaPaymentResolver auto-taps on the bot's behalf.
        var s = new BotTestScenario();
        s.AddLandToBattlefield(s.Self, "Mountain1");
        s.AddLandToBattlefield(s.Self, "Mountain2");

        var payment = Fb1().PickMana(s.Context, s.Self, ManaCost.Parse("{1}{R}"));

        payment.Sources.Should().BeEmpty("pinned FB1 decision (measured 2026-06-12)");
    }

    [Fact]
    public void Pin8_BottomsNonLands_OnMulliganBottom()
    {
        // 2 lands + 2 creatures, bottom 2. Measured: FB1 bottoms the two
        // CREATURES (keeps lands) — order CreatureA then CreatureB.
        var hand = new List<ICard>
        {
            new Land("LandA"), new Creature("CreatureA", "", 2, 2),
            new Land("LandB"), new Creature("CreatureB", "", 3, 3),
        };

        var bottomed = Fb1().PickCardsToBottom(hand, 2);

        bottomed.Select(c => c.Name).Should().Equal(new[] { "CreatureA", "CreatureB" },
            "pinned FB1 decision (measured 2026-06-12)");
    }
}
