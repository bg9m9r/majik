using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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
/// Tests for Smash to Smithereens (Mirrodin, {1}{R}, Instant).
///
/// Covers:
///   - Card identity (Instant, {1}{R}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Structural shape — single "target artifact", Removal intent.
///   - Resolve: destroys target artifact (CR 701.7) → owner's graveyard.
///   - Resolve: deals 3 damage to that artifact's controller (CR 119)
///     captured from last-known information (CR 400.7a / 608.2c).
///   - Resolve illegal target (non-artifact / not on battlefield) is a
///     clean no-op — no destroy, no damage (CR 608.2b).
/// </summary>
public class SmashToSmithereensTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SmashToSmithereensTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SmashToSmithereens_IsInstant_AtCost1R()
    {
        var sts = SmashToSmithereensFactory.Create(_alice);

        sts.Name.Should().Be("Smash to Smithereens");
        sts.ManaCost.Should().Be("{1}{R}");
        sts.HasType(CardType.Instant).Should().BeTrue();
        sts.Owner.Should().BeSameAs(_alice);
        sts.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SmashToSmithereens()
    {
        var card = NamedCardFactory.Create("Smash to Smithereens", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Smash to Smithereens");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SmashToSmithereens_Helper_HasTargetArtifactRequest()
    {
        var def = SmashToSmithereensFactory.BuildSpellDefinition(o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("artifact");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DestroysArtifact_AndDeals3DamageToItsController()
    {
        // Bob controls Sol Ring — legal target.
        var solRing = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(solRing);
        solRing.SetZone(ZoneType.Battlefield);

        var bobStarting = _bob.LifeTotal;

        var def = SmashToSmithereensFactory.BuildSpellDefinition(o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { solRing } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        // CR 701.7 — destroyed → owner's graveyard.
        solRing.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(solRing);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(solRing);

        // CR 119 — 3 damage to that artifact's controller (Bob).
        _bob.LifeTotal.Should().Be(bobStarting - 3);
    }

    [Fact]
    public void Resolve_IllegalTarget_NonArtifact_IsCleanNoOp()
    {
        // A creature (not an artifact) is illegal at resolution (CR 608.2b).
        var goblin = new Creature("Goblin Guide", "{R}", power: 2, toughness: 2)
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield);

        var bobStarting = _bob.LifeTotal;

        var def = SmashToSmithereensFactory.BuildSpellDefinition(o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { goblin } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        // Creature untouched; no damage rider — illegal-target collapse.
        goblin.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(goblin);
        _bob.LifeTotal.Should().Be(bobStarting);
    }

    [Fact]
    public void Resolve_IllegalTarget_NotOnBattlefield_IsCleanNoOp()
    {
        // Artifact no longer on the battlefield (CR 608.2b — illegal target).
        var bauble = new Artifact("Mishra's Bauble", "{0}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        // Deliberately not placed on the battlefield.
        bauble.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bauble);

        var bobStarting = _bob.LifeTotal;

        var def = SmashToSmithereensFactory.BuildSpellDefinition(o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { bauble } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        bauble.Zone.Should().Be(ZoneType.Graveyard);
        _bob.LifeTotal.Should().Be(bobStarting);
    }

    [Fact]
    public async Task SmashToSmithereens_Cast_Resolves_DestroysAndPings()
    {
        // End-to-end cast through SpellCastFlow.
        var solRing = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(solRing);
        solRing.SetZone(ZoneType.Battlefield);

        var bobStarting = _bob.LifeTotal;

        var sts = SmashToSmithereensFactory.Create(_alice);
        sts.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sts);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { solRing });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.Main, _stack);

        var spell = await _flow.CastAsync(
            _alice, sts,
            SmashToSmithereensFactory.BuildSpellDefinition(t => t),
            agent, ctx);

        sts.Zone.Should().Be(ZoneType.Stack);
        spell.Resolve();

        solRing.Zone.Should().Be(ZoneType.Graveyard);
        _bob.LifeTotal.Should().Be(bobStarting - 3);
    }
}
