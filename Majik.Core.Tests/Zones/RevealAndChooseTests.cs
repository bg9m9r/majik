using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Zones;

/// <summary>
/// CR 701.15 — coverage for the shared reveal-and-choose helper. Three
/// invariants:
///   1) Library underflow: revealing N from a library with fewer than N
///      cards reveals what's there + no crash.
///   2) Agent is prompted EVEN when the eligible subset is empty so the
///      player sees the reveal pile (same UX principle as
///      <see cref="LibrarySearch"/> on an empty candidates list).
///   3) Zone moves go through <see cref="ZoneServiceRegistry"/> when
///      registered so <see cref="CardMovedEvent"/> fires + ETB triggers
///      observe the picked / discarded cards.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class RevealAndChooseTests : IDisposable
{
    private readonly Player _alice;

    public RevealAndChooseTests()
    {
        AgentRegistry.Clear();
        ZoneServiceRegistry.Clear();
        GameRandomRegistry.Clear();
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(null);
        GameRandomRegistry.SetDefault(new GameRandom(seed: 0));
        _alice = new Player("Alice");
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
        ZoneServiceRegistry.Clear();
        GameRandomRegistry.Clear();
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(null);
    }

    private sealed class RecordingAgent : IPlayerAgent
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<ICard>? LastRevealed { get; private set; }
        public IReadOnlyList<ICard>? LastEligible { get; private set; }
        public bool LastOptional { get; private set; }
        public string? LastLabel { get; private set; }
        public ICard? PickToReturn { get; init; }

        public Task<ICard?> ChooseFromRevealedAsync(
            GameContext? ctx,
            IReadOnlyList<ICard> revealed,
            IReadOnlyList<ICard> eligible,
            bool optional,
            string label,
            CancellationToken ct = default)
        {
            CallCount++;
            LastRevealed = revealed;
            LastEligible = eligible;
            LastOptional = optional;
            LastLabel = label;
            return Task.FromResult(PickToReturn);
        }

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private static Creature Creat(string name, Player owner)
    {
        var c = new Creature(name, "1G", 1, 1);
        c.SetOwner(owner);
        return c;
    }

    private static Instant Inst(string name, Player owner)
    {
        var c = new Instant(name, "U");
        c.SetOwner(owner);
        return c;
    }

    private static bool IsPermanent(ICard c) =>
        c.HasType(CardType.Creature) ||
        c.HasType(CardType.Artifact) ||
        c.HasType(CardType.Enchantment) ||
        c.HasType(CardType.Land) ||
        c.HasType(CardType.Planeswalker);

    [Fact]
    public void RevealTopAndChoose_EmptyLibrary_DoesNotPromptAndReturnsNull()
    {
        var agent = new RecordingAgent { PickToReturn = null };
        AgentRegistry.Set(_alice, agent);

        var result = RevealAndChoose.RevealTopAndChoose(
            _alice, count: 4, IsPermanent, optional: true,
            "permanent", ZoneType.Hand, ZoneType.Graveyard,
            sourceTag: "test");

        result.Should().BeNull();
        // Empty reveal pile means there's nothing to look at, so no
        // prompt fires — distinct from the empty-eligible case below
        // (where the player still sees the reveal pile).
        agent.CallCount.Should().Be(0);
    }

    [Fact]
    public void RevealTopAndChoose_EmptyEligible_StillPromptsAgent_ReturnsNull()
    {
        var i1 = Inst("Counterspell", _alice);
        var i2 = Inst("Shock", _alice);
        _alice.Zones.Library.AddCard(i1);
        i1.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(i2);
        i2.SetZone(ZoneType.Library);

        var agent = new RecordingAgent { PickToReturn = null };
        AgentRegistry.Set(_alice, agent);

        var result = RevealAndChoose.RevealTopAndChoose(
            _alice, count: 4, IsPermanent, optional: true,
            "permanent", ZoneType.Hand, ZoneType.Graveyard,
            sourceTag: "test");

        // Even though no card matches the predicate, the agent is
        // prompted so the player sees the reveal pile.
        agent.CallCount.Should().Be(1);
        agent.LastRevealed.Should().HaveCount(2);
        agent.LastEligible.Should().BeEmpty();
        result.Should().BeNull();
        // All revealed cards go to the rest destination.
        _alice.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { i1, i2 });
    }

    [Fact]
    public void RevealTopAndChoose_AgentPicksEligibleCard_MovesToPickedDestination()
    {
        var bear = Creat("Bear", _alice);
        var bolt = Inst("Bolt", _alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        var agent = new RecordingAgent { PickToReturn = bear };
        AgentRegistry.Set(_alice, agent);

        var result = RevealAndChoose.RevealTopAndChoose(
            _alice, count: 4, IsPermanent, optional: true,
            "permanent", ZoneType.Hand, ZoneType.Graveyard,
            sourceTag: "test");

        result.Should().BeSameAs(bear);
        _alice.Zones.Hand.GetCards().Should().Contain(bear);
        _alice.Zones.Graveyard.GetCards().Should().Contain(bolt);
        _alice.Zones.Library.GetCards().Should().NotContain(bear);
        _alice.Zones.Library.GetCards().Should().NotContain(bolt);
    }

    [Fact]
    public void RevealTopAndChoose_OptionalAgentDeclines_AllToRestDestination()
    {
        var bear = Creat("Bear", _alice);
        _alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        var agent = new RecordingAgent { PickToReturn = null };
        AgentRegistry.Set(_alice, agent);

        var result = RevealAndChoose.RevealTopAndChoose(
            _alice, count: 4, IsPermanent, optional: true,
            "permanent", ZoneType.Hand, ZoneType.Graveyard,
            sourceTag: "test");

        result.Should().BeNull();
        // CR 116.1b — "you may" decline. Bear goes to graveyard with rest.
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void RevealTopAndChoose_NoAgent_FallsBackToFirstEligible()
    {
        var bolt = Inst("Bolt", _alice);
        var bear = Creat("Bear", _alice);
        // Library reads top→bottom in AddCard order — bolt is on top.
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        var result = RevealAndChoose.RevealTopAndChoose(
            _alice, count: 4, IsPermanent, optional: true,
            "permanent", ZoneType.Hand, ZoneType.Graveyard,
            sourceTag: "test");

        // No agent — falls back to first eligible (bear).
        result.Should().BeSameAs(bear);
        _alice.Zones.Hand.GetCards().Should().Contain(bear);
        _alice.Zones.Graveyard.GetCards().Should().Contain(bolt);
    }

    [Fact]
    public void RevealTopAndChoose_AgentReturnsIneligibleCard_CoercedToDecline()
    {
        var bolt = Inst("Bolt", _alice);
        var bear = Creat("Bear", _alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        // Agent returns the bolt (not eligible because IsPermanent excludes
        // instants) — helper defensively coerces to decline.
        var agent = new RecordingAgent { PickToReturn = bolt };
        AgentRegistry.Set(_alice, agent);

        var result = RevealAndChoose.RevealTopAndChoose(
            _alice, count: 4, IsPermanent, optional: true,
            "permanent", ZoneType.Hand, ZoneType.Graveyard,
            sourceTag: "test");

        result.Should().BeNull();
        // Bear stays in the rest pile because the agent never picked it.
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { bolt, bear });
    }

    [Fact]
    public void RevealTopAndChoose_LibrarySmallerThanCount_RevealsWhatsThere()
    {
        var bear = Creat("Bear", _alice);
        _alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        var agent = new RecordingAgent { PickToReturn = bear };
        AgentRegistry.Set(_alice, agent);

        var result = RevealAndChoose.RevealTopAndChoose(
            _alice, count: 4, IsPermanent, optional: true,
            "permanent", ZoneType.Hand, ZoneType.Graveyard,
            sourceTag: "test");

        result.Should().BeSameAs(bear);
        agent.LastRevealed.Should().HaveCount(1);
        _alice.Zones.Hand.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void RevealTopAndChoose_RoutesMovesThroughZoneServiceRegistry()
    {
        var bear = Creat("Bear", _alice);
        var bolt = Inst("Bolt", _alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        var bus = new EventBus();
        var observed = new List<CardMovedEvent>();
        bus.Subscribe<CardMovedEvent>(observed.Add);
        var zones = new ZoneService(bus);
        ZoneServiceRegistry.Set(_alice, zones);

        var agent = new RecordingAgent { PickToReturn = bear };
        AgentRegistry.Set(_alice, agent);

        _ = RevealAndChoose.RevealTopAndChoose(
            _alice, count: 4, IsPermanent, optional: true,
            "permanent", ZoneType.Hand, ZoneType.Graveyard,
            sourceTag: "test");

        // Two CardMovedEvent emissions — one for the picked bear (lib→hand)
        // and one for the discarded bolt (lib→graveyard).
        observed.Should().HaveCount(2);
        observed.Should().Contain(e => ReferenceEquals(e.Card, bear) && e.ToZone == ZoneType.Hand);
        observed.Should().Contain(e => ReferenceEquals(e.Card, bolt) && e.ToZone == ZoneType.Graveyard);
    }
}
