using FluentAssertions;
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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Voltage Surge (Modern Horizons 3, {R}, Instant).
///
/// Covers:
///   - Card identity (Instant, {R}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve default-no-sac → 2 damage + 2 energy.
///   - Resolve sacrificed-an-artifact → 4 damage + 2 energy.
///   - Energy gain is unconditional (fires whether or not sac was paid).
///   - SacrificeAnArtifactAdditionalCost.Sacrificed sentinel is the
///     resolve-time read.
/// </summary>
public class VoltageSurgeTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public VoltageSurgeTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void VoltageSurge_IsInstant_AtCostR()
    {
        var vs = VoltageSurgeFactory.Create(_alice);

        vs.Name.Should().Be("Voltage Surge");
        vs.ManaCost.Should().Be("{R}");
        vs.HasType(CardType.Instant).Should().BeTrue();
        vs.Owner.Should().BeSameAs(_alice);
        vs.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_VoltageSurge()
    {
        var card = NamedCardFactory.Create("Voltage Surge", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Voltage Surge");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — sacrifice gate
    // -----------------------------------------------------------------------

    [Fact]
    public async Task VoltageSurge_NoSacrifice_Deals2Damage_And_Gains2Energy()
    {
        var bobStarting = _bob.LifeTotal;
        _alice.EnergyCounters.Should().Be(0);

        await CastAndResolveTargeting(_bob, withSacrifice: false);

        _bob.LifeTotal.Should().Be(bobStarting - 2);
        _alice.EnergyCounters.Should().Be(2);
    }

    [Fact]
    public async Task VoltageSurge_WithSacrifice_Deals4Damage_And_Gains2Energy()
    {
        var bobStarting = _bob.LifeTotal;
        _alice.EnergyCounters.Should().Be(0);

        // Give Alice an artifact to sacrifice.
        SeedBattlefieldArtifact(_alice, "Mishra's Bauble");

        await CastAndResolveTargeting(_bob, withSacrifice: true);

        // CR 702.x-style sacrificed-rider — 4 damage replaces base 2.
        _bob.LifeTotal.Should().Be(bobStarting - 4);
        // Energy gain unconditional.
        _alice.EnergyCounters.Should().Be(2);
    }

    [Fact]
    public void VoltageSurge_EnergyGain_IsUnconditional()
    {
        // Direct exercise: invoke the spell-definition's effect factory
        // without the cost-payment path. Whether or not the cost is
        // supplied, the energy ledger should advance by 2.
        var cost = VoltageSurgeFactory.BuildAdditionalCost();
        var def = VoltageSurgeFactory.BuildSpellDefinition(
            _alice, t => t, sacrificeCost: cost);

        // Simulate resolution without the sacrifice paid.
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));
        foreach (var e in effects) e.Execute();

        // Base damage (2) — cost.Sacrificed is null.
        _bob.LifeTotal.Should().Be(20 - 2);
        // Energy gain fires regardless.
        _alice.EnergyCounters.Should().Be(2);
        VoltageSurgeFactory.ReadSacrificeOutcome(cost).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Cost legality
    // -----------------------------------------------------------------------

    [Fact]
    public void SacrificeAdditionalCost_CanPay_RequiresArtifact()
    {
        var cost = VoltageSurgeFactory.BuildAdditionalCost();
        cost.CanPay(_alice).Should().BeFalse();

        SeedBattlefieldArtifact(_alice, "Mishra's Bauble");
        cost.CanPay(_alice).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task CastAndResolveTargeting(object target, bool withSacrifice)
    {
        var vs = VoltageSurgeFactory.Create(_alice);
        vs.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(vs);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        SacrificeAnArtifactAdditionalCost? cost = null;
        IReadOnlyList<IAdditionalCost>? additional = null;
        if (withSacrifice)
        {
            cost = VoltageSurgeFactory.BuildAdditionalCost();
            additional = new IAdditionalCost[] { cost };
        }

        var spell = await _flow.CastAsync(
            _alice, vs,
            VoltageSurgeFactory.BuildSpellDefinition(_alice, t => t, sacrificeCost: cost),
            agent, ctx,
            additionalCosts: additional);

        vs.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();
    }

    private static void SeedBattlefieldArtifact(Player owner, string name)
    {
        var art = new Artifact(name, string.Empty);
        art.SetOwner(owner);
        art.SetController(owner);
        art.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(art);
    }
}
