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
/// Tests for Shrapnel Blast (Mirrodin, {1}{R}, Instant).
///
/// Covers:
///   - Card identity (Instant, {1}{R}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Structural shape — single "any target", Removal intent, plus
///     mandatory sacrifice-an-artifact additional cost.
///   - Resolve with sacrifice paid → 5 damage to chosen target.
///   - The mandatory additional cost is exposed via
///     <see cref="SpellDefinition.AdditionalCosts"/>.
/// </summary>
public class ShrapnelBlastTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ShrapnelBlastTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ShrapnelBlast_IsInstant_AtCost1R()
    {
        var sb = ShrapnelBlastFactory.Create(_alice);

        sb.Name.Should().Be("Shrapnel Blast");
        sb.ManaCost.Should().Be("{1}{R}");
        sb.HasType(CardType.Instant).Should().BeTrue();
        sb.Owner.Should().BeSameAs(_alice);
        sb.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ShrapnelBlast()
    {
        var card = NamedCardFactory.Create("Shrapnel Blast", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Shrapnel Blast");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void ShrapnelBlast_Helper_HasSacrificeAdditionalCost_And_AnyTarget()
    {
        var def = ShrapnelBlastFactory.BuildSpellDefinition(_alice, o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("any target");
        tr.Intent.Should().Be(BotIntent.Removal);

        // CR 601.2f — additional cost is in the spell definition.
        def.AdditionalCostsOrEmpty.Should().HaveCount(1);
        def.AdditionalCostsOrEmpty[0].Should()
            .BeOfType<SacrificeAnArtifactAdditionalCost>();
    }

    [Fact]
    public void ShrapnelBlast_AdditionalCost_RequiresArtifact()
    {
        var cost = new SacrificeAnArtifactAdditionalCost();
        cost.CanPay(_alice).Should().BeFalse();

        SeedBattlefieldArtifact(_alice, "Mishra's Bauble");
        cost.CanPay(_alice).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Resolution — 5 damage
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ShrapnelBlast_Resolve_Deals5Damage()
    {
        var bobStarting = _bob.LifeTotal;

        // Cast requires a sacrificeable artifact (mandatory additional cost).
        SeedBattlefieldArtifact(_alice, "Ornithopter");

        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(bobStarting - 5);
    }

    [Fact]
    public void ShrapnelBlast_ResolveDirect_Deals5Damage()
    {
        // Direct exercise of the SpellDefinition's effect factory —
        // mirrors VoltageSurge / GalvanicBlast direct-resolve tests.
        var def = ShrapnelBlastFactory.BuildSpellDefinition(_alice, t => t);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(20 - 5);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task CastAndResolveTargeting(object target)
    {
        var sb = ShrapnelBlastFactory.Create(_alice);
        sb.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sb);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.Main, _stack);

        var spell = await _flow.CastAsync(
            _alice, sb,
            ShrapnelBlastFactory.BuildSpellDefinition(_alice, t => t),
            agent, ctx);

        sb.Zone.Should().Be(ZoneType.Stack);
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
