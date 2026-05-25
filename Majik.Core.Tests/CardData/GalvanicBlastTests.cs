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
/// Tests for Galvanic Blast (Mirrodin Besieged, {R}, Instant).
///
/// Covers:
///   - Card identity (Instant, {R}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve without Metalcraft → 2 damage.
///   - Resolve with Metalcraft (>= 3 artifacts on the controller's
///     battlefield) → 4 damage.
///   - Edge case: exactly 2 artifacts → base damage (gate is strict
///     CR 702.95 ">= 3").
///   - <see cref="GalvanicBlastFactory.ControlsThreeOrMoreArtifacts"/>
///     helper exposed for state-read inspection.
///   - Structural shape — single "any target" request with Removal intent.
/// </summary>
public class GalvanicBlastTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public GalvanicBlastTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GalvanicBlast_IsInstant_AtCostR()
    {
        var gb = GalvanicBlastFactory.Create(_alice);

        gb.Name.Should().Be("Galvanic Blast");
        gb.ManaCost.Should().Be("{R}");
        gb.HasType(CardType.Instant).Should().BeTrue();
        gb.Owner.Should().BeSameAs(_alice);
        gb.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GalvanicBlast()
    {
        var card = NamedCardFactory.Create("Galvanic Blast", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Galvanic Blast");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void GalvanicBlast_Helper_HasSingleAnyTargetRequest()
    {
        var def = GalvanicBlastFactory.BuildSpellDefinition(_alice, o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("any target");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Metalcraft gate helper
    // -----------------------------------------------------------------------

    [Fact]
    public void Metalcraft_RequiresThreeArtifacts()
    {
        GalvanicBlastFactory.ControlsThreeOrMoreArtifacts(_alice).Should().BeFalse();

        SeedBattlefieldArtifact(_alice, "Mishra's Bauble");
        SeedBattlefieldArtifact(_alice, "Chromatic Star");
        GalvanicBlastFactory.ControlsThreeOrMoreArtifacts(_alice).Should().BeFalse();

        SeedBattlefieldArtifact(_alice, "Ornithopter");
        GalvanicBlastFactory.ControlsThreeOrMoreArtifacts(_alice).Should().BeTrue();
    }

    [Fact]
    public void Metalcraft_DoesNotCountOpponentArtifacts()
    {
        // Bob's artifacts don't contribute to Alice's Metalcraft (CR 702.95).
        SeedBattlefieldArtifact(_bob, "Mishra's Bauble");
        SeedBattlefieldArtifact(_bob, "Chromatic Star");
        SeedBattlefieldArtifact(_bob, "Ornithopter");

        GalvanicBlastFactory.ControlsThreeOrMoreArtifacts(_alice).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Resolution — Metalcraft branch
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GalvanicBlast_NoMetalcraft_Deals2Damage()
    {
        var bobStarting = _bob.LifeTotal;

        // Two artifacts is not enough — strict ">= 3" gate (CR 702.95).
        SeedBattlefieldArtifact(_alice, "Mishra's Bauble");
        SeedBattlefieldArtifact(_alice, "Chromatic Star");

        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(bobStarting - 2);
    }

    [Fact]
    public async Task GalvanicBlast_Metalcraft_Deals4Damage()
    {
        var bobStarting = _bob.LifeTotal;

        SeedBattlefieldArtifact(_alice, "Mishra's Bauble");
        SeedBattlefieldArtifact(_alice, "Chromatic Star");
        SeedBattlefieldArtifact(_alice, "Ornithopter");

        await CastAndResolveTargeting(_bob);

        // CR 702.95 — "instead" replacement: 4 damage replaces base 2.
        _bob.LifeTotal.Should().Be(bobStarting - 4);
    }

    [Fact]
    public void GalvanicBlast_ResolveDirect_ScalesWithMetalcraft()
    {
        // Direct exercise of the SpellDefinition's effect factory —
        // mirrors VoltageSurge's energy-gain unconditional test.
        SeedBattlefieldArtifact(_alice, "Sol Ring");
        SeedBattlefieldArtifact(_alice, "Mox Opal");
        SeedBattlefieldArtifact(_alice, "Ornithopter");

        var def = GalvanicBlastFactory.BuildSpellDefinition(_alice, t => t);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));
        foreach (var e in effects) e.Execute();

        // Metalcraft active — 4 damage.
        _bob.LifeTotal.Should().Be(20 - 4);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task CastAndResolveTargeting(object target)
    {
        var gb = GalvanicBlastFactory.Create(_alice);
        gb.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(gb);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.Main, _stack);

        var spell = await _flow.CastAsync(
            _alice, gb,
            GalvanicBlastFactory.BuildSpellDefinition(_alice, t => t),
            agent, ctx);

        gb.Zone.Should().Be(ZoneType.Stack);
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
