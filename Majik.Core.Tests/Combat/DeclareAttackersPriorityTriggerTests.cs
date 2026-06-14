using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Combat;

/// <summary>
/// CR 508.4 / 509.4 — the declare-attackers and declare-blockers steps each
/// GRANT PRIORITY after the relevant declaration. "Whenever ~ attacks" triggers
/// (CR 508.1f) are put on the stack (CR 603.3) and resolve DURING the
/// declare-attackers step — BEFORE combat damage — not after the damage step.
///
/// Driving case: Goblin Guide ("Whenever Goblin Guide attacks, defending player
/// reveals the top card of their library; if it's a land card, that player puts
/// it into their hand"). The land must reach the defender's hand before any
/// combat damage is dealt.
///
/// These run the live async combat flow through <see cref="TurnDriver"/> (the
/// production PriorityRound → PriorityLoop path) — the bug only manifests through
/// the real flow, not by invoking the effect directly.
/// </summary>
public class DeclareAttackersPriorityTriggerTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly StateBasedActions _sba;
    private readonly PriorityManager _priority;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public DeclareAttackersPriorityTriggerTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _sba = new StateBasedActions(_bus, _zones, _triggers);
        _priority = new PriorityManager(new List<Player> { _alice, _bob }, _stack, _bus, _triggers);
    }

    [Fact]
    public async Task GoblinGuideAttackTrigger_ResolvesDuringDeclareAttackers_LandReachesHandBeforeDamage()
    {
        // Alice's Goblin Guide (haste) attacks Bob. Bob's top card is a LAND.
        var guide = GoblinGuideFactory.Create(_alice, _zones, _bus, _triggers);
        guide.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(guide);

        // Bob's library top is a Mountain (a land) — Goblin Guide's trigger
        // should pull it into his hand. Two more cards under it so the draw
        // step (if any) and SBAs don't empty his library.
        var topLand = NamedCardFactory.Create("Mountain", _bob);
        _bob.Zones.Library.AddCard(topLand);
        topLand.SetZone(ZoneType.Library);
        SeedLibrary(_bob, 3);
        SeedLibrary(_alice, 3);

        // Record the order of two key events: the land moving Library → Hand
        // (the trigger resolving) and Goblin Guide dealing combat damage.
        var landMovedToHandFirst = false;
        var sawDamage = false;
        _bus.Subscribe<CardMovedEvent>(e =>
        {
            if (ReferenceEquals(e.Card, topLand)
                && e.ToZone == ZoneType.Hand
                && !sawDamage)
            {
                landMovedToHandFirst = true;
            }
        });
        _bus.Subscribe<CombatDamageDealtEvent>(_ => sawDamage = true);

        var aliceAgent = new AttackAllAgent(_bob);
        var bobAgent = new AttackAllAgent(_alice);
        var driver = NewDriver(aliceAgent, bobAgent);

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        _bob.Zones.Hand.GetCards().Should().Contain(topLand,
            because: "Goblin Guide's attack trigger put the revealed land into the defender's hand");
        sawDamage.Should().BeTrue(because: "Goblin Guide attacked unblocked and dealt combat damage");
        landMovedToHandFirst.Should().BeTrue(
            because: "the attack trigger must resolve DURING the declare-attackers step, BEFORE combat damage (CR 508.4)");
    }

    [Fact]
    public async Task GoblinGuideAttackTrigger_NonLandTop_StaysOnLibrary()
    {
        // Top card is a NON-land (Grizzly Bears) — revealed but stays on top.
        var guide = GoblinGuideFactory.Create(_alice, _zones, _bus, _triggers);
        guide.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(guide);

        var topCreature = NamedCardFactory.Create("Grizzly Bears", _bob);
        _bob.Zones.Library.AddCard(topCreature);
        topCreature.SetZone(ZoneType.Library);
        SeedLibrary(_bob, 3);
        SeedLibrary(_alice, 3);

        var revealed = false;
        _bus.Subscribe<CardRevealedEvent>(e =>
        {
            if (ReferenceEquals(e.Card, topCreature)) revealed = true;
        });

        var aliceAgent = new AttackAllAgent(_bob);
        var bobAgent = new AttackAllAgent(_alice);
        var driver = NewDriver(aliceAgent, bobAgent);

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        revealed.Should().BeTrue(because: "Goblin Guide reveals the defender's top card on attack");
        _bob.Zones.Hand.GetCards().Should().NotContain(topCreature,
            because: "a non-land revealed card stays on top of the library");
        _bob.Zones.Library.GetCards().Should().Contain(topCreature,
            because: "a non-land revealed card stays on the library");
    }

    private TurnDriver NewDriver(IPlayerAgent aliceAgent, IPlayerAgent bobAgent)
    {
        return new TurnDriver(
            players: new[] { _alice, _bob },
            agents: new Dictionary<Player, IPlayerAgent>
            {
                [_alice] = aliceAgent,
                [_bob] = bobAgent,
            },
            stack: _stack,
            zoneService: _zones,
            triggerManager: _triggers,
            stackResolver: _resolver,
            stateBasedActions: _sba,
            priorityManager: _priority,
            combatFlow: new CombatFlow(_bus, _sba),
            eventBus: _bus);
    }

    private static void SeedLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = NamedCardFactory.Create("Mountain", p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    /// <summary>
    /// Passes every priority window, never blocks, but declares ALL eligible
    /// attackers against the given defender so combat actually happens.
    /// </summary>
    private sealed class AttackAllAgent : IPlayerAgent
    {
        private readonly Player _defender;
        public AttackAllAgent(Player defender) => _defender = defender;

        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Permanent> eligibleAttackers, CancellationToken ct = default)
            => Task.FromResult(new CombatPlan(
                eligibleAttackers
                    .Select(a => new Majik.Core.Players.Agents.AttackerDeclaration(a, _defender))
                    .ToList()));

        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Permanent> attackers, IReadOnlyList<Permanent> eligibleBlockers, CancellationToken ct = default)
            => Task.FromResult(BlockPlan.None);

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => Task.FromResult(PriorityAction.Pass);
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => Task.FromResult(MulliganDecision.Keep);
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ICard>>(Array.Empty<ICard>());
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<object>>(request.LegalCandidates.Take(request.MinTargets).ToList());
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<Majik.Core.Cards.BotIntent>? modeIntents = null, CancellationToken ct = default) => Task.FromResult(0);
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ITriggeredAbility>>(mine);
        public Task<Majik.Core.Players.Agents.ManaPayment> ChooseManaSourcesAsync(GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
            => Task.FromResult(Majik.Core.Players.Agents.ManaPayment.Empty);
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.ScryAction.ScryDecision(ToBottom: peeked.ToList(), TopOrder: Array.Empty<ICard>()));
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.SurveilAction.SurveilDecision(ToGraveyard: peeked.ToList(), TopOrder: Array.Empty<ICard>()));
    }
}
