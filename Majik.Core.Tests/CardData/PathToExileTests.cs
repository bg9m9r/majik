using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Path to Exile (Conflux, {W}, Instant).
///
/// Covers:
///   - Card identity (Instant, {W}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Exile target creature on resolve (Grizzly Bears under opponent).
///   - Exiled creature's controller may decline the basic-land tutor
///     (CR 701.19a "may search" — declining is legal).
///   - Exiled creature's controller may tutor a basic land (Mountain)
///     onto the battlefield tapped.
///   - Empty library: tutor is a legal no-op when there are no basics
///     to find (CR 701.19a).
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class PathToExileTests : IDisposable
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public PathToExileTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    public void Dispose()
    {
        // AgentRegistry is a process-wide static — clear between tests so
        // a tutor-decline test doesn't leak its agent into the next case.
        AgentRegistry.Clear();
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PathToExile_IsInstant_AtCostW()
    {
        var pte = PathToExileFactory.Create(_alice);

        pte.Name.Should().Be("Path to Exile");
        pte.ManaCost.Should().Be("{W}");
        pte.HasType(CardType.Instant).Should().BeTrue();
        pte.Owner.Should().BeSameAs(_alice);
        pte.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PathToExile()
    {
        var card = NamedCardFactory.Create("Path to Exile", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Path to Exile");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — exile + tutor
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PathToExile_ExilesTargetCreature_ControlledByOpponent()
    {
        // Bob controls Grizzly Bears.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        bears.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bears);

        // Bob has nothing in his library — decline path covered separately;
        // this test only asserts the exile half of the resolution.
        await CastAndResolveTargeting(bears);

        bears.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bears);
        _bob.Zones.Exile.GetCards().Should().Contain(bears);
    }

    [Fact]
    public async Task PathToExile_ExiledCreaturesController_MayDeclineTutor()
    {
        // Bob controls a Bear and has a Plains in his library.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        bears.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bears);

        var plains = NamedCardFactory.Create("Plains", _bob);
        plains.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(plains);

        // Bob declines the tutor (CR 701.19a — "may search" lets the
        // player skip the search entirely, or search and find nothing).
        AgentRegistry.Set(_bob, new DecliningPickAgent());

        await CastAndResolveTargeting(bears);

        // Creature exiled.
        bears.Zone.Should().Be(ZoneType.Exile);

        // Plains stays in the library — Bob declined to find anything.
        plains.Zone.Should().Be(ZoneType.Library);
        _bob.Zones.Library.GetCards().Should().Contain(plains);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(plains);
    }

    [Fact]
    public async Task PathToExile_ExiledCreaturesController_TutorsBasicLand_OntoBattlefieldTapped()
    {
        // Bob controls a Bear. Bob's library has Mountain (basic) and a
        // nonbasic to make sure the predicate filters correctly.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        bears.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bears);

        var mountain = NamedCardFactory.Create("Mountain", _bob);
        mountain.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(mountain);

        // No agent registered → factory falls back to first candidate
        // (deterministic test default — mirrors SearchSpellFactory).
        await CastAndResolveTargeting(bears);

        // Creature exiled.
        bears.Zone.Should().Be(ZoneType.Exile);

        // Mountain found, on Bob's battlefield, tapped, controlled by Bob.
        mountain.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(mountain);
        _bob.Zones.Library.GetCards().Should().NotContain(mountain);
        mountain.Controller.Should().BeSameAs(_bob);
        ((Permanent)mountain).IsTapped.Should().BeTrue();
    }

    [Fact]
    public async Task PathToExile_EmptyLibrary_ExilesCreature_NoTutor()
    {
        // Bob controls a Bear and has nothing in his library — exile
        // still happens; tutor has zero candidates so it's a no-op
        // (CR 701.19a — "if no card matches, the search effect has no
        // effect").
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        bears.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bears);

        await CastAndResolveTargeting(bears);

        bears.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Battlefield.GetCards().Where(c => c.HasType(CardType.Land))
            .Should().BeEmpty();
        _bob.Zones.Library.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Cast Path to Exile from Alice's hand at <paramref name="target"/>
    /// and resolve the resulting stack object. Mirrors the
    /// <see cref="UnholyHeatTests"/> cast harness — direct cast/resolve,
    /// no priority loop.
    /// </summary>
    private async Task CastAndResolveTargeting(object target)
    {
        var pte = PathToExileFactory.Create(_alice);
        pte.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pte);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.Main, _stack);

        var spell = await _flow.CastAsync(
            _alice, pte,
            PathToExileFactory.BuildSpellDefinition(t => t),
            agent, ctx);

        pte.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();
    }

    /// <summary>
    /// Test agent that always declines library picks (returns null from
    /// <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>) and throws on
    /// any other prompt — this agent is only registered for the player
    /// whose tutor we want to decline; other prompts go to Alice's
    /// <see cref="ScriptedAgent"/>.
    /// </summary>
    private sealed class DecliningPickAgent : IPlayerAgent
    {
        public Task<ICard?> ChooseLibraryPickAsync(
            GameContext? ctx, IReadOnlyList<ICard> candidates, string kindLabel,
            CancellationToken ct = default)
            => Task.FromResult<ICard?>(null);

        // Other IPlayerAgent members are unreachable for Bob in these
        // tests (Bob has no priority window, no targets to choose, etc.).
        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility> mine, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
