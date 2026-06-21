using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
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

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 601.2c → 601.2h — chooser-bearing NON-MANA additional costs on a SPELL
/// (a typed sacrifice such as "sacrifice an artifact or creature", or a
/// variable discard such as "discard X cards") prompt the caster's agent for
/// WHICH permanent / cards to use, and that prompt must fire at the CR 601.2h
/// payment point — AFTER target choice (CR 601.2c) — not at the early CanPay
/// pre-check (CR 601.2f). This pins the cast-pipeline ordering for the
/// chooser-bearing additional costs (the residual leg of the
/// non-mana-additional-cost cast-time ordering audit): a targeting failure must
/// rewind (CR 731.1) without ever having prompted the chooser, and a legal cast
/// must honour the caster's pick instead of the legacy first-eligible auto-pick.
/// </summary>
public class SpellCastFlowAdditionalCostChooserTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpellCastFlowAdditionalCostChooserTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _flow = new SpellCastFlow(_stack, new ZoneService(_bus), _bus);
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    [Fact]
    public async Task SacrificeChooser_PromptsCaster_HonoursPick_NotFirstEligible()
    {
        // Two eligible sacrifices; the legacy auto-pick would take the FIRST
        // (bear1). The agent prompt must let the caster choose bear2 instead.
        var bear1 = new Creature("Bear One", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var bear2 = new Creature("Bear Two", "1G", 3, 3)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(bear1);
        _alice.Zones.Battlefield.AddCard(bear2);

        var spell = new Instant("Deadly Dispute", "1B") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(spell);

        var sacCost = new SacrificeAnArtifactOrCreatureAdditionalCost(_bus);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        // Sacrifice chooser: pick the SECOND eligible permanent (bear2).
        agent.QueueChoice(candidates => candidates.Where(c => ReferenceEquals(c, bear2)).ToList());

        await _flow.CastAsync(
            _alice, spell,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, NewContext(),
            additionalCosts: new IAdditionalCost[] { sacCost });

        // CR 601.2h — the caster's choice (bear2) was sacrificed, not the
        // first-eligible default (bear1).
        bear2.Zone.Should().Be(ZoneType.Graveyard);
        bear1.Zone.Should().Be(ZoneType.Battlefield);
        sacCost.Sacrificed.Should().BeSameAs(bear2);
        _stack.Count.Should().Be(1);
    }

    [Fact]
    public async Task SacrificeChooser_NotPromptedWhenTargetingFails_CR731Ordering()
    {
        // The chooser prompt must fire AT 601.2h (after targets). A targeting
        // failure (CR 601.2c) must throw BEFORE the chooser is ever consulted —
        // so the spell that pairs a chooser sacrifice with an unsatisfiable
        // target never asks "which permanent do you sacrifice?".
        var bear1 = new Creature("Bear One", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var bear2 = new Creature("Bear Two", "1G", 3, 3)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(bear1);
        _alice.Zones.Battlefield.AddCard(bear2);

        var spell = new Instant("Targeted Sac Spell", "1B") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(spell);

        var sacCost = new SacrificeAnArtifactOrCreatureAdditionalCost(_bus);

        var chooserConsulted = false;
        var agent = new ScriptedAgent();
        agent.QueueTargets(System.Array.Empty<object>()); // no legal target → throws at 601.2c
        agent.QueueMana(ManaPayment.Empty);
        agent.QueueChoice(candidates => { chooserConsulted = true; return candidates; });

        var def = new SpellDefinition(
            Modes: System.Array.Empty<string>(), HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target creature", 1, 1, System.Array.Empty<object>()) },
            EffectFactory: _ => System.Array.Empty<IEffect>());

        var act = async () => await _flow.CastAsync(
            _alice, spell, def, agent, NewContext(),
            additionalCosts: new IAdditionalCost[] { sacCost });

        await act.Should().ThrowAsync<System.InvalidOperationException>();

        chooserConsulted.Should().BeFalse(
            "CR 601.2c precedes CR 601.2h — the sacrifice chooser must not be " +
            "prompted before legal targets exist");
        bear1.Zone.Should().Be(ZoneType.Battlefield);
        bear2.Zone.Should().Be(ZoneType.Battlefield);
        sacCost.Sacrificed.Should().BeNull();
        spell.Zone.Should().Be(ZoneType.Hand);
        _stack.Count.Should().Be(0);
    }

    [Fact]
    public async Task DiscardXChooser_PromptsCaster_DiscardsChosenSubset_NotWholeHand()
    {
        // Nahiri's Wrath analogue: "discard X cards" where X is caster-chosen.
        // The legacy default discards the WHOLE hand; the prompt must let the
        // caster nominate a specific subset.
        var keep = new Instant("Keep Me", "1") { Owner = _alice, Zone = ZoneType.Hand };
        var discardA = new Instant("Discard A", "1") { Owner = _alice, Zone = ZoneType.Hand };
        var discardB = new Instant("Discard B", "1") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(keep);
        _alice.Zones.Hand.AddCard(discardA);
        _alice.Zones.Hand.AddCard(discardB);

        var spell = new Sorcery("Wrath Spell", "2R") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(spell);

        var discardCost = new DiscardXCardsAdditionalCost();

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        // Discard chooser: nominate exactly { discardA, discardB }, keep `keep`.
        agent.QueueChoice(candidates =>
            candidates.Where(c => ReferenceEquals(c, discardA) || ReferenceEquals(c, discardB)).ToList());

        await _flow.CastAsync(
            _alice, spell,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, NewContext(),
            additionalCosts: new IAdditionalCost[] { discardCost });

        discardA.Zone.Should().Be(ZoneType.Graveyard);
        discardB.Zone.Should().Be(ZoneType.Graveyard);
        keep.Zone.Should().Be(ZoneType.Hand, "the caster kept this card out of the discard");
        discardCost.Discarded.Should().BeEquivalentTo(new[] { discardA, discardB });
        _stack.Count.Should().Be(1);
    }
}
