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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Solitude (Modern Horizons 2). Exercise both cast
/// paths (normal + evoke) and assert the on-resolution triggers behave per
/// CR 702.74 (Evoke) and Solitude's printed ETB exile trigger.
/// </summary>
public class SolitudeFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SolitudeFactoryTests()
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
        var solitude = SolitudeFactory.Create(_alice);

        solitude.Name.Should().Be("Solitude");
        solitude.BasePower.Should().Be(3);
        solitude.BaseToughness.Should().Be(2);
        solitude.HasType(CardType.Creature).Should().BeTrue();
        solitude.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        solitude.HasSubtype(CardSubtype.Incarnation).Should().BeTrue();

        var keywordNames = solitude.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywordNames.Should().Contain(new[] { "Flash", "Lifelink", "Evoke" });

        // Two triggered abilities: ETB exile + Evoke sacrifice.
        solitude.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    // ── Cast paths ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CastForEvoke_SacrificeTriggerFires_AndCreatureGoesToGraveyard()
    {
        // Setup: Solitude in Alice's hand, a Plains for pitch fuel.
        var solitude = SolitudeInHand(_alice);
        var pitchCard = new Creature("Savannah Lions", "W", 2, 1) { Owner = _alice };
        pitchCard.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitchCard);

        // Bob has a target creature for the ETB exile clause.
        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2)
        { Owner = _bob, Controller = _bob };
        grizzly.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);

        // Cast Solitude via Evoke (pitch the white Lions; no mana paid).
        var evokeCost = new EvokeAlternativeCost(
            ManaCost.Zero, ManaColor.White, pitchCard);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, solitude,
            // Vanilla spell-definition shell — the gameplay effects live on
            // Solitude's printed triggered abilities, not the spell itself.
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: evokeCost);

        // Spell resolves: alt-cost cleanup flips EvokeWasPaid and exiles pitch.
        _resolver.ResolveTop(_stack);

        solitude.Zone.Should().Be(ZoneType.Battlefield);
        solitude.EvokeWasPaid.Should().BeTrue();
        pitchCard.Zone.Should().Be(ZoneType.Exile);

        // Two triggers fired on the ETB CardMovedEvent: exile target + sac.
        _triggers.PendingCount.Should().Be(2);

        // Set Solitude's ETB exile-trigger to target Bob's bear, then resolve.
        var solitudeTriggers = solitude.Abilities.OfType<TriggeredAbility>().ToList();
        var exileTrigger = solitudeTriggers.First(t => t.TargetRequests.Count > 0);
        exileTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        // Resolve both triggers (order: APNAP — both Alice's, so we resolve top-down).
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }

        // ETB exile fired: Bob's bear is in exile.
        grizzly.Zone.Should().Be(ZoneType.Exile);
        // Bob (the exiled creature's controller) gained life equal to bear's power (2).
        _bob.LifeTotal.Should().Be(22);

        // Evoke sacrifice fired: Solitude is now in Alice's graveyard.
        solitude.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(solitude);
    }

    [Fact]
    public async Task CastForNormalMana_OnlyExileTriggerFires_NoSacrifice()
    {
        // Setup: Solitude in hand, Bob has a target.
        var solitude = SolitudeInHand(_alice);

        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2)
        { Owner = _bob, Controller = _bob };
        grizzly.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);

        // Cast Solitude normally (no alternative cost). Mana payment is faked
        // (ScriptedAgent returns ManaPayment.Empty; we don't model {3}{W}{W}
        // resolution in this test).
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, solitude,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: null);

        // Resolve the spell — moves Solitude to the battlefield. EvokeWasPaid
        // must remain false (no alt-cost was used).
        _resolver.ResolveTop(_stack);

        solitude.Zone.Should().Be(ZoneType.Battlefield);
        solitude.EvokeWasPaid.Should().BeFalse();

        // Only the ETB exile trigger is pending — the evoke-sacrifice trigger
        // had its intervening-if check fail (EvokeWasPaid == false) and was
        // dropped at queue-time (CR 603.4).
        _triggers.PendingCount.Should().Be(1);

        var solitudeTriggers = solitude.Abilities.OfType<TriggeredAbility>().ToList();
        var exileTrigger = solitudeTriggers.First(t => t.TargetRequests.Count > 0);
        exileTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }

        // ETB exile fired.
        grizzly.Zone.Should().Be(ZoneType.Exile);
        _bob.LifeTotal.Should().Be(22);

        // Solitude is still on the battlefield (no sacrifice).
        solitude.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public async Task CastForEvoke_NoTargetChosen_SacrificeStillFires()
    {
        // "Exile up to one other target creature" — zero targets is legal.
        // The exile effect should no-op, but the evoke sacrifice still fires.
        var solitude = SolitudeInHand(_alice);
        var pitchCard = new Creature("Savannah Lions", "W", 2, 1) { Owner = _alice };
        pitchCard.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitchCard);

        var evokeCost = new EvokeAlternativeCost(
            ManaCost.Zero, ManaColor.White, pitchCard);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, solitude,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: evokeCost);

        _resolver.ResolveTop(_stack);

        solitude.EvokeWasPaid.Should().BeTrue();
        _triggers.PendingCount.Should().Be(2);

        // No target chosen on the exile trigger (zero-target list).
        var solitudeTriggers = solitude.Abilities.OfType<TriggeredAbility>().ToList();
        var exileTrigger = solitudeTriggers.First(t => t.TargetRequests.Count > 0);
        exileTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }

        // Exile no-op'd (no target), no lifegain.
        _bob.LifeTotal.Should().Be(20);
        // Sacrifice fired.
        solitude.Zone.Should().Be(ZoneType.Graveyard);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Creature SolitudeInHand(Player owner)
    {
        var s = SolitudeFactory.Create(owner);
        s.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(s);
        return s;
    }
}
