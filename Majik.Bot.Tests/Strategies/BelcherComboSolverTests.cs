using FluentAssertions;
using Majik.Bot.Strategies;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Strategies;

/// <summary>
/// Unit tests for <see cref="BelcherComboSolver"/> — the Goblin Charbelcher
/// combo executor (plan 2026-06-13, Phase C). The solver re-derives the next
/// action toward the kill each priority window:
///
/// <list type="bullet">
///   <item>Charbelcher on board + {3} FLOATING → fire the belch.</item>
///   <item>Charbelcher on board + {3} not floating but tappable → float it
///     (mana-ability action) — the LIVE engine pays an activated ability from
///     the FLOATING pool only, so the solver must float first.</item>
///   <item>Charbelcher in hand + ≥ {7} available → hard-cast it.</item>
///   <item>not lethal / not enough mana / no Charbelcher → null (play normally).</item>
/// </list>
///
/// Lethality gate: a reveal-until-LAND belch deals damage = nonland-cards
/// revealed, so the library must hold ≥ opp-life NONLAND cards. Tests seed the
/// library accordingly.
/// </summary>
public sealed class BelcherComboSolverTests
{
    private static BelcherComboSolver Solver() => new();

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Charbelcher with the real {3},{T} belch cost shape.</summary>
    private static Artifact BuildCharbelcher(Player owner)
    {
        var belcher = new Artifact("Goblin Charbelcher", "{4}");
        belcher.ChangeOwner(owner);
        belcher.ChangeController(owner);

        var ability = new ActivatedAbility(
            source: belcher,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{3}"), AdditionalCost.Tap(belcher) },
            effects: Array.Empty<IEffect>(),
            targetRequests: new[]
            {
                new TargetRequest("any target", MinTargets: 1, MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });
        belcher.AddAbility(ability);
        return belcher;
    }

    /// <summary>Lotus Bloom with its "{T}, Sac: add three" mana ability (R mode).</summary>
    private static Artifact BuildLotusBloom(Player owner)
    {
        var bloom = new Artifact("Lotus Bloom", "");
        bloom.ChangeOwner(owner);
        bloom.ChangeController(owner);
        bloom.AddAbility(new ManaAbility(
            source: bloom,
            controller: owner,
            manaGenerated: ManaCost.Parse("RRR"),
            canActivateCheck: () => !bloom.IsTapped));
        return bloom;
    }

    /// <summary>Seed <paramref name="count"/> nonland cards into the library so
    /// the belch is lethal at the given opponent life.</summary>
    private static void SeedNonlandLibrary(Player p, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var c = new Instant($"Nonland-{i}", "{1}");
            c.ChangeOwner(p);
            p.Zones.GetZone(ZoneType.Library).AddCard(c);
        }
    }

    // ── Arm 1: Charbelcher on board + {3} floating → belch ─────────────────────

    [Fact]
    public void Belch_Fires_WhenCharbelcherOnBoard_And3Floating_AndLethal()
    {
        var s = new BotTestScenario(oppLife: 12);
        var belcher = BuildCharbelcher(s.Self);
        s.Self.Zones.Battlefield.AddCard(belcher);
        s.Self.AddManaToPool(ManaCost.Parse("{R}{R}{R}"));
        SeedNonlandLibrary(s.Self, 20); // ≥ 12 nonland → lethal

        var action = Solver().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeOfType<PriorityAction.ActivateAbility>(
            "Charbelcher untapped, {3} floating, reveal is lethal → fire the belch");
    }

    [Fact]
    public void Belch_TargetsOpponent()
    {
        var s = new BotTestScenario(oppLife: 12);
        var belcher = BuildCharbelcher(s.Self);
        s.Self.Zones.Battlefield.AddCard(belcher);
        s.Self.AddManaToPool(ManaCost.Parse("{R}{R}{R}"));
        SeedNonlandLibrary(s.Self, 20);

        var action = Solver().TryGetNextWinningAction(s.Context, s.Self);

        var activate = action.Should().BeOfType<PriorityAction.ActivateAbility>().Subject;
        activate.Targets.Should().ContainSingle()
            .Which.Should().BeSameAs(s.Opponent, "the belch is aimed at the opponent");
    }

    // ── Arm 2: Charbelcher on board, {3} NOT floating → float first ────────────

    [Fact]
    public void Floats_LotusBloom_WhenCharbelcherOnBoard_ButNoFloatingMana()
    {
        var s = new BotTestScenario(oppLife: 12);
        var belcher = BuildCharbelcher(s.Self);
        s.Self.Zones.Battlefield.AddCard(belcher);
        // {3} lives in Lotus Bloom (untapped), NOT in the floating pool.
        var bloom = BuildLotusBloom(s.Self);
        s.Self.Zones.Battlefield.AddCard(bloom);
        SeedNonlandLibrary(s.Self, 20);

        var action = Solver().TryGetNextWinningAction(s.Context, s.Self);

        var mana = action.Should().BeOfType<PriorityAction.ActivateManaAbility>(
            "the live engine pays the belch from the FLOATING pool only — the solver " +
            "must tap Lotus Bloom to float {3} before it can fire the belch").Subject;
        mana.Source.Name.Should().Be("Lotus Bloom");
    }

    [Fact]
    public void DoesNotBelch_WhenManaIsUntappedButNotFloated()
    {
        // Regression for the WU-deck non-firing bug: total mana ≥ {3} but it is
        // all in untapped sources, none floating → must NOT return the belch
        // (the live dispatch would silently swallow it). Returns the float step.
        var s = new BotTestScenario(oppLife: 12);
        var belcher = BuildCharbelcher(s.Self);
        s.Self.Zones.Battlefield.AddCard(belcher);
        s.Self.Zones.Battlefield.AddCard(BuildLotusBloom(s.Self));
        SeedNonlandLibrary(s.Self, 20);

        var action = Solver().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().NotBeOfType<PriorityAction.ActivateAbility>(
            "with no floating mana the belch can't be paid by the live loop — float first");
    }

    // ── Arm 3: Charbelcher in hand → hard-cast ────────────────────────────────

    [Fact]
    public void HardCasts_WhenCharbelcherInHand_And7Available_AndLethal()
    {
        var s = new BotTestScenario(oppLife: 12);
        var belcher = new Artifact("Goblin Charbelcher", "{4}");
        s.AddCardToHand(s.Self, belcher);
        // {4} cast + {3} activation = {7} available (floating is fine for detection).
        s.Self.AddManaToPool(ManaCost.Parse("{R}{R}{R}{R}{R}{R}{R}"));
        SeedNonlandLibrary(s.Self, 20);

        var action = Solver().TryGetNextWinningAction(s.Context, s.Self);

        var cast = action.Should().BeOfType<PriorityAction.CastSpell>(
            "Charbelcher in hand + {7} available + lethal → hard-cast it ({4})").Subject;
        cast.Card.Name.Should().Be("Goblin Charbelcher");
    }

    // ── Detection negatives ───────────────────────────────────────────────────

    [Fact]
    public void Null_WhenNotEnoughManaForActivation()
    {
        var s = new BotTestScenario(oppLife: 12);
        var belcher = BuildCharbelcher(s.Self);
        s.Self.Zones.Battlefield.AddCard(belcher);
        // Only {2} available — belch needs {3}.
        s.Self.AddManaToPool(ManaCost.Parse("{R}{R}"));
        SeedNonlandLibrary(s.Self, 20);

        var action = Solver().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeNull("{2} < {3} activation — not assemblable this turn");
    }

    [Fact]
    public void Null_WhenInHandButOnly4Available()
    {
        var s = new BotTestScenario(oppLife: 12);
        s.AddCardToHand(s.Self, new Artifact("Goblin Charbelcher", "{4}"));
        // {4} pays the cast but leaves nothing for the {3} activation.
        s.Self.AddManaToPool(ManaCost.Parse("{R}{R}{R}{R}"));
        SeedNonlandLibrary(s.Self, 20);

        var action = Solver().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeNull("{4} only pays the cast — need {7} for cast + activation");
    }

    [Fact]
    public void Null_WhenRevealNotLethal()
    {
        // Charbelcher + {3} floating, but only 5 nonland cards in library vs an
        // opponent at 12 → the belch deals ≤ 5, not lethal → don't fire.
        var s = new BotTestScenario(oppLife: 12);
        var belcher = BuildCharbelcher(s.Self);
        s.Self.Zones.Battlefield.AddCard(belcher);
        s.Self.AddManaToPool(ManaCost.Parse("{R}{R}{R}"));
        SeedNonlandLibrary(s.Self, 5);

        var action = Solver().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeNull("5 nonland cards < 12 opp life → reveal is not lethal");
    }

    [Fact]
    public void Null_WhenNoCharbelcherAnywhere()
    {
        var s = new BotTestScenario(oppLife: 12);
        s.Self.AddManaToPool(ManaCost.Parse("{R}{R}{R}{R}{R}{R}{R}"));
        SeedNonlandLibrary(s.Self, 20);

        var action = Solver().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeNull("no Charbelcher on board or in hand → no line");
    }

    [Fact]
    public void Null_WhenCharbelcherTappedAndNoOtherLine()
    {
        var s = new BotTestScenario(oppLife: 12);
        var belcher = BuildCharbelcher(s.Self);
        s.Self.Zones.Battlefield.AddCard(belcher);
        belcher.Tap();
        s.Self.AddManaToPool(ManaCost.Parse("{R}{R}{R}"));
        SeedNonlandLibrary(s.Self, 20);

        var action = Solver().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeNull("tapped Charbelcher can't pay its {T} — no belch");
    }

    // ── StrategicScore ────────────────────────────────────────────────────────

    [Fact]
    public void StrategicScore_HighestWhenCharbelcherOnBoard()
    {
        var s = new BotTestScenario();
        var empty = Solver().StrategicScore(s.Context, s.Self);

        var belcher = new Artifact("Goblin Charbelcher", "{4}");
        belcher.ChangeOwner(s.Self);
        s.Self.Zones.Battlefield.AddCard(belcher);
        var onBoard = Solver().StrategicScore(s.Context, s.Self);

        onBoard.Should().BeGreaterThan(empty);
    }

    // ── AdviseMulligan ────────────────────────────────────────────────────────

    [Fact]
    public void Mulligan_Keeps_WhenCharbelcherInHand()
    {
        var hand = new List<ICard> { new Artifact("Goblin Charbelcher", "{4}") };
        Solver().AdviseMulligan(hand, 0).Should().Be(MulliganDecision.Keep);
    }

    [Fact]
    public void Mulligan_Keeps_WhenLotusBloomInHand()
    {
        var hand = new List<ICard> { new Artifact("Lotus Bloom", "") };
        Solver().AdviseMulligan(hand, 0).Should().Be(MulliganDecision.Keep);
    }

    [Fact]
    public void Mulligan_Ships_WhenNoComboPiece()
    {
        var hand = new List<ICard> { new Instant("Preordain", "{U}") };
        Solver().AdviseMulligan(hand, 0).Should().Be(MulliganDecision.Mulligan);
    }

    [Fact]
    public void Mulligan_DefersToGeneric_AtDepth3()
    {
        var hand = new List<ICard> { new Instant("Preordain", "{U}") };
        Solver().AdviseMulligan(hand, 3).Should().BeNull();
    }
}
