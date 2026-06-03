using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Everflowing Chalice (Worldwake, {0}, Artifact) — the canonical
/// Multikicker (CR 702.32) scaling payoff.
///
///   "Multikicker {2} (You may pay an additional {2} any number of times as
///    you cast this spell.)
///    This artifact enters with a charge counter on it for each time it was
///    kicked.
///    {T}: Add {C} for each charge counter on this artifact."
///
/// Coverage:
///   - Identity (Artifact, {0}) + NamedCardFactory dispatch.
///   - CR 614.1d replacement: enters WITH N charge counters where N = times
///     kicked (no ETB trigger / separate event — the counters are on the
///     artifact the instant it lands).
///   - {T}: Add {C} per charge counter — taps for {C}{C} with two counters,
///     for nothing with zero.
///   - End-to-end cast pipeline: multikick ×2 → enters with 2 charge counters
///     → taps for {C}{C}; multikick ×0 → 0 counters, taps for nothing;
///     mana-bounded ×3 fails.
///   - CR 400.7 — the kicker sentinel is cleared after resolution so a blink /
///     token copy enters with zero.
/// </summary>
public class EverflowingChaliceTests
{
    private readonly EventBus _bus = new();
    private readonly ReplacementBus _replacements = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public EverflowingChaliceTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus, _replacements);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void EverflowingChalice_IsArtifact_ZeroCost()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice);

        chalice.Name.Should().Be("Everflowing Chalice");
        chalice.HasType(CardType.Artifact).Should().BeTrue();
        chalice.ManaCost.Should().Be("{0}");
        chalice.Owner.Should().BeSameAs(_alice);
        chalice.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_EverflowingChalice()
    {
        var card = NamedCardFactory.Create("Everflowing Chalice", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Everflowing Chalice");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{0}");
    }

    [Fact]
    public void EverflowingChalice_HasOneManaAbility_AndNoEtbTrigger()
    {
        // CR 614.1d — the "enters with charge counters" clause is a
        // replacement effect, not a triggered ability, so the chalice carries
        // exactly one (mana) ability and no ETB trigger.
        var chalice = EverflowingChaliceFactory.Create(_alice);

        chalice.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        chalice.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // CR 614.1d — enters WITH a charge counter for each time kicked
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_KickedTwice_EntersWithTwoChargeCounters()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice, _replacements);
        chalice.SetZone(ZoneType.Stack);
        _alice.Zones.Stack.AddCard(chalice);
        // Simulate a multikicker ×2 cast having stamped the count.
        chalice.SetTimesKicked(2);

        _zones.MoveCard(chalice, ZoneType.Stack, ZoneType.Battlefield, controller: _alice);

        chalice.Counters.Count(CounterType.Charge).Should().Be(2,
            "the chalice enters WITH a charge counter for each time it was kicked (CR 614.1d / CR 702.32c)");
    }

    [Fact]
    public void Etb_NotKicked_EntersWithZeroChargeCounters()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice, _replacements);
        chalice.SetZone(ZoneType.Stack);
        _alice.Zones.Stack.AddCard(chalice);
        // TimesKicked defaults to 0 (cast without paying the multikicker).

        _zones.MoveCard(chalice, ZoneType.Stack, ZoneType.Battlefield, controller: _alice);

        chalice.Counters.Count(CounterType.Charge).Should().Be(0,
            "a multikicker paid zero times = zero charge counters");
    }

    [Fact]
    public void Etb_KickedTwice_NoEtbTriggerWindow_CountersPresentOnLanding()
    {
        // Regression guard for the correctness upgrade: with the replacement
        // shape there is no window where the chalice is on the battlefield with
        // the wrong count. We assert the counters are present the instant the
        // CardMovedEvent fires (i.e. during ETB observation), not afterwards.
        var chalice = EverflowingChaliceFactory.Create(_alice, _replacements);
        chalice.SetZone(ZoneType.Stack);
        _alice.Zones.Stack.AddCard(chalice);
        chalice.SetTimesKicked(3);

        int observedAtEtb = -1;
        _bus.Subscribe<CardMovedEvent>(e =>
        {
            if (ReferenceEquals(e.Card, chalice) && e.ToZone == ZoneType.Battlefield)
            {
                observedAtEtb = chalice.Counters.Count(CounterType.Charge);
            }
        });

        _zones.MoveCard(chalice, ZoneType.Stack, ZoneType.Battlefield, controller: _alice);

        observedAtEtb.Should().Be(3,
            "the charge counters are on the chalice the moment it enters — CR 614.1d replacement, not an after-the-fact trigger");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C} for each charge counter
    // -----------------------------------------------------------------------

    [Fact]
    public void Tap_WithTwoChargeCounters_ProducesTwoColorless()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(chalice);
        chalice.SetZone(ZoneType.Battlefield);
        chalice.Counters.Add(CounterType.Charge, 2);

        var mana = chalice.Abilities.OfType<ManaAbility>().Single();
        mana.CanActivate().Should().BeTrue();

        var produced = mana.Activate();

        // {C}{C} folds into the generic bucket (CR 107.4c).
        produced.TotalValue.Should().Be(2);
        chalice.IsTapped.Should().BeTrue("the {T} activation cost taps the chalice");
    }

    [Fact]
    public void Tap_WithZeroChargeCounters_ProducesNothing()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(chalice);
        chalice.SetZone(ZoneType.Battlefield);
        // No charge counters.

        var mana = chalice.Abilities.OfType<ManaAbility>().Single();
        var produced = mana.Activate();

        produced.TotalValue.Should().Be(0,
            "with no charge counters the chalice taps for nothing");
    }

    // -----------------------------------------------------------------------
    // End-to-end cast pipeline — Multikicker through SpellCastFlow + resolver
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Cast_MultikickedTwice_StampsTimesKicked2_AndDrainsFourMana()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice, _replacements);
        chalice.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(chalice);

        // Two kicks of {2} = {4} in the pool.
        _alice.AddManaToPool(ManaCost.Parse("{4}"));

        var (agent, ctx) = ScriptedCast();

        var additional = new[] { EverflowingChaliceFactory.BuildAdditionalCost(chalice, times: 2) };

        var spell = await _flow.CastAsync(
            _alice, chalice, ChaliceSpellDef(), agent, ctx,
            additionalCosts: additional);

        // CR 702.32c — the kick count is stamped on the card + spell.
        chalice.TimesKicked.Should().Be(2);
        spell.TimesKicked.Should().Be(2);
        spell.WasKicked.Should().BeTrue();
        // Multikicker {2} ×2 drained the pool.
        _alice.ManaPool.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task Cast_MultikickedTwice_EntersWithTwoChargeCounters_TapsForTwoColorless()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice, _replacements);
        chalice.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(chalice);
        _alice.AddManaToPool(ManaCost.Parse("{4}"));

        var (agent, ctx) = ScriptedCast();

        await _flow.CastAsync(
            _alice, chalice, ChaliceSpellDef(), agent, ctx,
            additionalCosts: new[] { EverflowingChaliceFactory.BuildAdditionalCost(chalice, times: 2) });

        // Resolve the chalice off the stack through the real resolver — the
        // CR 614.1d replacement fires during the Stack → Battlefield move.
        await _resolver.ResolveTopAsync(_stack, agentLookup: _ => agent, game: ctx);

        chalice.Zone.Should().Be(ZoneType.Battlefield);
        chalice.Counters.Count(CounterType.Charge).Should().Be(2,
            "enters WITH two charge counters via the CR 614.1d replacement");

        var mana = chalice.Abilities.OfType<ManaAbility>().Single();
        mana.Activate().TotalValue.Should().Be(2);
    }

    [Fact]
    public async Task Cast_NotKicked_EntersWithZeroChargeCounters_TapsForNothing()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice, _replacements);
        chalice.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(chalice);
        // No mana — but multikicker ×0 needs none.

        var (agent, ctx) = ScriptedCast();

        var spell = await _flow.CastAsync(
            _alice, chalice, ChaliceSpellDef(), agent, ctx,
            additionalCosts: new[] { EverflowingChaliceFactory.BuildAdditionalCost(chalice, times: 0) });

        spell.WasKicked.Should().BeFalse();

        await _resolver.ResolveTopAsync(_stack, agentLookup: _ => agent, game: ctx);

        chalice.Zone.Should().Be(ZoneType.Battlefield);
        chalice.Counters.Count(CounterType.Charge).Should().Be(0);
        chalice.Abilities.OfType<ManaAbility>().Single().Activate().TotalValue.Should().Be(0);
    }

    [Fact]
    public async Task Cast_MultikickedThrice_RequiresSixMana_FailsWhenShort()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice, _replacements);
        chalice.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(chalice);

        // Three kicks need {6}; only {4} available.
        _alice.AddManaToPool(ManaCost.Parse("{4}"));

        var (agent, ctx) = ScriptedCast();

        Func<Task> act = async () => await _flow.CastAsync(
            _alice, chalice, ChaliceSpellDef(), agent, ctx,
            additionalCosts: new[] { EverflowingChaliceFactory.BuildAdditionalCost(chalice, times: 3) });

        await act.Should().ThrowAsync<InvalidOperationException>(
            "CR 601.2g — the multikicker is unaffordable so the cast is illegal");

        // Pool untouched (no partial payment).
        _alice.ManaPool.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task Cast_MultikickedTwice_ClearsKickCountAfterResolution_SoBlinkEntersWithZero()
    {
        var chalice = EverflowingChaliceFactory.Create(_alice, _replacements);
        chalice.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(chalice);
        _alice.AddManaToPool(ManaCost.Parse("{4}"));

        var (agent, ctx) = ScriptedCast();

        await _flow.CastAsync(
            _alice, chalice, ChaliceSpellDef(), agent, ctx,
            additionalCosts: new[] { EverflowingChaliceFactory.BuildAdditionalCost(chalice, times: 2) });

        await _resolver.ResolveTopAsync(_stack, agentLookup: _ => agent, game: ctx);

        // CR 400.7 — the cast-time tally is consumed so a later blink / token
        // copy of this object enters with zero charge counters.
        chalice.TimesKicked.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private (ScriptedAgent, GameContext) ScriptedCast()
    {
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, StepStateType.PreCombatMain, _stack);
        return (agent, ctx);
    }

    /// <summary>Minimal permanent SpellDefinition — Everflowing Chalice's
    /// printed body is empty (its behaviour is the ETB replacement + mana
    /// ability on the permanent), so the EffectFactory yields no effects.</summary>
    private static SpellDefinition ChaliceSpellDef() =>
        new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => Array.Empty<IEffect>());
}
