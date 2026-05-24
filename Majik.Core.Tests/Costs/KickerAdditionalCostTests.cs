using FluentAssertions;
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
/// Tests for <see cref="KickerAdditionalCost"/> — CR 702.33 additive
/// optional cast cost. Covers the cost primitive itself
/// (<see cref="KickerAdditionalCost.CanPay"/> /
/// <see cref="KickerAdditionalCost.Pay"/>), the cast-pipeline
/// integration through <see cref="SpellCastFlow"/> (Burst Lightning
/// is the canonical kicker-bearing factory), the bot-side
/// <see cref="KickerAltCostProbe"/> discovery surface, and the
/// post-resolve cleanup that clears <see cref="Card.WasKicked"/> so
/// the sentinel doesn't leak to copies / blinks (CR 400.7).
/// </summary>
public class KickerAdditionalCostTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public KickerAdditionalCostTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // ── Construction ─────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullCard_Throws()
    {
        Action act = () => new KickerAdditionalCost(null!, ManaCost.Parse("{4}"));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullCost_Throws()
    {
        var bl = BurstLightningFactory.Create(_alice);
        Action act = () => new KickerAdditionalCost(bl, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Description_IsKickerLabelWithCost()
    {
        var bl = BurstLightningFactory.Create(_alice);
        var cost = new KickerAdditionalCost(bl, ManaCost.Parse("{4}"));
        cost.Description.Should().StartWith("Kicker");
        cost.Description.Should().Contain("4");
    }

    // ── CanPay / Pay primitives ──────────────────────────────────────────────

    [Fact]
    public void CanPay_WithSufficientMana_ReturnsTrue()
    {
        var bl = BurstLightningFactory.Create(_alice);
        _alice.AddManaToPool(ManaCost.Parse("{4}"));

        var cost = new KickerAdditionalCost(bl, ManaCost.Parse("{4}"));
        cost.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void CanPay_WithInsufficientMana_ReturnsFalse()
    {
        var bl = BurstLightningFactory.Create(_alice);
        // No mana in pool.
        var cost = new KickerAdditionalCost(bl, ManaCost.Parse("{4}"));
        cost.CanPay(_alice).Should().BeFalse();
    }

    [Fact]
    public void Pay_DrainsManaAndStampsCardWasKicked()
    {
        var bl = BurstLightningFactory.Create(_alice);
        _alice.AddManaToPool(ManaCost.Parse("{4}"));
        bl.WasKicked.Should().BeFalse();

        var cost = new KickerAdditionalCost(bl, ManaCost.Parse("{4}"));
        cost.Pay(_alice).Should().BeTrue();

        bl.WasKicked.Should().BeTrue();
        // Mana pool drained.
        _alice.ManaPool.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Pay_WithInsufficientMana_ReturnsFalseAndDoesNotStamp()
    {
        var bl = BurstLightningFactory.Create(_alice);
        // No mana in pool.

        var cost = new KickerAdditionalCost(bl, ManaCost.Parse("{4}"));
        cost.Pay(_alice).Should().BeFalse();

        // CR 601.2g — failed payment does not stamp the sentinel.
        bl.WasKicked.Should().BeFalse();
    }

    // ── Burst Lightning cast-pipeline integration ────────────────────────────

    [Fact]
    public async Task Cast_WithoutKicker_Deals2DamageToTarget()
    {
        var bobStarting = _bob.LifeTotal;
        var bl = BurstLightningFactory.Create(_alice);
        bl.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bl);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.Main, _stack);

        var spell = await _flow.CastAsync(
            _alice, bl,
            BurstLightningFactory.BuildSpellDefinition(bl, t => t),
            agent, ctx);

        spell.Resolve();

        _bob.LifeTotal.Should().Be(bobStarting - BurstLightningFactory.BaseDamage);
        spell.WasKicked.Should().BeFalse();
        bl.WasKicked.Should().BeFalse();
    }

    [Fact]
    public async Task Cast_WithKicker_Deals4DamageAndPaysKickerMana()
    {
        var bobStarting = _bob.LifeTotal;
        var bl = BurstLightningFactory.Create(_alice);
        bl.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bl);

        // Alice has the kicker {4} in her pool ready to be drained.
        _alice.AddManaToPool(ManaCost.Parse("{4}"));

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.Main, _stack);

        var additional = new[] { BurstLightningFactory.BuildAdditionalCost(bl) };

        var spell = await _flow.CastAsync(
            _alice, bl,
            BurstLightningFactory.BuildSpellDefinition(bl, t => t),
            agent, ctx,
            additionalCosts: additional);

        // CR 702.33b — kicker stamps the resolving spell + the cast
        // card prior to resolution so the resolve body branches.
        spell.WasKicked.Should().BeTrue();
        // Mana pool drained by the kicker payment.
        _alice.ManaPool.IsEmpty.Should().BeTrue();

        spell.Resolve();

        _bob.LifeTotal.Should().Be(bobStarting - BurstLightningFactory.KickedDamage);
    }

    [Fact]
    public async Task Cast_WithKicker_ClearsCardWasKickedAfterResolve()
    {
        // CR 400.7 — the kicker sentinel on the card must not leak
        // past resolution. A re-cast / blink / token copy of the
        // same card object should see WasKicked == false after the
        // kicked spell finishes resolving.
        var bl = BurstLightningFactory.Create(_alice);
        bl.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bl);
        _alice.AddManaToPool(ManaCost.Parse("{4}"));

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.Main, _stack);

        var spell = await _flow.CastAsync(
            _alice, bl,
            BurstLightningFactory.BuildSpellDefinition(bl, t => t),
            agent, ctx,
            additionalCosts: new[] { BurstLightningFactory.BuildAdditionalCost(bl) });

        bl.WasKicked.Should().BeTrue("the cost primitive stamps the card during cast announcement");

        spell.Resolve();

        bl.WasKicked.Should().BeFalse("SpellCastFlow appends a cleanup effect that clears the sentinel after resolution");
    }

    // ── Bot probe discovery ──────────────────────────────────────────────────

    [Fact]
    public void Probe_SurfacesBurstLightningKickerCost()
    {
        var bl = BurstLightningFactory.Create(_alice);
        bl.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bl);

        var probe = new KickerAltCostProbe();
        var cost = probe.KickerCostFor(bl, _alice);

        cost.Should().NotBeNull();
        cost!.Should().Be(ManaCost.Parse("{4}"));
    }

    [Fact]
    public void Probe_ReturnsNullForNonKickerCard()
    {
        var random = new Instant("Lightning Bolt", "{R}");
        random.SetOwner(_alice);
        random.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(random);

        var probe = new KickerAltCostProbe();
        probe.KickerCostFor(random, _alice).Should().BeNull();
    }

    [Fact]
    public void Probe_DoesNotSurfaceFromGraveyard()
    {
        var bl = BurstLightningFactory.Create(_alice);
        bl.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bl);

        var probe = new KickerAltCostProbe();
        probe.KickerCostFor(bl, _alice).Should().BeNull();
    }

    [Fact]
    public void Probe_DoesNotSurfaceForNonOwner()
    {
        var bl = BurstLightningFactory.Create(_alice);
        bl.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bl);

        var probe = new KickerAltCostProbe();
        probe.KickerCostFor(bl, _bob).Should().BeNull();
    }

    [Fact]
    public void Probe_BuildsAdditionalCost_ForKickerCard()
    {
        var bl = BurstLightningFactory.Create(_alice);

        var probe = new KickerAltCostProbe();
        var built = probe.BuildAdditionalCost(bl);

        built.Should().NotBeNull();
        built!.Description.Should().Contain("Kicker");
    }

    [Fact]
    public void Probe_AlwaysYieldsZeroAlternativeCostCandidates()
    {
        // Kicker is an additional cost, not an alternative cost —
        // mirrors CascadeAltCostProbe's discovery-only posture.
        var bl = BurstLightningFactory.Create(_alice);
        bl.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bl);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.Main, _stack);

        var probe = new KickerAltCostProbe();
        probe.CandidatesFor(bl, _alice, ctx).Should().BeEmpty();
    }

    [Fact]
    public void DefaultRegistry_RegistersKickerProbe()
    {
        var registry = AlternativeCostProbeRegistry.CreateDefault();
        registry.Probes.OfType<KickerAltCostProbe>().Should().HaveCount(1);
    }
}
