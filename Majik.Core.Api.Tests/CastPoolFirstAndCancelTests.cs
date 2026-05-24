using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// PR-A coverage for the drag-to-cast UX server side:
///
/// 1. <b>Pay from floating pool first</b> (CR 601.2g + CR 106.4) —
///    <c>GameFacade.DispatchCast</c> / <c>TurnDriver.DispatchCast</c> skip
///    the <c>ChooseManaCommand</c> prompt entirely when the player's
///    already-floating mana pool covers the printed cost. This is what
///    lets the portal's "tap lands → drag spell" flow work without
///    surfacing a second confirmation dialog.
///
/// 2. <b><see cref="CancelCastCommand"/></b> (CR 601.2 / CR 727) — a
///    response to the cost-payment prompt that aborts the cast: spell
///    stays in hand, no <c>SpellCastEvent</c>, no priority change.
/// </summary>
public class CastPoolFirstAndCancelTests
{
    // ── 1. Pool covers entire cost → no prompt ──────────────────────────

    [Fact]
    public async Task Cast_PoolCoversCost_SkipsChooseManaPrompt_AndEmptiesPoolAfter()
    {
        // {W}{W} creature with two White floating in the pool. The cast
        // dispatcher must NOT prompt for sources — the pool pays directly.
        var token = new Creature("Soldier of Fortune", "WW", 2, 2);
        var facade = GameFacade.Create("Alice", "Bob",
            new ICard[] { token }, Array.Empty<ICard>());

        // Seed the hand + pool BEFORE StartAsync so the priority prompt's
        // legality-narrowed ExpectedKinds includes CastSpellCommand and
        // the pool is already full when the dispatcher runs.
        facade.Alice.Zones.Library.RemoveCard(token);
        facade.Alice.Zones.Hand.AddCard(token);
        token.SetZone(ZoneType.Hand);
        facade.Alice.AddManaToPool(ManaCost.Parse("WW"));
        facade.Alice.ManaPool.White.Should().Be(2, "fixture sanity: pool seeded");

        await facade.StartAsync();
        var aliceId = facade.Alice.Id;

        // Cast the creature. If pool-pay-first wasn't honoured, the engine
        // would now be awaiting ChooseManaCommand and the next submit
        // would either hang or be rejected. The new path skips the prompt,
        // so the dispatcher returns and the facade settles at the next
        // priority window (or end of round).
        await facade.SubmitAsync(new CastSpellCommand(
            CardInstanceId: token.InstanceId,
            TargetInstanceIds: Array.Empty<Guid>(),
            XValue: null,
            ModeIndex: null)
        { PlayerId = aliceId });

        // Spell consumed the pool's {W}{W} and left the hand.
        facade.Alice.ManaPool.White.Should().Be(0,
            "CR 601.2g — the pool's WW paid the cost directly.");
        facade.Alice.Zones.Hand.GetCards().Should().NotContain(token,
            "the creature left the hand on its way to the stack/battlefield.");
    }

    // ── 2. Pool empty → prompt fires (regression guard) ─────────────────

    [Fact]
    public async Task Cast_PoolEmpty_PromptsForSources_ProvidingMountainStillWorks()
    {
        // Existing path: pool can't cover, agent picks a Mountain via
        // ChooseManaCommand, ManaPaymentResolver taps it and pays.
        var goblin = new Creature("Mons's Goblin Raiders", "R", 1, 1);
        var facade = GameFacade.Create("Alice", "Bob",
            new ICard[] { goblin }, Array.Empty<ICard>());

        var mountain = BuildBasicLand("Mountain", CardSubtype.Mountain, facade.Alice);
        facade.Alice.Zones.Battlefield.AddCard(mountain);

        facade.Alice.Zones.Library.RemoveCard(goblin);
        facade.Alice.Zones.Hand.AddCard(goblin);
        goblin.SetZone(ZoneType.Hand);
        // Pool is empty — auto-pay-from-pool short-circuit must not fire.

        await facade.StartAsync();
        var aliceId = facade.Alice.Id;

        await facade.SubmitAsync(new CastSpellCommand(
            CardInstanceId: goblin.InstanceId,
            TargetInstanceIds: Array.Empty<Guid>(),
            XValue: null,
            ModeIndex: null)
        { PlayerId = aliceId });

        // Engine should now be at the ChooseManaCommand prompt. Provide
        // the Mountain as the source. ManaPaymentResolver taps it and
        // pays the {R} cost.
        await facade.SubmitAsync(new ChooseManaCommand(new[] { mountain.InstanceId })
        { PlayerId = aliceId });

        mountain.IsTapped.Should().BeTrue("Mountain tapped to pay {R}.");
        facade.Alice.ManaPool.IsEmpty.Should().BeTrue("cost consumed all generated mana.");
        facade.Alice.Zones.Hand.GetCards().Should().NotContain(goblin);
    }

    // ── 3. CancelCast — spell stays in hand, no mana deducted ───────────

    [Fact]
    public async Task Cast_CancelDuringChooseMana_LeavesSpellInHand_NoPriorityChange()
    {
        // Pool empty → prompt fires → submit CancelCastCommand. The
        // dispatcher must return without pushing the spell. The card is
        // still in Alice's hand and nothing was tapped / paid.
        var goblin = new Creature("Goblin", "R", 1, 1);
        var facade = GameFacade.Create("Alice", "Bob",
            new ICard[] { goblin }, Array.Empty<ICard>());

        var mountain = BuildBasicLand("Mountain", CardSubtype.Mountain, facade.Alice);
        facade.Alice.Zones.Battlefield.AddCard(mountain);

        facade.Alice.Zones.Library.RemoveCard(goblin);
        facade.Alice.Zones.Hand.AddCard(goblin);
        goblin.SetZone(ZoneType.Hand);

        await facade.StartAsync();
        var aliceId = facade.Alice.Id;

        await facade.SubmitAsync(new CastSpellCommand(
            CardInstanceId: goblin.InstanceId,
            TargetInstanceIds: Array.Empty<Guid>(),
            XValue: null,
            ModeIndex: null)
        { PlayerId = aliceId });

        // Bail out at the cost-payment prompt.
        await facade.SubmitAsync(new CancelCastCommand { PlayerId = aliceId });

        // Spell is back in hand (never left it — no payment was attempted).
        facade.Alice.Zones.Hand.GetCards().Should().Contain(goblin,
            "CR 601.2 / CR 727 — cancelled cast leaves the spell in hand.");
        mountain.IsTapped.Should().BeFalse("no mana ability was activated.");
        facade.Alice.ManaPool.IsEmpty.Should().BeTrue("no mana was deducted.");

        // Priority is still Alice's — she can submit another priority-
        // window command (a Pass closes the round cleanly).
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = aliceId });
        await facade.SubmitAsync(new PassPriorityCommand { PlayerId = facade.Bob.Id });
        facade.IsRoundComplete.Should().BeTrue();
    }

    // ── 4. CancelCast invalid timing → rejected at the agent boundary ───

    [Fact]
    public async Task CancelCast_OutsideChooseManaPrompt_IsRejected()
    {
        // CancelCastCommand is only legal as a response to a
        // ChooseManaCommand. At a vanilla priority window the expected
        // kinds are Pass / PlayLand / CastSpell / ActivateManaAbility —
        // CancelCast is not among them. RemoteAgent.Submit must throw.
        var facade = GameFacade.Create("Alice", "Bob",
            Array.Empty<ICard>(), Array.Empty<ICard>());

        await facade.StartAsync();
        var aliceId = facade.Alice.Id;

        var act = async () => await facade.SubmitAsync(new CancelCastCommand { PlayerId = aliceId });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*expected*CancelCastCommand*");
    }

    // ── 5. End-step empties pool (regression guard) ─────────────────────

    [Fact]
    public void Pool_EmptyManaPool_StillEmptiesViaPlayerAPI()
    {
        // CR 106.4 — floating mana empties at end of each phase/step.
        // Pool-pay-first must not have broken that invariant. We exercise
        // the Player.EmptyManaPool call directly; TurnDriver.RunTurn
        // invokes it at each step boundary (Player.cs:293-296,
        // TurnDriver.cs around the cleanup step).
        var alice = new Player("Alice", 20);
        alice.AddManaToPool(ManaCost.Parse("WUBRG"));
        alice.ManaPool.IsEmpty.Should().BeFalse();

        alice.EmptyManaPool();

        alice.ManaPool.IsEmpty.Should().BeTrue(
            "CR 106.4 — pools empty at end of each step/phase.");
    }

    // ── 6. ExpectedCommandKinds — ChooseMana prompt offers CancelCast ───

    [Fact]
    public async Task ChooseManaSources_PromptKinds_IncludeCancelCastAlongsideChooseMana()
    {
        // Direct RemoteAgent unit: when the engine asks for mana sources,
        // ExpectedCommandKinds must surface BOTH ChooseManaCommand and
        // CancelCastCommand so the portal can offer a "Cancel" button on
        // the cost-payment dialog (this is what PR-B will wire up).
        var alice = new Player("Alice", 20);
        var agent = new RemoteAgent(alice);
        var ctx = new Majik.Core.Game.GameContext(
            alice, new[] { alice }, alice, 1,
            Majik.Core.StateMachine.PhaseStateType.Main,
            new Majik.Core.Stack.Stack());

        _ = agent.ChooseManaSourcesAsync(ctx, ManaCost.Parse("R"));

        agent.ExpectedCommandKinds.Should().BeEquivalentTo(new[]
        {
            typeof(ChooseManaCommand),
            typeof(CancelCastCommand),
        });
    }

    [Fact]
    public async Task ChooseManaSources_CancelCastSubmitted_ResolvesToCancelledSentinel()
    {
        // The agent-level translation: CancelCastCommand at the cost-
        // payment prompt completes the awaiting TCS with
        // ManaPayment.Cancelled. The dispatch site reads IsCancelled and
        // aborts the cast.
        var alice = new Player("Alice", 20);
        var agent = new RemoteAgent(alice);
        var ctx = new Majik.Core.Game.GameContext(
            alice, new[] { alice }, alice, 1,
            Majik.Core.StateMachine.PhaseStateType.Main,
            new Majik.Core.Stack.Stack());

        var task = agent.ChooseManaSourcesAsync(ctx, ManaCost.Parse("R"));
        agent.Submit(new CancelCastCommand { PlayerId = alice.Id });

        var payment = await task;
        payment.IsCancelled.Should().BeTrue();
        payment.Should().BeSameAs(ManaPayment.Cancelled);
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static Land BuildBasicLand(string name, CardSubtype subtype, Player controller)
    {
        var land = new Land(
            name,
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { subtype });
        land.SetOwner(controller);
        land.ChangeController(controller);
        land.SetZone(ZoneType.Battlefield);
        OracleManaBinder.BindBasicLandMana(land, controller);
        return land;
    }
}
