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
/// Tests for Roil Eruption (Zendikar Rising, {1}{R}, Sorcery).
///
/// Covers ONLY the card's unique behaviour (kicker-conditional damage)
/// plus a single identity assert for the non-vanilla cost. Dispatch +
/// well-formedness are owned by
/// <see cref="CardFactoryContractTests"/>.
///
/// Kicker (CR 702.33) is a real <see cref="IAdditionalCost"/> primitive —
/// <see cref="KickerAdditionalCost"/>. The kicked branch is reached by
/// layering the cost onto the cast via
/// <see cref="SpellCastFlow.CastAsync"/>'s <c>additionalCosts</c>
/// parameter; the not-kicked branch is the default cast with no
/// additional cost. See <see cref="RoilEruptionFactory"/> xmldoc.
/// </summary>
[Trait("Color", "R")]
public class RoilEruptionTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public RoilEruptionTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    [Fact]
    public void RoilEruption_IsSorcery_AtCost1R()
    {
        var re = RoilEruptionFactory.Create(_alice);

        re.Name.Should().Be("Roil Eruption");
        re.ManaCost.Should().Be("{1}{R}");
        re.HasType(CardType.Sorcery).Should().BeTrue();
        re.Owner.Should().BeSameAs(_alice);
        re.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public async Task RoilEruption_NotKicked_Deals3Damage()
    {
        var bobStarting = _bob.LifeTotal;

        await CastAndResolveTargeting(_bob, wasKicked: false);

        // Default cast — base 3 damage (CR 702.33b: kicker gate is false,
        // so the printed "instead" clause does not apply).
        _bob.LifeTotal.Should().Be(bobStarting - 3);
    }

    [Fact]
    public async Task RoilEruption_Kicked_Deals5Damage()
    {
        var bobStarting = _bob.LifeTotal;

        await CastAndResolveTargeting(_bob, wasKicked: true);

        // Kicked branch — "If this spell was kicked, it deals 5 damage
        // instead." (CR 702.33b).
        _bob.LifeTotal.Should().Be(bobStarting - 5);
    }

    /// <summary>
    /// Cast Roil Eruption from Alice's hand at <paramref name="target"/>
    /// and resolve the resulting stack object. Mirrors the
    /// BurstLightningTests cast harness — direct cast/resolve, no priority
    /// loop. The <paramref name="wasKicked"/> flag is exercised by layering
    /// a <see cref="KickerAdditionalCost"/> onto the cast (the production
    /// wiring; see <see cref="RoilEruptionFactory"/> xmldoc).
    /// </summary>
    private async Task CastAndResolveTargeting(object target, bool wasKicked)
    {
        var re = RoilEruptionFactory.Create(_alice);
        re.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(re);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, StepStateType.PreCombatMain, _stack);

        IReadOnlyList<IAdditionalCost>? additional = null;
        if (wasKicked)
        {
            // CR 702.33 — pay the kicker mana into Alice's pool so
            // KickerAdditionalCost.Pay (which routes through Player.PayMana)
            // succeeds.
            _alice.AddManaToPool(ManaCost.Parse("{5}"));
            additional = new[] { RoilEruptionFactory.BuildAdditionalCost(re) };
        }

        var spell = await _flow.CastAsync(
            _alice, re,
            RoilEruptionFactory.BuildSpellDefinition(re, t => t),
            agent, ctx,
            additionalCosts: additional);

        re.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();
    }
}
