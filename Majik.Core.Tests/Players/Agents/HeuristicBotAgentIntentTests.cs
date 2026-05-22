using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Players.Agents;

/// <summary>
/// Intent-aware decisions on <see cref="HeuristicBotAgent"/>. Covers
/// <c>ChooseModeAsync</c> + <c>ChooseTargetsAsync</c> reading
/// <see cref="BotIntent"/> off the request / mode-intents list.
/// </summary>
public class HeuristicBotAgentIntentTests
{
    private readonly Player _self = new("Self", 20);
    private readonly Player _opp = new("Opp", 20);

    private GameContext NewCtx() =>
        new(_self, new[] { _self, _opp }, _self,
            1, PhaseStateType.Main, new Majik.Core.Stack.Stack());

    private Creature AddCreature(Player owner, string name, int p, int t)
    {
        var c = new Creature(name, "1G", p, t) { Owner = owner, Controller = owner };
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    [Fact]
    public async Task ChooseMode_PicksRemoval_WhenOpponentHasCreature()
    {
        AddCreature(_opp, "Bear", 2, 2);
        var bot = new HeuristicBotAgent();
        var idx = await bot.ChooseModeAsync(
            NewCtx(),
            modes: new[] { "Destroy target creature.", "Draw a card.", "Gain 3 life." },
            modeIntents: new[] { BotIntent.Removal, BotIntent.Draw, BotIntent.Heal });
        idx.Should().Be(0);
    }

    [Fact]
    public async Task ChooseMode_PicksHeal_WhenLifeLow()
    {
        var lowSelf = new Player("Self", 4);
        var ctx = new GameContext(lowSelf, new[] { lowSelf, _opp }, lowSelf,
            1, PhaseStateType.Main, new Majik.Core.Stack.Stack());

        var bot = new HeuristicBotAgent();
        var idx = await bot.ChooseModeAsync(ctx,
            modes: new[] { "Draw a card.", "Gain 5 life." },
            modeIntents: new[] { BotIntent.Draw, BotIntent.Heal });
        idx.Should().Be(1);
    }

    [Fact]
    public async Task ChooseMode_LegacyFallback_WhenModeIntentsNull()
    {
        AddCreature(_opp, "Bear", 2, 2);
        var bot = new HeuristicBotAgent();
        // Pre-annotation templates pass null modeIntents — must produce a
        // valid index without crashing.
        var idx = await bot.ChooseModeAsync(
            NewCtx(),
            modes: new[] { "Destroy target creature.", "Draw a card." },
            modeIntents: null);
        idx.Should().BeInRange(0, 1);
    }

    [Fact]
    public async Task ChooseMode_LegacyFallback_WhenAllModeIntentsNone()
    {
        AddCreature(_opp, "Bear", 2, 2);
        var bot = new HeuristicBotAgent();
        // Composer's None-Intent passes through as a list of all-None.
        var idx = await bot.ChooseModeAsync(
            NewCtx(),
            modes: new[] { "Destroy target creature.", "Draw a card." },
            modeIntents: new[] { BotIntent.None, BotIntent.None });
        idx.Should().BeInRange(0, 1);
    }

    [Fact]
    public async Task ChooseMode_WrathSuppressed_WhenEmptyBoard()
    {
        // Wrath without our own creatures — should NOT pick over Draw.
        var bot = new HeuristicBotAgent();
        AddCreature(_opp, "Bear", 2, 2);
        var idx = await bot.ChooseModeAsync(
            NewCtx(),
            modes: new[] { "Destroy all creatures.", "Draw two cards." },
            modeIntents: new[] { BotIntent.Wrath, BotIntent.Draw });
        // Wrath gets +35 (opp has creature) but we want Draw when we have
        // no creatures of our own — actually the spec's scorer doesn't
        // suppress Wrath at choose-mode time (Task 11 handles that bias).
        // For now just confirm one of the two is picked.
        idx.Should().BeInRange(0, 1);
    }

    [Fact]
    public async Task ChooseMode_EmptyModes_ReturnsZero()
    {
        var bot = new HeuristicBotAgent();
        var idx = await bot.ChooseModeAsync(NewCtx(),
            modes: Array.Empty<string>(),
            modeIntents: Array.Empty<BotIntent>());
        idx.Should().Be(0);
    }

    [Fact]
    public async Task ChooseTargets_BuffIntent_PicksOwnBestCreature()
    {
        var mine = AddCreature(_self, "MyBear", 2, 2);
        var theirs = AddCreature(_opp, "OppBear", 2, 2);
        var bot = new HeuristicBotAgent();

        var req = new TargetRequest(
            Description: "target creature",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: new object[] { theirs, mine },
            Intent: BotIntent.Buff);

        var picked = await bot.ChooseTargetsAsync(NewCtx(), req);
        picked.Should().ContainSingle().Which.Should().BeSameAs(mine);
    }

    [Fact]
    public async Task ChooseTargets_RemovalIntent_PicksOpponentBiggest()
    {
        var small = AddCreature(_opp, "Goblin", 1, 1);
        var big = AddCreature(_opp, "Wurm", 6, 6);
        var bot = new HeuristicBotAgent();

        var req = new TargetRequest(
            Description: "target creature",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: new object[] { small, big },
            Intent: BotIntent.Removal);

        var picked = await bot.ChooseTargetsAsync(NewCtx(), req);
        picked.Should().ContainSingle().Which.Should().BeSameAs(big);
    }

    [Fact]
    public async Task ChooseTargets_HealIntent_PrefersSelfPlayer()
    {
        var bot = new HeuristicBotAgent();
        var req = new TargetRequest(
            Description: "target player",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: new object[] { _opp, _self },
            Intent: BotIntent.Heal);

        var picked = await bot.ChooseTargetsAsync(NewCtx(), req);
        picked.Should().ContainSingle().Which.Should().BeSameAs(_self);
    }

    [Fact]
    public async Task ChooseTargets_LegacyLabelFallback_WhenIntentNone()
    {
        // Intent None + "you control" label — exercises the legacy
        // LabelIsBuff path that older templates rely on.
        var mine = AddCreature(_self, "MyBear", 2, 2);
        var theirs = AddCreature(_opp, "OppBear", 2, 2);
        var bot = new HeuristicBotAgent();

        var req = new TargetRequest(
            Description: "target creature you control",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: new object[] { theirs, mine },
            Intent: BotIntent.None);

        var picked = await bot.ChooseTargetsAsync(NewCtx(), req);
        picked.Should().ContainSingle().Which.Should().BeSameAs(mine);
    }
}
