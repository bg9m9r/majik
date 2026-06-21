using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// LIVE-SEAT integration coverage for the divide-damage trigger prompt
/// (CR 601.2d / CR 119.4) — the residual the deferral
/// <c>divide-damage-trigger-portal-numeric-allocation-server-fallback-audit</c>
/// called out. The engine/contract layer (DamageDivisionViewDto /
/// DamageDivisionTargetDto / ChooseDamageDivisionCommand) and the
/// <see cref="RemoteAgent.ChooseDamageDivisionAsync"/> override shipped earlier,
/// but there was NO end-to-end assertion that — for a HUMAN seat — a real
/// divide-damage <b>trigger/spell</b> ANNOUNCEMENT actually RAISES the numeric
/// per-target allocation prompt (with the <see cref="DamageDivisionViewDto"/>
/// reaching the facade's prompt buffer) instead of silently falling back to the
/// bot / disconnected even-split default.
///
/// <para>These tests drive the SAME announcement seam the live engine runs —
/// <see cref="DamageDivisionDefaults.PromptAsync"/>, the helper both
/// <see cref="TriggerManager.PutPendingTriggersOnStackAsync"/> (the triggered-
/// ability divide-damage path used by Inferno Titan / Fury) and
/// <c>SpellCastFlow</c> (the cast path used by Avacyn's Judgment) invoke right
/// after targets are chosen — against the facade's human-seat
/// <see cref="RemoteAgent"/>, fanned out via
/// <see cref="GameFacade.SubscribePrompts"/> (the buffer the
/// <c>MatchFacadeBridge</c> persists + replays to a reconnecting client). The
/// previous coverage stopped at the RemoteAgent unit level; this proves the
/// view crosses the facade publish boundary to the wire <see cref="PromptDto"/>
/// the server ships, and that the human's numeric answer (not the even default)
/// is what the engine records.</para>
/// </summary>
public sealed class DivideDamageLivePromptTests
{
    private static (RemoteAgent alice, RemoteAgent bob) Agents(GameFacade facade)
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var alice = (RemoteAgent)typeof(GameFacade).GetField("_aliceAgent", Flags)!.GetValue(facade)!;
        var bob = (RemoteAgent)typeof(GameFacade).GetField("_bobAgent", Flags)!.GetValue(facade)!;
        return (alice, bob);
    }

    private static GameContext Context(GameFacade facade) => new(
        facade.Alice,
        new[] { facade.Alice, facade.Bob },
        facade.Alice,
        turnNumber: 1,
        currentPhase: StepStateType.PreCombatMain,
        stack: facade.LiveStack);

    // ── 1. The divide-damage announcement RAISES a view to the prompt buffer ──

    [Fact]
    public async Task HumanSeatDivideDamageTrigger_RaisesDamageDivisionViewToPromptBuffer()
    {
        var facade = GameFacade.Create("Alice", "Bob",
            aliceDeck: Array.Empty<ICard>(), bobDeck: Array.Empty<ICard>());
        var (aliceAgent, _) = Agents(facade);

        var prompts = new List<PromptDto>();
        using var sub = facade.SubscribePrompts(prompts.Add);

        // Inferno Titan: "deals 3 damage divided as you choose among one, two,
        // or three targets" (CR 601.2d / CR 119.4). Two chosen targets — Bob
        // (player) + a creature — so the human must pick the split.
        var titan = InfernoTitanFactory.Create(facade.Alice, facade.Triggers);
        var bob = facade.Bob;
        var bear = BattlefieldBear(bob);

        // The exact production call TriggerManager.PutPendingTriggersOnStackAsync
        // makes after target collection (CR 603.3): prompt the controller's agent
        // for the per-target split. For the human RemoteAgent this RAISES the
        // ChooseDamageDivisionCommand prompt rather than even-splitting.
        var promptTask = DamageDivisionDefaults.PromptAsync(
            aliceAgent, Context(facade), titan,
            InfernoTitanFactory.DamageTotal, new object[] { bob, bear });

        // The view must have crossed the facade publish boundary into the buffer.
        prompts.Should().NotBeEmpty(
            "a human-seat divide-damage announcement must RAISE a prompt, not silently even-split");
        var dividePrompt = prompts.Last();
        dividePrompt.PlayerId.Should().Be(facade.Alice.Id, "Alice controls Inferno Titan");
        dividePrompt.ExpectedKinds.Should().Contain(nameof(ChooseDamageDivisionCommand));

        var view = dividePrompt.DamageDivisionView;
        view.Should().NotBeNull("the prompt must ship the numeric per-target allocation view");
        view!.SourceCardName.Should().Be("Inferno Titan");
        view.TotalDamage.Should().Be(InfernoTitanFactory.DamageTotal);
        view.Targets.Should().HaveCount(2, "Bob + the bear were the chosen targets");
        view.Targets.Select(t => t.TargetId).Should().Contain(new[] { bob.Id, bear.InstanceId });
        view.Targets.Single(t => t.TargetId == bob.Id).Name.Should().Be(bob.Name);
        view.Targets.Single(t => t.TargetId == bear.InstanceId).Name.Should().Be("Grizzly Bears");

        // Answer so the awaiting announcement completes (the engine then records
        // the human's split, NOT the even default).
        aliceAgent.Submit(new ChooseDamageDivisionCommand(new[]
        {
            new DamageDivisionAllocationDto(bob.Id, 2),
            new DamageDivisionAllocationDto(bear.InstanceId, 1),
        }) { PlayerId = facade.Alice.Id });

        var division = await promptTask;
        division.Should().NotBeNull();
    }

    // ── 2. The human's announced split is honoured (NOT the even default) ──────

    [Fact]
    public async Task HumanSeatDivideDamageTrigger_HonoursAnnouncedSplit_OverEvenDefault()
    {
        var facade = GameFacade.Create("Alice", "Bob",
            aliceDeck: Array.Empty<ICard>(), bobDeck: Array.Empty<ICard>());
        var (aliceAgent, _) = Agents(facade);
        using var sub = facade.SubscribePrompts(_ => { });

        var titan = InfernoTitanFactory.Create(facade.Alice, facade.Triggers);
        var bob = facade.Bob;
        var bear = BattlefieldBear(bob);

        var promptTask = DamageDivisionDefaults.PromptAsync(
            aliceAgent, Context(facade), titan,
            InfernoTitanFactory.DamageTotal, new object[] { bob, bear });

        aliceAgent.HasPending.Should().BeTrue(
            "the human must be ASKED — no auto even-split for a connected seat");

        // The front-loaded even split for 3-among-2 is [2,1]. The human instead
        // dumps 2 onto the bear and 1 on Bob — the OPPOSITE weighting — proving
        // the numeric allocation is honoured, not the disconnected/bot default.
        aliceAgent.Submit(new ChooseDamageDivisionCommand(new[]
        {
            new DamageDivisionAllocationDto(bob.Id, 1),
            new DamageDivisionAllocationDto(bear.InstanceId, 2),
        }) { PlayerId = facade.Alice.Id });

        var division = await promptTask;
        division.Should().NotBeNull();
        var byTarget = division!.ToDictionary(a => a.Target, a => a.Amount);
        byTarget[bob].Should().Be(1, "the human allocated 1 to Bob");
        byTarget[bear].Should().Be(2, "the human allocated 2 to the bear");
        division.Sum(a => a.Amount).Should().Be(
            InfernoTitanFactory.DamageTotal, "the full printed damage must be assigned (CR 119.4)");
    }

    // ── 3. A bot/disconnected seat still even-splits (no prompt raised) ────────

    [Fact]
    public async Task BotSeatDivideDamage_EvenSplits_WithoutRaisingPrompt()
    {
        // The even-split remains the bot/disconnected default (the deferral's
        // explicit invariant): the base IPlayerAgent default does NOT raise a
        // numeric prompt. ScriptedAgent (bot) takes the interface default.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bear = BattlefieldBear(bob);
        var titan = InfernoTitanFactory.Create(alice);

        IPlayerAgent botAgent = new ScriptedAgent();
        var ctx = new GameContext(alice, new[] { alice, bob }, alice, 1,
            StepStateType.PreCombatMain, new Majik.Core.Stack.Stack());

        var division = await DamageDivisionDefaults.PromptAsync(
            botAgent, ctx, titan, InfernoTitanFactory.DamageTotal, new object[] { bob, bear });

        division.Should().NotBeNull();
        // 3 among two targets, front-loaded → [2, 1] (the bot/disconnected seat
        // takes the even-split default, CR 119.4).
        division!.Select(a => a.Amount).Should().Equal(new[] { 2, 1 });
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Creature BattlefieldBear(Player controller)
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(controller);
        bear.SetController(controller);
        ((ICard)bear).SetZone(ZoneType.Battlefield);
        return bear;
    }
}
