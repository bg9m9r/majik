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
/// Tests for Burst Lightning (Zendikar / Modern Masters, {R}, Instant).
///
/// Covers:
///   - Card identity (Instant, {R}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve default-not-kicked → 2 damage to target.
///   - Resolve structural kicked branch → 4 damage to target.
///
/// Kicker (CR 702.33) is now a real <see cref="IAdditionalCost"/>
/// primitive — <see cref="KickerAdditionalCost"/>. The kicked branch
/// is reached by layering the cost onto the cast via
/// <see cref="SpellCastFlow.CastAsync"/>'s <c>additionalCosts</c>
/// parameter; the not-kicked branch is the default cast with no
/// additional cost. See <see cref="BurstLightningFactory"/> xmldoc.
/// </summary>
public class BurstLightningTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public BurstLightningTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BurstLightning_IsInstant_AtCostR()
    {
        var bl = BurstLightningFactory.Create(_alice);

        bl.Name.Should().Be("Burst Lightning");
        bl.ManaCost.Should().Be("{R}");
        bl.HasType(CardType.Instant).Should().BeTrue();
        bl.Owner.Should().BeSameAs(_alice);
        bl.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BurstLightning()
    {
        var card = NamedCardFactory.Create("Burst Lightning", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Burst Lightning");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — kicker gate
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BurstLightning_NotKicked_Deals2Damage()
    {
        var bobStarting = _bob.LifeTotal;

        await CastAndResolveTargeting(_bob, wasKicked: false);

        // Default cast — base 2 damage (CR 702.33b: kicker gate is
        // false, so the printed "instead" clause does not apply).
        _bob.LifeTotal.Should().Be(bobStarting - 2);
    }

    [Fact]
    public async Task BurstLightning_Kicked_Deals4Damage()
    {
        var bobStarting = _bob.LifeTotal;

        await CastAndResolveTargeting(_bob, wasKicked: true);

        // Kicked branch — "If Burst Lightning was kicked, it deals 4
        // damage to that target instead." (CR 702.33b).
        _bob.LifeTotal.Should().Be(bobStarting - 4);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Cast Burst Lightning from Alice's hand at <paramref name="target"/>
    /// and resolve the resulting stack object. Mirrors the
    /// UnholyHeatTests cast harness — direct cast/resolve, no priority
    /// loop. The <paramref name="wasKicked"/> flag is exercised by
    /// layering a <see cref="KickerAdditionalCost"/> onto the cast (the
    /// production wiring; see <see cref="BurstLightningFactory"/> xmldoc).
    /// </summary>
    private async Task CastAndResolveTargeting(object target, bool wasKicked)
    {
        var bl = BurstLightningFactory.Create(_alice);
        bl.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bl);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.Main, _stack);

        IReadOnlyList<IAdditionalCost>? additional = null;
        if (wasKicked)
        {
            // CR 702.33 — pay the kicker mana into Alice's pool so
            // KickerAdditionalCost.Pay (which routes through
            // Player.PayMana) succeeds.
            _alice.AddManaToPool(ManaCost.Parse("{4}"));
            additional = new[] { BurstLightningFactory.BuildAdditionalCost(bl) };
        }

        var spell = await _flow.CastAsync(
            _alice, bl,
            BurstLightningFactory.BuildSpellDefinition(bl, t => t),
            agent, ctx,
            additionalCosts: additional);

        bl.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();
    }
}
