using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

public class PriorityLoopLandDropTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly PriorityManager _priority;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public PriorityLoopLandDropTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _priority = new PriorityManager(new List<Player> { _alice, _bob }, _stack, _bus, _triggers);
    }

    [Fact]
    public async Task PlayLand_FirstDropOfTurn_Succeeds()
    {
        var land = NamedCardFactory.Create("Mountain", _alice);
        land.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(land);
        var tracker = new LandDropTracker();

        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueuePriority(new PriorityAction.PlayLand(land));
        aliceAgent.QueuePriority(PriorityAction.Pass);
        var bobAgent = new ScriptedAgent();
        bobAgent.QueuePriority(PriorityAction.Pass);
        bobAgent.QueuePriority(PriorityAction.Pass);

        var loop = new PriorityLoop(
            new[] { _alice, _bob }, _priority, _stack, _resolver, _zones,
            new Dictionary<Player, IPlayerAgent>
            { [_alice] = aliceAgent, [_bob] = bobAgent },
            () => 1, () => StepStateType.PreCombatMain, tracker);

        await loop.RunUntilRoundEndsAsync(_alice);

        land.Zone.Should().Be(ZoneType.Battlefield);
        tracker.DropsUsedThisTurn(_alice).Should().Be(1);
    }

    [Fact]
    public async Task PlayLand_SecondDropOfTurn_IsRejectedAndLandStaysInHand()
    {
        // CR 305.2 — over-cap PlayLand is rejected by the priority loop
        // (swallowed + logged, mirroring the cast/activate posture so a
        // misbehaving agent can't crash the turn). The land stays in
        // hand and the counter does NOT increment.
        var l1 = NamedCardFactory.Create("Mountain", _alice);
        var l2 = NamedCardFactory.Create("Mountain", _alice);
        l1.SetZone(ZoneType.Hand); l2.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(l1);
        _alice.Zones.Hand.AddCard(l2);
        var tracker = new LandDropTracker();
        tracker.RecordLandPlayed(_alice); // pretend already played one

        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueuePriority(new PriorityAction.PlayLand(l2));
        aliceAgent.QueuePriority(PriorityAction.Pass);
        var bobAgent = new ScriptedAgent();
        bobAgent.QueuePriority(PriorityAction.Pass);
        bobAgent.QueuePriority(PriorityAction.Pass);

        var loop = new PriorityLoop(
            new[] { _alice, _bob }, _priority, _stack, _resolver, _zones,
            new Dictionary<Player, IPlayerAgent>
            { [_alice] = aliceAgent, [_bob] = bobAgent },
            () => 1, () => StepStateType.PreCombatMain, tracker);

        await loop.RunUntilRoundEndsAsync(_alice);

        l2.Zone.Should().Be(ZoneType.Hand, "rejected PlayLand should leave the land in hand");
        tracker.DropsUsedThisTurn(_alice).Should().Be(1, "rejected play must not increment the counter");
    }

    [Fact]
    public async Task PlayLand_RejectedOverCap_PassesPriority_DoesNotReAskProposer()
    {
        // Regression: a rejected PlayLand must NOT re-hand priority to the
        // proposing actor. The swallow-and-log path used to still honor the
        // action's HoldPriority flag, so an agent that re-proposed the illegal
        // land spun the round to the kActionLimit safety cap (500), flooding
        // stderr with "rejected PlayLand" lines every round of every turn.
        // The loop now treats a rejected proposal as a pass, so the proposer
        // is asked exactly ONCE. We prove that by queueing ONLY the illegal
        // land with no follow-up Pass: the old spinning loop would re-ask
        // Alice and ScriptedAgent would throw on its now-empty queue.
        var l1 = NamedCardFactory.Create("Mountain", _alice);
        var l2 = NamedCardFactory.Create("Mountain", _alice);
        l1.SetZone(ZoneType.Hand); l2.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(l1);
        _alice.Zones.Hand.AddCard(l2);
        var tracker = new LandDropTracker();
        tracker.RecordLandPlayed(_alice); // drop already spent this turn

        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueuePriority(new PriorityAction.PlayLand(l2)); // the ONLY entry
        var bobAgent = new ScriptedAgent();
        bobAgent.QueuePriority(PriorityAction.Pass);
        bobAgent.QueuePriority(PriorityAction.Pass);

        var loop = new PriorityLoop(
            new[] { _alice, _bob }, _priority, _stack, _resolver, _zones,
            new Dictionary<Player, IPlayerAgent>
            { [_alice] = aliceAgent, [_bob] = bobAgent },
            () => 1, () => StepStateType.PreCombatMain, tracker);

        var act = async () => await loop.RunUntilRoundEndsAsync(_alice);

        await act.Should().NotThrowAsync(
            "a rejected PlayLand should pass priority, not re-ask the proposer into an empty script");
        l2.Zone.Should().Be(ZoneType.Hand);
        tracker.DropsUsedThisTurn(_alice).Should().Be(1);
    }

    [Fact]
    public async Task PlayLand_FromExile_HarnfelGrant_EntersBattlefield_AndConsumesDrop()
    {
        // CR 305.2 — Harnfel, Horn of Bounty ("you may play those cards this
        // turn") on an exiled LAND stamps a runtime exile land-play grant. The
        // land is PLAYED, not cast, from the Exile zone. This proves the FULL
        // live path: an exiled land carrying the grant, proposed as a
        // PlayLand by an agent, is executed by the PriorityLoop straight from
        // Exile onto the battlefield (ZoneService.MoveCardToAsync moves it from
        // whatever zone it occupies) and still spends the one CR 305.2 land
        // drop. The agent-enumeration surface (PlayableLandsFromExile) +
        // the grant stamp are unit-covered elsewhere; this closes the
        // execution half — the loop actually plays the exiled land.
        var land = (Card)NamedCardFactory.Create("Mountain", _alice);
        _alice.Zones.Exile.AddCard(land);
        land.SetZone(ZoneType.Exile);

        // Stamp the land-play half of Harnfel's "this turn" permission.
        Majik.Core.Keywords.ExilePlayPermission.GrantUntil(
            land, _alice, land.ManaCostValue,
            Majik.Core.Keywords.ExilePlayExpiry.EndOfTurn);

        Majik.Core.Keywords.ExilePlayPermission.PlayableLandsFromExile(_alice)
            .Should().ContainSingle().Which.Should().BeSameAs(land,
                "the exiled land surfaces as a legal land drop from exile");

        var tracker = new LandDropTracker();

        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueuePriority(new PriorityAction.PlayLand(land));
        aliceAgent.QueuePriority(PriorityAction.Pass);
        var bobAgent = new ScriptedAgent();
        bobAgent.QueuePriority(PriorityAction.Pass);
        bobAgent.QueuePriority(PriorityAction.Pass);

        var loop = new PriorityLoop(
            new[] { _alice, _bob }, _priority, _stack, _resolver, _zones,
            new Dictionary<Player, IPlayerAgent>
            { [_alice] = aliceAgent, [_bob] = bobAgent },
            () => 1, () => StepStateType.PreCombatMain, tracker);

        await loop.RunUntilRoundEndsAsync(_alice);

        land.Zone.Should().Be(ZoneType.Battlefield,
            "the exiled land is played from Exile onto the battlefield");
        _alice.Zones.Exile.GetCards().Should().NotContain(land,
            "playing it from exile removes it from the exile zone");
        _alice.Zones.Battlefield.GetCards().Should().Contain(land);
        tracker.DropsUsedThisTurn(_alice).Should().Be(1,
            "playing a land from exile still consumes the CR 305.2 land drop");
    }

    [Fact]
    public void Ctor_NullLandDropTracker_Throws()
    {
        // CR 305.2 — the per-turn one-land cap is engine-level and unconditional.
        // PriorityLoop refuses to construct without a tracker so no caller can
        // accidentally fall into a "no tracker = no rule" code path.
        var act = () => new PriorityLoop(
            new[] { _alice, _bob }, _priority, _stack, _resolver, _zones,
            new Dictionary<Player, IPlayerAgent>
            { [_alice] = new ScriptedAgent(), [_bob] = new ScriptedAgent() },
            () => 1, () => StepStateType.PreCombatMain, landDropTracker: null!);

        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("landDropTracker");
    }
}
