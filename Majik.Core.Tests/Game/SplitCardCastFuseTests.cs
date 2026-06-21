using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// Tests for the split-card Fuse cast surface (CR 702.102) — casting BOTH
/// halves of a split card as one spell, paying the combined mana cost and
/// collecting targets for both halves through one cast pass.
///
/// Exercises the declarative composition in <see cref="SplitCardCast"/> +
/// the combined-cost gate <see cref="FuseAlternativeCost"/> on the two real
/// Fuse split cards (oracle text verified against Scryfall 2026-06-21):
///   Wear // Tear ({1}{R} // {W}): destroy target artifact AND target
///     enchantment.
///   Beck // Call ({G}{U} // {4}{W}{U}): the Beck creature-ETB delayed trigger
///     AND create four 1/1 Birds.
/// Both faces print "Fuse (You may cast one or both halves of this card from
/// your hand.)".
/// </summary>
public class SplitCardCastFuseTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── ManaCost.Combine — CR 702.102b combined cost ───────────────────────

    [Fact]
    public void Combine_SumsBothHalvesFieldWise_NotByStringConcat()
    {
        // Wear {1}{R} + Tear {W} = {1}{R}{W}: generic 1, red 1, white 1.
        var fused = SplitCardCast.FuseCost("{1}{R}", "{W}");
        fused.Generic.Should().Be(1);
        fused.Red.Should().Be(1);
        fused.White.Should().Be(1);
        fused.TotalValue.Should().Be(3);
    }

    [Fact]
    public void Combine_AddsTwoGenericClusters_WithoutStringConcatBug()
    {
        // Beck {G}{U} + Call {4}{W}{U} = generic 4, green 1, white 1, blue 2.
        // String-concat re-parse would mis-collapse to generic 4 only;
        // field-wise sum is the correct combiner.
        var fused = SplitCardCast.FuseCost("{G}{U}", "{4}{W}{U}");
        fused.Generic.Should().Be(4);
        fused.Green.Should().Be(1);
        fused.White.Should().Be(1);
        fused.Blue.Should().Be(2);
        fused.TotalValue.Should().Be(8);
    }

    [Fact]
    public void Combine_ConcatenatesHybridPips()
    {
        // {1}{W/B} + {4}{B/R}{B/R}: generic 5, three hybrid pips.
        var fused = SplitCardCast.FuseCost("{1}{W/B}", "{4}{B/R}{B/R}");
        fused.Generic.Should().Be(5);
        fused.HybridPips.Should().HaveCount(3);
    }

    // ── FuseAlternativeCost — CR 702.102b cost gate ────────────────────────

    [Fact]
    public void FuseAlternativeCost_CarriesCombinedCost_AndOnlyFromHand()
    {
        var card = WearTearFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var fuse = new FuseAlternativeCost(
            SplitCardCast.FuseCost(
                WearTearFactory.WearManaCost, WearTearFactory.TearManaCost));

        fuse.AlternativeManaCost.Generic.Should().Be(1);
        fuse.AlternativeManaCost.Red.Should().Be(1);
        fuse.AlternativeManaCost.White.Should().Be(1);
        fuse.CanCastFor(card, _alice).Should().BeTrue();

        // CR 702.102a — Fuse is a from-hand-only cast.
        card.SetZone(ZoneType.Graveyard);
        fuse.CanCastFor(card, _alice).Should().BeFalse();
    }

    // ── Wear // Tear fused resolution — both halves, in order ──────────────

    [Fact]
    public void WearTear_Fused_DestroysBothArtifactAndEnchantment()
    {
        var artifact = new Artifact("Sol Ring", "{1}")
        { Owner = _bob, Controller = _bob };
        artifact.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(artifact);

        var enchantment = new Enchantment("Sylvan Library", "{1}{G}")
        { Owner = _bob, Controller = _bob };
        enchantment.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(enchantment);

        var fused = SplitCardCast.BuildFusedDefinition(
            WearTearFactory.BuildWearDefinition(resolver: x => x),
            WearTearFactory.BuildTearDefinition(resolver: x => x),
            "Wear", "Tear");

        // CR 702.102 — fused spell carries BOTH halves' target requests, keyed
        // by mode index (left = 0, right = 1).
        fused.TargetRequests.Should().HaveCount(2);
        fused.TargetRequests[0].ModeIndex.Should().Be(0);
        fused.TargetRequests[1].ModeIndex.Should().Be(1);

        // Targets keyed by mode index: slot 0 = Wear's artifact, slot 1 = Tear's
        // enchantment.
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                new object[] { artifact },
                new object[] { enchantment },
            },
            Mana: ManaPayment.Empty,
            ModeIndexes: SplitCardCast.FusedModeChoice);

        foreach (var fx in fused.EffectFactory(chosen))
        {
            fx.Execute();
        }

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "the fused cast does what Wear does (CR 702.102e)");
        enchantment.Zone.Should().Be(ZoneType.Graveyard,
            because: "the fused cast ALSO does what Tear does (CR 702.102e)");
    }

    // ── End-to-end: cast Wear // Tear FUSED through SpellCastFlow ───────────

    [Fact]
    public async Task WearTear_Fused_CastThroughSpellCastFlow_PaysCombinedCost_DestroysBoth()
    {
        var bus = new Majik.Core.Events.EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);
        var flow = new SpellCastFlow(stack, zones, bus);

        var card = WearTearFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var artifact = new Artifact("Sol Ring", "{1}")
        { Owner = _bob, Controller = _bob };
        artifact.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(artifact);

        var enchantment = new Enchantment("Sylvan Library", "{1}{G}")
        { Owner = _bob, Controller = _bob };
        enchantment.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(enchantment);

        var fusedDef = WearTearFactory.BuildFusedDefinition(resolver: x => x);
        var fuseCost = new FuseAlternativeCost(WearTearFactory.FuseCost());

        // CR 702.102 — choose BOTH halves, then a target for each (Wear → mode 0
        // → artifact; Tear → mode 1 → enchantment).
        var agent = new ScriptedAgent();
        agent.QueueModes(SplitCardCast.FusedModeChoice.ToArray());
        agent.QueueTargets(new[] { (object)artifact });
        agent.QueueTargets(new[] { (object)enchantment });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, stack);

        ManaCost? effectiveCost = null;
        var spell = await flow.CastAsync(
            _alice, card, fusedDef, agent, ctx,
            alternativeCost: fuseCost,
            preChosenMana: ManaPayment.Empty,
            payManaCost: c => { effectiveCost = c; return true; });

        card.Zone.Should().Be(ZoneType.Stack);
        // CR 702.102b — the combined cost ({1}{R} + {W} = {1}{R}{W}) was the
        // effective cost paid, not the front-half cost alone.
        effectiveCost.Should().NotBeNull();
        effectiveCost!.Generic.Should().Be(1);
        effectiveCost.Red.Should().Be(1);
        effectiveCost.White.Should().Be(1);

        spell.Resolve();

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "the fused cast does what Wear does (CR 702.102e)");
        enchantment.Zone.Should().Be(ZoneType.Graveyard,
            because: "the fused cast ALSO does what Tear does (CR 702.102e)");
    }

    // ── Beck // Call fused resolution — untargeted both halves ─────────────

    [Fact]
    public void BeckCall_Fused_RunsBeckTriggerThenCreatesBirds()
    {
        // triggers: null — Beck's delayed-trigger registration no-ops without a
        // TriggerManager; this test asserts the BOTH-halves ordered resolution,
        // not Beck's draw (covered by BeckCallTests).
        var fused = BeckCallFactory.BuildFusedDefinition(
            _alice, triggers: null, zoneService: null, mayDraw: () => false);

        // Untargeted halves contribute no target requests.
        fused.TargetRequests.Should().BeEmpty();

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: System.Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            ModeIndexes: SplitCardCast.FusedModeChoice);

        foreach (var fx in fused.EffectFactory(chosen))
        {
            fx.Execute();
        }

        // Call's four 1/1 Birds entered under the caster.
        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.Name == "Bird").Should().Be(4,
            because: "the fused cast does what Call does (CR 702.102e)");
    }
}
