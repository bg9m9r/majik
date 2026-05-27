using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Foundation Breaker (Modern Horizons 2, {2}{G}, Creature —
/// Elemental 3/2).
///
/// Oracle:
///   "When this creature enters, you may destroy target artifact or
///    enchantment.
///    Evoke {1}{G}"
///
/// Covers:
///   - Card identity (name, mana cost, P/T, Elemental subtype, owner /
///     controller) + NamedCardFactory dispatch.
///   - Ability shape: Evoke marker, evoke-sacrifice trigger, ETB
///     "destroy target artifact or enchantment" trigger with MinTargets=0
///     (the printed "you may").
///   - Normal cast → ETB destroys target artifact + Foundation Breaker
///     stays on battlefield (no evoke sacrifice).
///   - Normal cast → ETB destroys target enchantment.
///   - "May" decline (no target chosen) → ETB no-op; Foundation Breaker
///     remains on battlefield.
///   - Evoke cast → ETB destroys target + evoke-sac sends Foundation
///     Breaker to its owner's graveyard.
///   - Illegal target on resolution (creature, not A/E) → no destroy.
/// </summary>
public class FoundationBreakerTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public FoundationBreakerTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Shape ────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasCorrectShape()
    {
        var fb = FoundationBreakerFactory.Create(_alice);

        fb.Name.Should().Be("Foundation Breaker");
        fb.ManaCost.Should().Be("{2}{G}");
        fb.BasePower.Should().Be(3);
        fb.BaseToughness.Should().Be(2);
        fb.HasType(CardType.Creature).Should().BeTrue();
        fb.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        fb.Owner.Should().BeSameAs(_alice);
        fb.Controller.Should().BeSameAs(_alice);

        // Evoke keyword marker.
        var keywords = fb.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Evoke");

        // Two triggered abilities: ETB destroy + evoke sacrifice.
        fb.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void NamedCardFactory_DispatchesFoundationBreaker()
    {
        var card = NamedCardFactory.Create("Foundation Breaker", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Foundation Breaker");
        var creature = (Creature)card;
        creature.BasePower.Should().Be(3);
        creature.BaseToughness.Should().Be(2);
        creature.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
    }

    [Fact]
    public void EtbTrigger_HasOptionalSingleTarget_ArtifactOrEnchantment()
    {
        var fb = FoundationBreakerFactory.Create(_alice);

        // The destroy trigger is the one with target requests; the evoke
        // sacrifice trigger has none.
        var destroyTrigger = fb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);

        destroyTrigger.TargetRequests.Should().HaveCount(1);
        destroyTrigger.TargetRequests[0].MinTargets.Should().Be(0,
            "the printed text is 'you may' (decline by picking 0 targets)");
        destroyTrigger.TargetRequests[0].MaxTargets.Should().Be(1);
        destroyTrigger.TargetRequests[0].Description.Should()
            .Contain("artifact").And.Contain("enchantment");
    }

    // ── ETB destroy ──────────────────────────────────────────────────────

    [Fact]
    public async Task NormalCast_TargetArtifact_DestroysIt_AndFoundationBreakerStays()
    {
        var fb = FoundationBreakerInHand(_alice);

        var trinket = new Artifact("Bob's Trinket", "{2}");
        trinket.SetOwner(_bob);
        trinket.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(trinket);
        trinket.SetZone(ZoneType.Battlefield);

        await CastNormalAndResolveAndTarget(fb, trinket);

        // Trinket destroyed.
        trinket.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(trinket);

        // Foundation Breaker stays on the battlefield — no evoke sacrifice
        // because the evoke cost wasn't paid.
        fb.Zone.Should().Be(ZoneType.Battlefield);
        fb.EvokeWasPaid.Should().BeFalse();
    }

    [Fact]
    public async Task NormalCast_TargetEnchantment_DestroysIt()
    {
        var fb = FoundationBreakerInHand(_alice);

        var aura = new Enchantment("Bob's Aura", "{1}{W}");
        aura.SetOwner(_bob);
        aura.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        await CastNormalAndResolveAndTarget(fb, aura);

        aura.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(aura);
        fb.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public async Task NormalCast_DeclineByPickingZeroTargets_NoDestroy_FbStays()
    {
        // Even when a legal target exists, the controller can decline by
        // picking 0 targets (MinTargets = 0 — the printed "you may").
        var fb = FoundationBreakerInHand(_alice);

        var trinket = new Artifact("Bob's Trinket", "{2}");
        trinket.SetOwner(_bob);
        trinket.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(trinket);
        trinket.SetZone(ZoneType.Battlefield);

        await CastNormalAndResolveWithZeroTargets(fb);

        // Trinket untouched.
        trinket.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();

        // Foundation Breaker stays.
        fb.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public async Task NormalCast_IllegalTargetAtResolution_Creature_NoDestroy()
    {
        // ChosenTargets is set to a Creature (not A/E). CR 608.2b: illegal
        // target at resolve → no destroy. Foundation Breaker still stays.
        var fb = FoundationBreakerInHand(_alice);

        var bear = new Creature("Bob's Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        await CastNormalAndResolveAndTarget(fb, bear);

        bear.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        fb.Zone.Should().Be(ZoneType.Battlefield);
    }

    // ── Evoke path ───────────────────────────────────────────────────────

    [Fact]
    public async Task EvokeCast_DestroysTarget_ThenSacrificesFoundationBreaker()
    {
        var fb = FoundationBreakerInHand(_alice);

        var trinket = new Artifact("Bob's Trinket", "{2}");
        trinket.SetOwner(_bob);
        trinket.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(trinket);
        trinket.SetZone(ZoneType.Battlefield);

        // Evoke {1}{G} — pure-mana evoke (no pitch component).
        var evokeCost = new EvokeAlternativeCost(ManaCost.Parse("{1}{G}"));

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, fb,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: evokeCost);

        _resolver.ResolveTop(_stack);

        fb.Zone.Should().Be(ZoneType.Battlefield);
        fb.EvokeWasPaid.Should().BeTrue();

        // Both triggers fired on the ETB CardMovedEvent.
        _triggers.PendingCount.Should().Be(2);

        // Point the destroy trigger at Bob's trinket.
        var destroyTrigger = fb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        destroyTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { trinket },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }

        // Trinket destroyed AND Foundation Breaker sacrificed.
        trinket.Zone.Should().Be(ZoneType.Graveyard);
        fb.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(fb);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private Creature FoundationBreakerInHand(Player owner)
    {
        var fb = FoundationBreakerFactory.Create(owner);
        fb.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(fb);
        return fb;
    }

    private async Task CastNormalAndResolveAndTarget(Creature fb, Permanent target)
    {
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, fb,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // Only the destroy trigger should be pending — evoke sac's
        // intervening-if (EvokeWasPaid == false) drops it.
        var destroyTrigger = fb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        destroyTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }
    }

    private async Task CastNormalAndResolveWithZeroTargets(Creature fb)
    {
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, fb,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // Decline — leave ChosenTargets empty / set explicitly to no picks.
        var destroyTrigger = fb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        destroyTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }
    }
}
