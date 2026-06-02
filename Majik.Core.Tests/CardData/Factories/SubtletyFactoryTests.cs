using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
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
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Subtlety (Modern Horizons 2). Exercise both cast
/// paths (normal + evoke) and assert the on-resolution triggers behave per
/// CR 702.74 (Evoke) and Subtlety's printed ETB bounce-and-look trigger.
/// </summary>
[Trait("Color", "U")]
public class SubtletyFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SubtletyFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Shape ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasCorrectShape()
    {
        var subtlety = SubtletyFactory.Create(_alice);

        subtlety.Name.Should().Be("Subtlety");
        subtlety.BasePower.Should().Be(3);
        subtlety.BaseToughness.Should().Be(3);
        subtlety.HasType(CardType.Creature).Should().BeTrue();
        subtlety.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        subtlety.HasSubtype(CardSubtype.Incarnation).Should().BeTrue();

        var keywordNames = subtlety.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywordNames.Should().Contain(new[] { "Flash", "Evoke" });

        // Two triggered abilities: ETB bounce + Evoke sacrifice.
        subtlety.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }
    // ── ETB bounce target shapes ──────────────────────────────────────────────

    [Fact]
    public async Task CastForEvoke_Creature_Bounced_AndSacrificeFires()
    {
        // Setup: Subtlety in Alice's hand, an Island for pitch fuel.
        var subtlety = SubtletyInHand(_alice);
        var pitchCard = new Creature("Spectral Sailor", "U", 1, 1) { Owner = _alice };
        pitchCard.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitchCard);

        // Bob has a target creature for the ETB bounce.
        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2)
        { Owner = _bob, Controller = _bob };
        grizzly.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);

        // Cast Subtlety via Evoke (pitch the blue spectral sailor; no mana paid).
        var evokeCost = new EvokeAlternativeCost(
            ManaCost.Zero, ManaColor.Blue, pitchCard);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, subtlety,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: evokeCost);

        _resolver.ResolveTop(_stack);

        subtlety.Zone.Should().Be(ZoneType.Battlefield);
        subtlety.EvokeWasPaid.Should().BeTrue();
        pitchCard.Zone.Should().Be(ZoneType.Exile);

        // Two triggers fired on the ETB CardMovedEvent: bounce + sac.
        _triggers.PendingCount.Should().Be(2);

        // Set Subtlety's ETB bounce-trigger to target Bob's bear, then resolve.
        var subtletyTriggers = subtlety.Abilities.OfType<TriggeredAbility>().ToList();
        var bounceTrigger = subtletyTriggers.First(t => t.TargetRequests.Count > 0);
        bounceTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }

        // ETB bounce fired: Bob's bear is in Bob's hand.
        grizzly.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(grizzly);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(grizzly);

        // Evoke sacrifice fired: Subtlety is now in Alice's graveyard.
        subtlety.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(subtlety);
    }

    [Fact]
    public async Task CastForNormalMana_Planeswalker_Bounced_NoSacrifice()
    {
        // Setup: Subtlety in hand, Bob has a planeswalker target.
        var subtlety = SubtletyInHand(_alice);

        var jace = new Planeswalker("Jace Beleren", "{1}{U}{U}", startingLoyalty: 3,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Jace })
        { Owner = _bob, Controller = _bob };
        jace.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(jace);

        // Cast Subtlety normally (no alternative cost).
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, subtlety,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        subtlety.Zone.Should().Be(ZoneType.Battlefield);
        subtlety.EvokeWasPaid.Should().BeFalse();

        // Only the ETB bounce trigger is pending — evoke-sac dropped at queue
        // time because EvokeWasPaid == false (CR 603.4).
        _triggers.PendingCount.Should().Be(1);

        var subtletyTriggers = subtlety.Abilities.OfType<TriggeredAbility>().ToList();
        var bounceTrigger = subtletyTriggers.First(t => t.TargetRequests.Count > 0);
        bounceTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { jace },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }

        // Planeswalker bounced to Bob's hand.
        jace.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(jace);

        // Subtlety is still on the battlefield (no sacrifice).
        subtlety.Zone.Should().Be(ZoneType.Battlefield);
    }

    // ── Look-at-top "may put on bottom" rider ────────────────────────────────

    [Fact]
    public async Task EtbLookRider_OpponentKeepsOnTop_LibraryOrderUnchanged()
    {
        var subtlety = SubtletyInHand(_alice);

        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2)
        { Owner = _bob, Controller = _bob };
        grizzly.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);

        // Seed Bob's library: top card is "TopCard", below it "Second".
        var topCard = new Creature("TopCard", "1G", 1, 1) { Owner = _bob };
        var secondCard = new Creature("Second", "1G", 1, 1) { Owner = _bob };
        topCard.SetZone(ZoneType.Library);
        secondCard.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(topCard);
        _bob.Zones.Library.AddCard(secondCard);

        // Register a scripted agent for Bob that declines the "may" (keep on top).
        // _bob is a fresh Player per test (xUnit creates new class instance per
        // test method), so its Guid does not collide with other tests' registry
        // entries — no Clear() needed.
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: Array.Empty<ICard>(),
            TopOrder: new ICard[] { topCard }));
        AgentRegistry.Set(_bob, bobAgent);

        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, subtlety,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            aliceAgent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        var bounceTrigger = subtlety.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count > 0);
        bounceTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }

        grizzly.Zone.Should().Be(ZoneType.Hand);

        // Library order unchanged: TopCard still on top.
        var lib = _bob.Zones.Library.GetCards().ToList();
        lib[0].Should().BeSameAs(topCard);
        lib[1].Should().BeSameAs(secondCard);
    }

    [Fact]
    public async Task EtbLookRider_OpponentBottomsTopCard_LibraryReorders()
    {
        var subtlety = SubtletyInHand(_alice);

        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2)
        { Owner = _bob, Controller = _bob };
        grizzly.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);

        // Seed Bob's library: top "TopCard", below it "Second".
        var topCard = new Creature("TopCard", "1G", 1, 1) { Owner = _bob };
        var secondCard = new Creature("Second", "1G", 1, 1) { Owner = _bob };
        topCard.SetZone(ZoneType.Library);
        secondCard.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(topCard);
        _bob.Zones.Library.AddCard(secondCard);

        // Bob accepts the "may" — put the peeked card on the bottom.
        // No Clear() needed: per-test fresh _bob Guid.
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: new ICard[] { topCard },
            TopOrder: Array.Empty<ICard>()));
        AgentRegistry.Set(_bob, bobAgent);

        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, subtlety,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            aliceAgent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        var bounceTrigger = subtlety.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count > 0);
        bounceTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }

        // Library reordered: peeked card now at the bottom; "Second" is the new top.
        var lib = _bob.Zones.Library.GetCards().ToList();
        lib[0].Should().BeSameAs(secondCard);
        lib[lib.Count - 1].Should().BeSameAs(topCard);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Creature SubtletyInHand(Player owner)
    {
        var s = SubtletyFactory.Create(owner);
        s.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(s);
        return s;
    }
}
