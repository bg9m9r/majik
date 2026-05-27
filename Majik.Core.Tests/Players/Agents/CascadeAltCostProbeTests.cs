using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData.Factories;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Players.Agents;

/// <summary>
/// Unit tests for <see cref="CascadeAltCostProbe"/>. Cascade isn't an
/// alternative cost — the probe's
/// <see cref="CascadeAltCostProbe.CandidatesFor"/> always yields empty.
/// Its real surface is the <see cref="CascadeAltCostProbe.HasCascade"/>
/// discovery query, exercised on the shipped cascade ship-list (Crashing
/// Footfalls, Living End).
/// </summary>
public class CascadeAltCostProbeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Majik.Core.Stack.Stack _stack;

    public CascadeAltCostProbeTests()
    {
        _stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
    }

    [Fact]
    public void HasCascade_CrashingFootfalls_IsTrue()
    {
        var probe = new CascadeAltCostProbe();
        var card = CrashingFootfallsFactory.Create(_alice);
        probe.HasCascade(card).Should().BeTrue();
    }

    [Fact]
    public void HasCascade_LivingEnd_IsTrue()
    {
        var probe = new CascadeAltCostProbe();
        var card = LivingEndFactory.Create(_alice);
        probe.HasCascade(card).Should().BeTrue();
    }

    [Fact]
    public void HasCascade_NonCascadeCard_IsFalse()
    {
        var probe = new CascadeAltCostProbe();
        var bolt = new Instant("Lightning Bolt", "{R}");
        probe.HasCascade(bolt).Should().BeFalse();
    }

    [Fact]
    public void CandidatesFor_AlwaysEmpty()
    {
        var probe = new CascadeAltCostProbe();
        var card = CrashingFootfallsFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, PhaseStateType.PreCombatMain, _stack);

        probe.CandidatesFor(card, _alice, ctx).Should().BeEmpty();
    }

    [Fact]
    public void CustomLookup_OverridesDefault()
    {
        // Custom lookup: every card has cascade.
        var probe = new CascadeAltCostProbe(_ => true);
        var bolt = new Instant("Lightning Bolt", "{R}");
        probe.HasCascade(bolt).Should().BeTrue();
    }
}
