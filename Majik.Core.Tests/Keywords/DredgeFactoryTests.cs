using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// Tests for <see cref="DredgeFactory"/> — the shared graveyard-anchored
/// draw replacement builder for the Dredge keyword (CR 702.52).
///
/// Covers:
/// - Build attaches a <see cref="KeywordAbility"/> "Dredge" marker with
///   <see cref="KeywordAbility.Arg"/> = N.
/// - Argument validation: N must be positive; source.Owner must be wired.
/// - Replacement fires only while source is in controller's graveyard
///   (CR 702.52a).
/// - Library &lt; N gates out the offer (CR 702.52b).
/// - Agent yes path: mill N + return source to hand + cancel underlying
///   draw.
/// - Agent no path: draw resolves normally.
/// - Mid-mill empty library halts cleanly without stamping the empty-
///   library loss flag (CR 704.5b is for draws, not mills).
/// - No-bus shape-only path attaches the marker without firing a
///   replacement.
/// </summary>
public class DredgeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Shape — KeywordAbility marker
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_AttachesDredgeKeywordMarker_WithArg()
    {
        var card = MakeCardInGraveyard("Stinkweed Imp");

        DredgeFactory.Build(card, n: 5);

        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Dredge")
            .Which.Arg.Should().Be(5);
    }

    [Fact]
    public void Build_ShapeOnlyWithoutBus_AttachesMarker_NoReplacementFired()
    {
        var card = MakeCardInGraveyard("Stinkweed Imp");

        DredgeFactory.Build(card, n: 5, replacementBus: null);

        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Dredge");
        // No bus -> Fx.DrawCards doesn't route through one; an attempted
        // draw resolves directly (shape-only path).
        FillLibrary(_alice, 10);
        Fx.DrawCards(_alice, 1);
        _alice.Zones.Hand.Count.Should().Be(1, "shape-only path: no replacement, draw resolves");
    }

    // -----------------------------------------------------------------------
    // Argument validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_ThrowsWhenNNonPositive()
    {
        var card = MakeCardInGraveyard("Stinkweed Imp");
        var act = () => DredgeFactory.Build(card, n: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Build_ThrowsWhenOwnerNotWired()
    {
        var card = new Card("Stinkweed Imp", "{1}{B}"); // no SetOwner
        var act = () => DredgeFactory.Build(card, n: 5);
        act.Should().Throw<ArgumentException>("replacement gates on source.Owner");
    }

    [Fact]
    public void Build_ThrowsOnNullSource()
    {
        var act = () => DredgeFactory.Build(null!, n: 5);
        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Primitive — accept path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Dredge_AgentYes_MillsN_ReturnsSourceToHand_CancelsDraw()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var imp = MakeCardInGraveyard("Stinkweed Imp");
        DredgeFactory.Build(imp, n: 5, replacementBus: bus);

        var lib = FillLibrary(_alice, 8);

        // Agent says yes to the dredge prompt. CR 702.52 prompting happens on
        // the async draw path (Fx.DrawCardsAsync → ApplyAsync).
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        var ctx = ResolutionContext.For(_alice, agent, game: null, chosenTargets: null);

        var drawn = await Fx.DrawCardsAsync(_alice, 1, ctx);

        // Original draw cancelled — no card added to hand from the
        // library top.
        drawn.Should().BeEmpty("Dredge consumed the draw");

        // Source returned from graveyard to hand.
        imp.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(imp);

        // N=5 cards milled from top into graveyard.
        _alice.Zones.Graveyard.GetCards()
            .Where(c => !ReferenceEquals(c, imp))
            .Should().HaveCount(5,
                "Dredge milled exactly N=5 cards on top of the previous graveyard contents");
        _alice.Zones.Library.GetCards().Should().HaveCount(3,
            "8 - 5 milled = 3 remaining");
    }

    // -----------------------------------------------------------------------
    // Primitive — decline path
    // -----------------------------------------------------------------------

    [Fact]
    public void Dredge_AgentNo_DrawResolvesNormally()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var imp = MakeCardInGraveyard("Stinkweed Imp");
        DredgeFactory.Build(imp, n: 5, replacementBus: bus);

        FillLibrary(_alice, 8);

        // Agent declines the dredge.
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(_alice, agent);

        try
        {
            var drawn = Fx.DrawCards(_alice, 1);

            drawn.Should().HaveCount(1, "decline -> straight draw");
            imp.Zone.Should().Be(ZoneType.Graveyard,
                "decline leaves Stinkweed Imp in graveyard for a future dredge");
            _alice.Zones.Library.Count.Should().Be(7, "1 drawn from top, none milled");
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // CR 702.52b — Library < N gates the offer out
    // -----------------------------------------------------------------------

    [Fact]
    public void Dredge_LibraryFewerThanN_OfferGated_AgentNotPrompted()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var imp = MakeCardInGraveyard("Stinkweed Imp");
        DredgeFactory.Build(imp, n: 5, replacementBus: bus);

        FillLibrary(_alice, 4); // only 4 < N=5

        var agent = new ScriptedAgent();
        // Don't queue any yes/no — if the agent IS prompted Pop would throw.
        AgentRegistry.Set(_alice, agent);

        try
        {
            var drawn = Fx.DrawCards(_alice, 1);

            drawn.Should().HaveCount(1,
                "library < N gates the Dredge offer out, so the underlying draw resolves");
            imp.Zone.Should().Be(ZoneType.Graveyard);
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // Source-anchored — only fires while in controller's graveyard
    // -----------------------------------------------------------------------

    [Fact]
    public void Dredge_SourceNotInGraveyard_ReplacementDoesNotApply()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var imp = MakeCardInGraveyard("Stinkweed Imp");
        DredgeFactory.Build(imp, n: 5, replacementBus: bus);

        FillLibrary(_alice, 8);

        // Move the source out of graveyard (e.g. exiled / reanimated).
        _alice.Zones.Graveyard.RemoveCard(imp);
        _alice.Zones.Exile.AddCard(imp);
        imp.SetZone(ZoneType.Exile);

        var agent = new ScriptedAgent();
        AgentRegistry.Set(_alice, agent);

        try
        {
            var drawn = Fx.DrawCards(_alice, 1);
            drawn.Should().HaveCount(1, "source not in graveyard -> replacement gated out");
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // CR 701.13 mid-mill empty library — halts cleanly
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Dredge_EmptyLibraryMidMill_HaltsCleanly()
    {
        // Build a library with EXACTLY N cards. Library.Count >= N gate
        // passes; the mill empties the library completely. Subsequent
        // draws would fire the empty-library loss flag but Dredge
        // consumed THIS draw so the flag stays clear.
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var imp = MakeCardInGraveyard("Stinkweed Imp");
        DredgeFactory.Build(imp, n: 5, replacementBus: bus);

        FillLibrary(_alice, 5);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        var ctx = ResolutionContext.For(_alice, agent, game: null, chosenTargets: null);

        await Fx.DrawCardsAsync(_alice, 1, ctx);

        _alice.Zones.Library.Count.Should().Be(0, "all 5 cards milled");
        imp.Zone.Should().Be(ZoneType.Hand, "Dredge returned the source to hand");
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse(
            "Dredge mills (CR 701.13); milling an empty library is not a draw from empty (CR 704.5b)");
    }

    // -----------------------------------------------------------------------
    // No agent registered — straight draw posture
    // -----------------------------------------------------------------------

    [Fact]
    public void Dredge_NoAgent_DefaultsToStraightDraw()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var imp = MakeCardInGraveyard("Stinkweed Imp");
        DredgeFactory.Build(imp, n: 5, replacementBus: bus);

        FillLibrary(_alice, 8);

        // No AgentRegistry.Set call.
        var drawn = Fx.DrawCards(_alice, 1);

        drawn.Should().HaveCount(1, "no agent -> conservative posture: straight draw");
        imp.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // PLAN 08 — async replacement path (Fx.DrawCardsAsync / ApplyAsync). The
    // Dredge "dredge?" prompt is genuinely awaited off the ResolutionContext.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Dredge_Async_AgentYes_MillsN_ReturnsSourceToHand_CancelsDraw()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var imp = MakeCardInGraveyard("Stinkweed Imp");
        DredgeFactory.Build(imp, n: 5, replacementBus: bus);
        FillLibrary(_alice, 8);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        var ctx = ResolutionContext.For(_alice, agent, game: null, chosenTargets: null);

        var drawn = await Fx.DrawCardsAsync(_alice, 1, ctx);

        drawn.Should().BeEmpty("Dredge consumed the draw");
        imp.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.Count.Should().Be(3, "8 - 5 milled = 3 remaining");
    }

    [Fact]
    public async Task Dredge_Async_GenuinelyAwaitsHuman_NoSyncBridge()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var imp = MakeCardInGraveyard("Stinkweed Imp");
        DredgeFactory.Build(imp, n: 5, replacementBus: bus);
        FillLibrary(_alice, 8);

        var human = new DeferredDredgeAgent();
        var ctx = ResolutionContext.For(_alice, human, game: null, chosenTargets: null);

        var drawTask = Fx.DrawCardsAsync(_alice, 1, ctx);

        human.WasPrompted.Should().BeTrue("the Dredge replacement awaited the agent");
        drawTask.IsCompleted.Should().BeFalse(
            "the human has not answered yet — no sync-over-async bridge");
        imp.Zone.Should().Be(ZoneType.Graveyard, "nothing happens while the human thinks");

        human.Answer(true);
        var drawn = await drawTask;

        drawn.Should().BeEmpty("human dredged → original draw cancelled");
        imp.Zone.Should().Be(ZoneType.Hand, "human's yes returned the source to hand");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Card MakeCardInGraveyard(string name)
    {
        var card = new Card(name, "{1}{B}");
        card.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
        return card;
    }

    private static List<Card> FillLibrary(Player p, int count)
    {
        var made = new List<Card>(count);
        for (int i = 0; i < count; i++)
        {
            var c = new Card($"Lib-{i}", "{0}");
            c.SetOwner(p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
            made.Add(c);
        }
        return made;
    }

    /// <summary>
    /// Human-think-time agent whose Dredge yes/no parks on a TCS until
    /// <see cref="Answer"/> is called. Proves the Dredge replacement genuinely
    /// awaits the agent (no sync-over-async bridge). Only the yes/no prompt is
    /// exercised; the rest of the surface throws.
    /// </summary>
    private sealed class DeferredDredgeAgent : Majik.Core.Players.Agents.IPlayerAgent
    {
        private readonly TaskCompletionSource<bool> _yesNo =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasPrompted { get; private set; }
        public void Answer(bool yes) => _yesNo.SetResult(yes);

        public Task<bool> ChooseYesNoAsync(string question, BotIntent intent, CancellationToken ct = default)
        {
            WasPrompted = true;
            return _yesNo.Task;
        }

        public Task<Majik.Core.Players.Agents.PriorityAction> ChoosePriorityActionAsync(Majik.Core.Game.GameContext ctx, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Majik.Core.Players.Agents.MulliganDecision> ChooseMulliganAsync(Majik.Core.Game.GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(Majik.Core.Game.GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(Majik.Core.Game.GameContext ctx, Majik.Core.Players.Agents.TargetRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> ChooseXAsync(Majik.Core.Game.GameContext ctx, ICard source, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> ChooseModeAsync(Majik.Core.Game.GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(Majik.Core.Game.GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Majik.Core.Players.Agents.ManaPayment> ChooseManaSourcesAsync(Majik.Core.Game.GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Majik.Core.Players.Agents.CombatPlan> DeclareAttackersAsync(Majik.Core.Game.GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Majik.Core.Players.Agents.BlockPlan> DeclareBlockersAsync(Majik.Core.Game.GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(Majik.Core.Game.GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(Majik.Core.Game.GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
