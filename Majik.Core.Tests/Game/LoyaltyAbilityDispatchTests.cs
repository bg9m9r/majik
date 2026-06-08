using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// End-to-end gameplay tests for planeswalker loyalty abilities (CR 606):
/// driving a real <see cref="TurnDriver.RunTurnAsync"/> turn, the active
/// player's agent activates a loyalty ability in its main phase; the loyalty
/// cost is paid as it goes on the stack, the effect resolves off the stack,
/// targets are prompted and the CHOSEN object is affected.
/// </summary>
public class LoyaltyAbilityDispatchTests
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

    public LoyaltyAbilityDispatchTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _sba = new StateBasedActions(_bus, _zones, _triggers);
        _priority = new PriorityManager(new List<Player> { _alice, _bob }, _stack, _bus, _triggers);
    }

    private TurnDriver NewDriver(IPlayerAgent aliceAgent, IPlayerAgent bobAgent)
        => new(
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
            combatFlow: new CombatFlow(_bus, _sba));

    private static void SeedLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = NamedCardFactory.Create("Mountain", p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    [Fact]
    public async Task PlusOne_ActivatedThroughTurn_RaisesLoyalty_AndResolvesEffect()
    {
        SeedLibrary(_alice, 5);
        SeedLibrary(_bob, 5);

        var tokenCreated = false;
        var pw = new Planeswalker("Test Walker", "{2}{U}", 3);
        pw.ChangeOwner(_alice);
        pw.ChangeController(_alice);
        _alice.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);
        pw.AddAbility(new LoyaltyAbility(pw, +1, () => tokenCreated = true));

        var alice = new LoyaltyActivatingAgent(pw, loyaltyChange: +1);
        var driver = NewDriver(alice, new PassAgent());

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        pw.Loyalty.Should().Be(4, "+1 raises loyalty 3 -> 4 (paid as it went on the stack)");
        tokenCreated.Should().BeTrue("the effect resolved off the stack");
        pw.LoyaltyAbilityActivatedThisTurn.Should().BeTrue("once-per-turn flag set");
        _stack.IsEmpty.Should().BeTrue("the loyalty ability resolved and left the stack");
    }

    [Fact]
    public async Task MinusAbility_ActivatedThroughTurn_LowersLoyalty()
    {
        SeedLibrary(_alice, 5);
        SeedLibrary(_bob, 5);

        var pw = new Planeswalker("Test Walker", "{2}{U}", 4);
        pw.ChangeOwner(_alice);
        pw.ChangeController(_alice);
        _alice.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);
        pw.AddAbility(new LoyaltyAbility(pw, -2, () => { }));

        var alice = new LoyaltyActivatingAgent(pw, loyaltyChange: -2);
        var driver = NewDriver(alice, new PassAgent());

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        pw.Loyalty.Should().Be(2, "−2 lowers loyalty 4 -> 2");
    }

    [Fact]
    public async Task LilianaMinus2_PromptsForTargetPlayer_ChosenPlayerSacrifices()
    {
        SeedLibrary(_alice, 5);
        SeedLibrary(_bob, 5);

        var liliana = LilianaOfTheVeilFactory.Create(_alice);
        liliana.ChangeOwner(_alice);
        liliana.ChangeController(_alice);
        liliana.AddLoyalty(0); // ensure at base loyalty 3 (>= 2 for the −2).
        _alice.Zones.Battlefield.AddCard(liliana);
        liliana.SetZone(ZoneType.Battlefield);

        // Bob has a creature to sacrifice.
        var victim = new Creature("Tarmogoyf", "{1}{G}", 4, 5);
        victim.ChangeOwner(_bob);
        victim.ChangeController(_bob);
        _bob.Zones.Battlefield.AddCard(victim);
        victim.SetZone(ZoneType.Battlefield);

        // Agent activates the −2 and chooses Bob as the target player.
        var alice = new LoyaltyActivatingAgent(liliana, loyaltyChange: -2)
        { TargetChoice = _bob };
        var driver = NewDriver(alice, new PassAgent());

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        liliana.Loyalty.Should().Be(1, "−2 lowers loyalty 3 -> 1");
        _bob.Zones.Graveyard.GetCards().Should().Contain(victim,
            "the chosen target player sacrificed a creature");
    }

    [Fact]
    public async Task KothPlus1_PromptsForTargetMountain_ChosenMountainUntaps()
    {
        SeedLibrary(_alice, 5);
        SeedLibrary(_bob, 5);

        var koth = KothOfTheHammerFactory.Create(_alice);
        koth.ChangeOwner(_alice);
        koth.ChangeController(_alice);
        _alice.Zones.Battlefield.AddCard(koth);
        koth.SetZone(ZoneType.Battlefield);

        // Two Mountains; the agent will choose the second one. They are
        // re-tapped by the agent just before it activates the +1 (the turn's
        // untap step would otherwise have untapped both) so the resolution-time
        // untap of the CHOSEN Mountain is observable.
        var mtn1 = (Land)NamedCardFactory.Create("Mountain", _alice);
        mtn1.ChangeController(_alice);
        _alice.Zones.Battlefield.AddCard(mtn1);
        mtn1.SetZone(ZoneType.Battlefield);

        var mtn2 = (Land)NamedCardFactory.Create("Mountain", _alice);
        mtn2.ChangeController(_alice);
        _alice.Zones.Battlefield.AddCard(mtn2);
        mtn2.SetZone(ZoneType.Battlefield);

        var alice = new LoyaltyActivatingAgent(koth, loyaltyChange: +1)
        {
            TargetChoice = mtn2,
            BeforeActivation = () => { mtn1.Tap(); mtn2.Tap(); },
        };
        var driver = NewDriver(alice, new PassAgent());

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        koth.Loyalty.Should().Be(4, "+1 raises loyalty 3 -> 4");
        mtn2.IsTapped.Should().BeFalse("the CHOSEN Mountain untapped");
        mtn1.IsTapped.Should().BeTrue("the non-chosen Mountain stayed tapped");
    }

    [Fact]
    public async Task GristMinus2_PromptsForDestroyTarget_ChosenPermanentIsDestroyed()
    {
        SeedLibrary(_alice, 5);
        SeedLibrary(_bob, 5);

        // Alice controls a creature to sacrifice (supplied via the resolver —
        // "you may sacrifice" is a choice, not a target).
        var sac = new Creature("Sakura-Tribe Elder", "{1}{G}", 1, 1);
        sac.ChangeOwner(_alice);
        sac.ChangeController(_alice);
        _alice.Zones.Battlefield.AddCard(sac);
        sac.SetZone(ZoneType.Battlefield);

        var grist = GristFactory.Create(
            _alice,
            zones: _zones,
            sacrificeResolver: () => new[] { sac },
            destroyTargetResolver: null,
            opponentsResolver: null);
        grist.ChangeOwner(_alice);
        grist.ChangeController(_alice);
        _alice.Zones.Battlefield.AddCard(grist);
        grist.SetZone(ZoneType.Battlefield);

        // Bob controls two creatures; the agent will choose the second to destroy.
        var decoy = new Creature("Llanowar Elves", "{G}", 1, 1);
        decoy.ChangeOwner(_bob);
        decoy.ChangeController(_bob);
        _bob.Zones.Battlefield.AddCard(decoy);
        decoy.SetZone(ZoneType.Battlefield);

        var victim = new Creature("Tarmogoyf", "{1}{G}", 4, 5);
        victim.ChangeOwner(_bob);
        victim.ChangeController(_bob);
        _bob.Zones.Battlefield.AddCard(victim);
        victim.SetZone(ZoneType.Battlefield);

        var alice = new LoyaltyActivatingAgent(grist, loyaltyChange: -2)
        { TargetChoice = victim };
        var driver = NewDriver(alice, new PassAgent());

        await driver.RunTurnAsync(_alice, turnNumber: 2);

        grist.Loyalty.Should().Be(1, "−2 lowers loyalty 3 -> 1");
        _alice.Zones.Graveyard.GetCards().Should().Contain(sac, "the chosen creature was sacrificed");
        _bob.Zones.Graveyard.GetCards().Should().Contain(victim, "the CHOSEN target was destroyed");
        _bob.Zones.Battlefield.GetCards().Should().Contain(decoy, "the non-chosen creature survived");
    }

    // -----------------------------------------------------------------------
    // Test agents.
    // -----------------------------------------------------------------------

    /// <summary>Agent that activates a specific loyalty ability the first time
    /// it is offered a priority window where it is legal, supplies a chosen
    /// target, then passes for the rest of the turn.</summary>
    private sealed class LoyaltyActivatingAgent : PassAgent
    {
        private readonly Planeswalker _walker;
        private readonly int _loyaltyChange;
        private bool _activated;

        public object? TargetChoice { get; init; }

        /// <summary>Side-effect run once, immediately before the activation is
        /// proposed (e.g. re-tap lands the untap step cleared).</summary>
        public System.Action? BeforeActivation { get; init; }

        public LoyaltyActivatingAgent(Planeswalker walker, int loyaltyChange)
        {
            _walker = walker;
            _loyaltyChange = loyaltyChange;
        }

        public override Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
        {
            if (!_activated
                && ReferenceEquals(ctx.ActivePlayer, _walker.Controller)
                && ctx.CurrentPhase is { } phase && phase.IsMain()
                && ctx.Stack.Count == 0)
            {
                var ability = _walker.Abilities.OfType<LoyaltyAbility>()
                    .FirstOrDefault(a => a.LoyaltyChange == _loyaltyChange && a.CanActivate());
                if (ability != null)
                {
                    _activated = true;
                    BeforeActivation?.Invoke();
                    return Task.FromResult<PriorityAction>(
                        new PriorityAction.ActivateLoyaltyAbility(ability, System.Array.Empty<object>()));
                }
            }
            return Task.FromResult(PriorityAction.Pass);
        }

        public override Task<IReadOnlyList<object>> ChooseTargetsAsync(
            GameContext ctx, TargetRequest request, CancellationToken ct = default)
        {
            if (TargetChoice != null)
                return Task.FromResult<IReadOnlyList<object>>(new[] { TargetChoice });
            return Task.FromResult<IReadOnlyList<object>>(System.Array.Empty<object>());
        }
    }

    /// <summary>Agent that passes every priority window and declines all choices.</summary>
    private class PassAgent : IPlayerAgent
    {
        public virtual Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => Task.FromResult(PriorityAction.Pass);
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => Task.FromResult(MulliganDecision.Keep);
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ICard>>(System.Array.Empty<ICard>());
        public virtual Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<object>>(System.Array.Empty<object>());
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<Majik.Core.Cards.BotIntent>? modeIntents = null, CancellationToken ct = default) => Task.FromResult(0);
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => Task.FromResult(mine);
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
            => Task.FromResult(ManaPayment.Empty);
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
            => Task.FromResult(CombatPlan.None);
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
            => Task.FromResult(BlockPlan.None);
        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new ScryAction.ScryDecision(ToBottom: System.Array.Empty<ICard>(), TopOrder: peeked.ToList()));
        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new SurveilAction.SurveilDecision(ToGraveyard: System.Array.Empty<ICard>(), TopOrder: peeked.ToList()));
    }
}
