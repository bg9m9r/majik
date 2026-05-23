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
/// Tests for Hidetsugu's Second Rite (Champions of Kamigawa / Kamigawa:
/// Neon Dynasty, {2}{R}, Sorcery).
///
/// Oracle: "If target opponent's life total is exactly 10, Hidetsugu's
/// Second Rite deals 10 damage to them."
///
/// Coverage:
///   - Identity (name, type, cost) + NamedCardFactory dispatch.
///   - Target opponent at exactly 10 life → 10 damage → 0 life.
///   - Target opponent at 11 life → 0 damage (above threshold).
///   - Target opponent at 9 life → 0 damage (below threshold).
///   - Target opponent at 20 life → 0 damage (typical starting total).
///
/// CR 608.2c — the printed "if ..." is a resolve-time condition; non-10
/// life totals resolve with no effect (not an illegal-target failure).
/// </summary>
public class HidetsugusSecondRiteTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public HidetsugusSecondRiteTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void HidetsugusSecondRite_IsSorcery_AtCost2R()
    {
        var rite = HidetsugusSecondRiteFactory.Create(_alice);

        rite.Name.Should().Be("Hidetsugu's Second Rite");
        rite.ManaCost.Should().Be("{2}{R}");
        rite.HasType(CardType.Sorcery).Should().BeTrue();
        rite.Owner.Should().BeSameAs(_alice);
        rite.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_HidetsugusSecondRite()
    {
        var card = NamedCardFactory.Create("Hidetsugu's Second Rite", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Hidetsugu's Second Rite");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — life-total equality gate (CR 608.2c)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TargetOpponentAtExactlyTen_Deals10Damage_DropsToZero()
    {
        _bob.LifeTotal = 10;

        await CastAndResolveTargeting(_bob);

        // Life == 10 → condition met → 10 damage → 0 life.
        _bob.LifeTotal.Should().Be(0);
    }

    [Fact]
    public async Task TargetOpponentAtEleven_DoesNothing()
    {
        _bob.LifeTotal = 11;

        await CastAndResolveTargeting(_bob);

        // Above threshold → no effect.
        _bob.LifeTotal.Should().Be(11);
    }

    [Fact]
    public async Task TargetOpponentAtNine_DoesNothing()
    {
        _bob.LifeTotal = 9;

        await CastAndResolveTargeting(_bob);

        // Below threshold → no effect.
        _bob.LifeTotal.Should().Be(9);
    }

    [Fact]
    public async Task TargetOpponentAtTwenty_DoesNothing()
    {
        // Starting life total — no effect.
        var bobStarting = _bob.LifeTotal;
        bobStarting.Should().Be(20);

        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Cast Hidetsugu's Second Rite from Alice's hand at
    /// <paramref name="target"/> and resolve the resulting stack object.
    /// Mirrors the UnholyHeatTests / RiftBoltTests cast harness — direct
    /// cast/resolve, no priority loop.
    /// </summary>
    private async Task CastAndResolveTargeting(object target)
    {
        var rite = HidetsugusSecondRiteFactory.Create(_alice);
        rite.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(rite);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.Main, _stack);

        var spell = await _flow.CastAsync(
            _alice, rite,
            HidetsugusSecondRiteFactory.BuildSpellDefinition(t => t),
            agent, ctx);

        rite.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();
    }
}
