using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Events;

/// <summary>
/// CR 118.8 / CR 119.4 — tests for <see cref="LifePaidEvent"/> publication
/// across the pay-life cost seams the engine funnels life PAYMENTS through:
///   1. <see cref="AdditionalCost.PayLife"/> via the central
///      <see cref="CostPayment.PayCosts(Player, System.Collections.Generic.IEnumerable{ICost}, ManaSpendContext, IEventBus)"/>
///      bus-aware cost seam (the prod activation/cast path),
///   2. <see cref="PayLifeCost"/> (an <see cref="ICost"/> pay-N-life rider)
///      via the same central seam,
///   3. the shock-land "as it enters, you may pay 2 life" ETB
///      (<see cref="Majik.Core.Effects.ShockLandReplacement"/>), looked up
///      best-effort via <see cref="EventBusRegistry"/>.
/// Plus the <see cref="Triggers.OnLifePaid(Player)"/> "you pay life" gate.
///
/// Distinguishes a life PAYMENT (a cost — CR 118.8, carried by
/// <see cref="LifePaidEvent.WasCost"/> = true) from plain life loss (burn /
/// drain), which never publishes this event.
/// </summary>
public class LifePaidEventTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Seam 1 — AdditionalCost.PayLife via the central bus-aware cost seam
    // -----------------------------------------------------------------------

    [Fact]
    public void AdditionalCostPayLife_ViaCentralSeam_PublishesLifePaidEvent_AsACost()
    {
        var bus = new EventBus();
        LifePaidEvent? captured = null;
        bus.Subscribe<LifePaidEvent>(e => captured = e);

        var cost = AdditionalCost.PayLife(2);
        new CostPayment().PayCosts(_alice, new[] { (ICost)cost }, ManaSpendContext.None, bus);

        captured.Should().NotBeNull();
        captured!.Player.Should().BeSameAs(_alice);
        captured.Amount.Should().Be(2);
        captured.WasCost.Should().BeTrue("paying life as an additional cost IS a cost");
        _alice.LifeTotal.Should().Be(18);
    }

    [Fact]
    public void AdditionalCostPayLife_NoBus_DoesNotPublish_StillPays()
    {
        var cost = AdditionalCost.PayLife(2);
        cost.Pay(_alice); // bus-less legacy path

        _alice.LifeTotal.Should().Be(18, "the payment still happens without a bus");
    }

    [Fact]
    public void AdditionalCostPayLife_Zero_PublishesNothing()
    {
        var bus = new EventBus();
        var captured = new List<LifePaidEvent>();
        bus.Subscribe<LifePaidEvent>(captured.Add);

        var cost = AdditionalCost.PayLife(0);
        new CostPayment().PayCosts(_alice, new[] { (ICost)cost }, ManaSpendContext.None, bus);

        captured.Should().BeEmpty("paying 0 life is not 'paying life' — CR 119.4");
        _alice.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Seam 2 — PayLifeCost (ICost) via the central bus-aware cost seam
    // -----------------------------------------------------------------------

    [Fact]
    public void PayLifeCost_ViaCentralSeam_PublishesLifePaidEvent_AsACost()
    {
        var bus = new EventBus();
        LifePaidEvent? captured = null;
        bus.Subscribe<LifePaidEvent>(e => captured = e);

        var cost = new PayLifeCost(3);
        new CostPayment().PayCosts(_alice, new[] { (ICost)cost }, ManaSpendContext.None, bus);

        captured.Should().NotBeNull();
        captured!.Player.Should().BeSameAs(_alice);
        captured.Amount.Should().Be(3);
        captured.WasCost.Should().BeTrue();
        _alice.LifeTotal.Should().Be(17);
    }

    [Fact]
    public void PayLifeCost_BusLessPay_DoesNotPublish_StillPays()
    {
        var cost = new PayLifeCost(3);
        cost.Pay(_alice);
        _alice.LifeTotal.Should().Be(17);
    }

    // -----------------------------------------------------------------------
    // Plain life loss (NOT a payment) never publishes a LifePaidEvent
    // -----------------------------------------------------------------------

    [Fact]
    public void PlainLoseLife_DoesNotPublishLifePaidEvent()
    {
        var bus = new EventBus();
        var captured = new List<LifePaidEvent>();
        bus.Subscribe<LifePaidEvent>(captured.Add);

        EventBusRegistry.Clear();
        EventBusRegistry.Set(_alice, bus);
        try
        {
            _alice.LoseLife(4); // burn / drain — life loss, not a payment
            captured.Should().BeEmpty("life LOST to a spell/effect is not life PAID as a cost");
            _alice.LifeTotal.Should().Be(16);
        }
        finally
        {
            EventBusRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // Bolas's Citadel — "pay life equal to its mana value" alt cost
    // (CR 118.9, best-effort bus lookup in OnResolved)
    // -----------------------------------------------------------------------

    [Fact]
    public void BolassCitadelAltCost_OnResolved_PublishesLifePaidEvent_EqualToManaValue()
    {
        var bus = new EventBus();
        LifePaidEvent? captured = null;
        bus.Subscribe<LifePaidEvent>(e => captured = e);

        EventBusRegistry.Clear();
        EventBusRegistry.Set(_alice, bus);
        try
        {
            // A {2}{B} (MV 3) spell cast off the top via the Citadel rider.
            var spell = new Card("Top Spell", "{2}{B}");
            spell.SetOwner(_alice);

            var altCost = new PayLifeEqualToManaValueAlternativeCost();
            altCost.OnResolved(spell, _alice);

            _alice.LifeTotal.Should().Be(17, "MV 3 → 3 life paid");
            captured.Should().NotBeNull();
            captured!.Player.Should().BeSameAs(_alice);
            captured.Amount.Should().Be(3);
            captured.WasCost.Should().BeTrue();
        }
        finally
        {
            EventBusRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // Seam 3 — shock-land ETB "you may pay 2 life" (best-effort bus lookup)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ShockLand_AgentPaysTwoLife_PublishesLifePaidEvent_AsACost()
    {
        AgentRegistry.Clear();
        var bus = new EventBus();
        LifePaidEvent? captured = null;
        bus.Subscribe<LifePaidEvent>(e => captured = e);

        EventBusRegistry.Clear();
        EventBusRegistry.Set(_alice, bus);
        try
        {
            var land = new Land("Overgrown Tomb") { Owner = _alice, Zone = ZoneType.Hand };
            var rbus = new ReplacementBus();
            rbus.Register(new ShockLandReplacement(land));

            var agent = new ScriptedAgent();
            agent.QueueYesNo(true);
            var ctx = ResolutionContext.For(_alice, agent, game: null, chosenTargets: null);

            var intent = new ZoneMoveIntent(
                land, ZoneType.Hand, ZoneType.Battlefield, Controller: _alice);
            var result = await rbus.ApplyAsync(intent, ctx);

            result.Should().NotBeNull();
            result!.EntersTapped.Should().BeFalse("agent said yes → enters untapped");
            _alice.LifeTotal.Should().Be(18);
            captured.Should().NotBeNull();
            captured!.Player.Should().BeSameAs(_alice);
            captured.Amount.Should().Be(2);
            captured.WasCost.Should().BeTrue("the shock-land 2 life is paid as a cost — CR 118.8");
        }
        finally
        {
            EventBusRegistry.Clear();
            AgentRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // Triggers.OnLifePaid — "you pay life" gate
    // -----------------------------------------------------------------------

    [Fact]
    public void TriggersOnLifePaid_FiresForMatchingPlayer_NotForOpponent()
    {
        var cond = Triggers.OnLifePaid(_alice);

        cond.Matches(new LifePaidEvent(_alice, 2, wasCost: true), null!).Should().BeTrue();
        cond.Matches(new LifePaidEvent(_alice, 1, wasCost: true), null!).Should().BeTrue();
        cond.Matches(new LifePaidEvent(_bob, 2, wasCost: true), null!).Should().BeFalse();
    }

    [Fact]
    public void TriggersOnAnyPlayerPaysLife_FiresForEveryPayer()
    {
        var cond = Triggers.OnAnyPlayerPaysLife();

        cond.Matches(new LifePaidEvent(_alice, 2, wasCost: true), null!).Should().BeTrue();
        cond.Matches(new LifePaidEvent(_bob, 5, wasCost: true), null!).Should().BeTrue();
    }
}
