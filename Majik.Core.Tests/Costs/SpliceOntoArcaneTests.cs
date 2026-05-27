using FluentAssertions;
using Majik.Core.Abilities;
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

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Tests for <see cref="SpliceOntoArcaneCost"/> — CR 702.46 additional
/// cost paid as an Arcane spell is cast. Covers the cost primitive
/// itself (CanPay / Pay gating on Arcane subtype + hand residence +
/// mana availability), the cast-pipeline integration through
/// <see cref="SpellCastFlow"/> (Goryo's Vengeance + Desperate Ritual
/// splice rider end-to-end), and the negative paths (no Arcane
/// subtype rejects; declining splice runs only the base body).
/// </summary>
public class SpliceOntoArcaneTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpliceOntoArcaneTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // ── Construction ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullArcaneTarget_Throws()
    {
        var dr = DesperateRitualFactory.Create(_alice);
        Action act = () => new SpliceOntoArcaneCost(
            null!, dr, ManaCost.Parse("{1}{R}"), _ => Array.Empty<IEffect>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullSplicedCard_Throws()
    {
        var goryos = GoryosVengeanceFactory.Create(_alice);
        Action act = () => new SpliceOntoArcaneCost(
            goryos, null!, ManaCost.Parse("{1}{R}"), _ => Array.Empty<IEffect>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullSpliceCost_Throws()
    {
        var goryos = GoryosVengeanceFactory.Create(_alice);
        var dr = DesperateRitualFactory.Create(_alice);
        Action act = () => new SpliceOntoArcaneCost(
            goryos, dr, null!, _ => Array.Empty<IEffect>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullEffectBuilder_Throws()
    {
        var goryos = GoryosVengeanceFactory.Create(_alice);
        var dr = DesperateRitualFactory.Create(_alice);
        Action act = () => new SpliceOntoArcaneCost(
            goryos, dr, ManaCost.Parse("{1}{R}"), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── Arcane subtype on retrofitted cards ──────────────────────────

    [Fact]
    public void DesperateRitual_CarriesArcaneSubtype()
    {
        var card = DesperateRitualFactory.Create(_alice);
        card.HasSubtype(CardSubtype.Arcane).Should().BeTrue(
            "CR 205.3k — Desperate Ritual prints as Instant — Arcane");
    }

    [Fact]
    public void GoryosVengeance_CarriesArcaneSubtype()
    {
        var card = GoryosVengeanceFactory.Create(_alice);
        card.HasSubtype(CardSubtype.Arcane).Should().BeTrue(
            "CR 205.3k — Goryo's Vengeance prints as Instant — Arcane");
    }

    [Fact]
    public void ThroughTheBreach_CarriesArcaneSubtype()
    {
        var card = ThroughTheBreachFactory.Create(_alice);
        card.HasSubtype(CardSubtype.Arcane).Should().BeTrue(
            "CR 205.3k — Through the Breach prints as Instant — Arcane");
    }

    // ── CanPay / Pay primitives ──────────────────────────────────────

    [Fact]
    public void CanPay_HappyPath_TargetIsArcane_SplicedInHand_ManaAvailable()
    {
        var goryos = GoryosVengeanceFactory.Create(_alice);
        var dr = DesperateRitualFactory.Create(_alice);
        dr.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(dr);
        _alice.AddManaToPool(ManaCost.Parse("{1}{R}"));

        var cost = DesperateRitualFactory.BuildSpliceCost(goryos, dr);

        cost.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void CanPay_TargetMissingArcaneSubtype_ReturnsFalse()
    {
        // A non-Arcane instant — Lightning Bolt-shape. Build raw so we
        // don't accidentally pick up an Arcane-stamped factory.
        var nonArcane = new Instant("Lightning Bolt", "{R}");
        nonArcane.SetOwner(_alice);
        nonArcane.SetController(_alice);

        var dr = DesperateRitualFactory.Create(_alice);
        dr.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(dr);
        _alice.AddManaToPool(ManaCost.Parse("{1}{R}"));

        var cost = DesperateRitualFactory.BuildSpliceCost(nonArcane, dr);

        cost.CanPay(_alice).Should().BeFalse(
            "CR 702.46 — splice can only attach to spells with the Arcane subtype");
    }

    [Fact]
    public void CanPay_SplicedCardNotInHand_ReturnsFalse()
    {
        var goryos = GoryosVengeanceFactory.Create(_alice);
        var dr = DesperateRitualFactory.Create(_alice);
        dr.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(dr);
        _alice.AddManaToPool(ManaCost.Parse("{1}{R}"));

        var cost = DesperateRitualFactory.BuildSpliceCost(goryos, dr);

        cost.CanPay(_alice).Should().BeFalse(
            "CR 702.46a — splice card must be revealed from hand");
    }

    [Fact]
    public void CanPay_InsufficientMana_ReturnsFalse()
    {
        var goryos = GoryosVengeanceFactory.Create(_alice);
        var dr = DesperateRitualFactory.Create(_alice);
        dr.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(dr);
        // No mana in pool.

        var cost = DesperateRitualFactory.BuildSpliceCost(goryos, dr);

        cost.CanPay(_alice).Should().BeFalse(
            "CR 702.46 — splice cost must be payable to splice");
    }

    [Fact]
    public void Pay_DrainsSpliceMana_AndLeavesSplicedCardInHand()
    {
        var goryos = GoryosVengeanceFactory.Create(_alice);
        var dr = DesperateRitualFactory.Create(_alice);
        dr.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(dr);
        _alice.AddManaToPool(ManaCost.Parse("{1}{R}"));

        var cost = DesperateRitualFactory.BuildSpliceCost(goryos, dr);

        cost.Pay(_alice).Should().BeTrue();

        _alice.ManaPool.IsEmpty.Should().BeTrue("splice mana fully drained");
        dr.Zone.Should().Be(ZoneType.Hand,
            "CR 702.46a — the spliced card stays in the caster's hand");
        _alice.Zones.Hand.GetCards().Should().Contain(dr);
    }

    [Fact]
    public void Description_IncludesSpliceCost()
    {
        var goryos = GoryosVengeanceFactory.Create(_alice);
        var dr = DesperateRitualFactory.Create(_alice);
        var cost = DesperateRitualFactory.BuildSpliceCost(goryos, dr);

        cost.Description.Should().Contain("Splice onto Arcane");
        cost.Description.Should().Contain("1").And.Contain("R");
    }

    // ── BuildSplicedEffects ──────────────────────────────────────────

    [Fact]
    public void BuildSplicedEffects_ReturnsRiderEffects()
    {
        var goryos = GoryosVengeanceFactory.Create(_alice);
        var dr = DesperateRitualFactory.Create(_alice);

        var cost = DesperateRitualFactory.BuildSpliceCost(goryos, dr);
        var effects = cost.BuildSplicedEffects(_alice);

        effects.Should().NotBeNull();
        effects.Should().NotBeEmpty();
    }

    // ── SpellCastFlow integration ────────────────────────────────────

    [Fact]
    public async Task Cast_GoryosVengeance_WithSplicedDesperateRitual_PaysBothCostsAndFiresBothEffects()
    {
        // Caster has {1}{B} for Goryo + {1}{R} for splice = pool gets
        // {2}{B}{R}. We pre-load the splice mana into the pool (the
        // additional-cost loop drains it) and pre-chose ManaPayment for
        // the printed cost so the test doesn't need real mana sources.
        var goryos = GoryosVengeanceFactory.Create(_alice);
        goryos.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(goryos);

        var dr = DesperateRitualFactory.Create(_alice);
        dr.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(dr);

        // Splice mana — {1}{R}. Drained by SpliceOntoArcaneCost.Pay.
        _alice.AddManaToPool(ManaCost.Parse("{1}{R}"));

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        // Goryo's resolve body needs the caster + zone service; in this
        // shape-only test there's nothing in the graveyard so the
        // reanimate path is a clean no-op (CR 117.x). The salient
        // assertion is that the spliced Desperate Ritual effect ALSO
        // fires — adding {R}{R}{R} to the pool.
        var goryosDef = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => GoryosVengeanceFactory.BuildResolveEffect(_alice, _zones, triggers: null));

        var splice = DesperateRitualFactory.BuildSpliceCost(goryos, dr);

        var spell = await _flow.CastAsync(
            _alice, goryos, goryosDef, agent, ctx,
            additionalCosts: new IAdditionalCost[] { splice });

        // Splice mana drained at cast announcement.
        _alice.ManaPool.IsEmpty.Should().BeTrue(
            "splice cost {1}{R} drained from the pool during announcement");

        // Goryo is on the stack; Desperate Ritual stays in hand.
        goryos.Zone.Should().Be(ZoneType.Stack);
        dr.Zone.Should().Be(ZoneType.Hand,
            "CR 702.46a — the spliced card stays in the caster's hand");
        _alice.Zones.Hand.GetCards().Should().Contain(dr);

        spell.Resolve();

        // The spliced Desperate Ritual rider fires — three red mana enter
        // Alice's mana pool. (The Goryo no-op produced nothing else.)
        _alice.ManaPool.Red.Should().Be(3,
            "splice rider's resolve effect appended to Goryo's effect chain");
    }

    [Fact]
    public async Task Cast_GoryosVengeance_WithoutSpliceRider_OnlyBaseSpellResolves()
    {
        var goryos = GoryosVengeanceFactory.Create(_alice);
        goryos.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(goryos);

        var dr = DesperateRitualFactory.Create(_alice);
        dr.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(dr);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var goryosDef = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => GoryosVengeanceFactory.BuildResolveEffect(_alice, _zones, triggers: null));

        // Decline splice — no additional cost supplied.
        var spell = await _flow.CastAsync(
            _alice, goryos, goryosDef, agent, ctx);

        spell.Resolve();

        // Desperate Ritual was NOT spliced. Its {R}{R}{R} rider did NOT
        // fire — pool stays empty (Goryo's no-op produces nothing).
        _alice.ManaPool.Total.Should().Be(0,
            "no splice rider was supplied — only Goryo's body ran");
        dr.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public async Task Cast_NonArcaneSpell_WithSpliceRider_RejectedAtAdditionalCostPreCheck()
    {
        // Cast a non-Arcane spell (Burst Lightning) and try to splice
        // Desperate Ritual onto it. The splice cost's CanPay fails the
        // Arcane subtype gate, so SpellCastFlow's CR 601.2g pre-check
        // throws — no mana mutation, no zone movement.
        var bl = BurstLightningFactory.Create(_alice);
        bl.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bl);
        bl.HasSubtype(CardSubtype.Arcane).Should().BeFalse(
            "Burst Lightning is not an Arcane spell");

        var dr = DesperateRitualFactory.Create(_alice);
        dr.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(dr);

        _alice.AddManaToPool(ManaCost.Parse("{1}{R}"));

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var splice = DesperateRitualFactory.BuildSpliceCost(bl, dr);

        Func<Task> act = () => _flow.CastAsync(
            _alice, bl,
            BurstLightningFactory.BuildSpellDefinition(bl, t => t),
            agent, ctx,
            additionalCosts: new IAdditionalCost[] { splice });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Splice onto Arcane*");

        // Splice mana NOT drained (pre-check fails before any cost
        // payment). Card still in hand.
        _alice.ManaPool.Total.Should().Be(2,
            "pre-check rejection short-circuits before mana is drained");
        bl.Zone.Should().Be(ZoneType.Hand,
            "rejected cast leaves the card untouched");
        dr.Zone.Should().Be(ZoneType.Hand);
    }
}
