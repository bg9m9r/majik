using FluentAssertions;
using Majik.Core.Cards;
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
/// Unit tests for <see cref="SuspendAltCostProbe"/> — yields
/// <see cref="SuspendAlternativeCost"/> candidates for cards in the
/// caster's hand whose name maps to a printed Suspend descriptor.
/// </summary>
public class SuspendAltCostProbeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Majik.Core.Stack.Stack _stack;

    public SuspendAltCostProbeTests()
    {
        _stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
    }

    private GameContext NewContext()
        => new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

    private static T InHand<T>(Player owner, T card) where T : Card
    {
        card.SetOwner(owner);
        card.SetController(owner);
        card.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(card);
        return card;
    }

    [Fact]
    public void DefaultLookup_RiftBolt_ReturnsSuspendOneR()
    {
        var bolt = new Sorcery("Rift Bolt", "{2}{R}");
        var desc = SuspendAltCostProbe.DefaultLookup(bolt);

        desc.Should().NotBeNull();
        desc!.Value.TimeCounters.Should().Be(1);
        desc.Value.SuspendManaCost.Should().Be(ManaCost.Parse("{R}"));
    }

    [Fact]
    public void DefaultLookup_SearchForTomorrow_ReturnsSuspendTwoG()
    {
        var sft = new Sorcery("Search for Tomorrow", "{2}{G}");
        var desc = SuspendAltCostProbe.DefaultLookup(sft);

        desc.Should().NotBeNull();
        desc!.Value.TimeCounters.Should().Be(2);
        desc.Value.SuspendManaCost.Should().Be(ManaCost.Parse("{G}"));
    }

    [Fact]
    public void DefaultLookup_UnknownCard_ReturnsNull()
    {
        var bolt = new Sorcery("Lightning Bolt", "{R}");
        SuspendAltCostProbe.DefaultLookup(bolt).Should().BeNull();
    }

    [Fact]
    public void CandidatesFor_CardInHand_YieldsSuspendAltCost()
    {
        var bolt = InHand(_alice, new Sorcery("Rift Bolt", "{2}{R}"));
        var probe = new SuspendAltCostProbe(SuspendAltCostProbe.DefaultLookup);

        var candidates = probe.CandidatesFor(bolt, _alice, NewContext()).ToList();

        candidates.Should().HaveCount(1);
        candidates[0].Should().BeOfType<SuspendAlternativeCost>();
        var sac = (SuspendAlternativeCost)candidates[0];
        sac.TimeCounters.Should().Be(1);
        sac.AlternativeManaCost.Should().Be(ManaCost.Parse("{R}"));
    }

    [Fact]
    public void CandidatesFor_CardNotInHand_YieldsNothing()
    {
        var bolt = new Sorcery("Rift Bolt", "{2}{R}");
        bolt.SetOwner(_alice);
        bolt.SetZone(ZoneType.Graveyard);

        var probe = new SuspendAltCostProbe(SuspendAltCostProbe.DefaultLookup);
        probe.CandidatesFor(bolt, _alice, NewContext()).Should().BeEmpty(
            "suspend is paid from the hand (CR 702.62b).");
    }

    [Fact]
    public void CandidatesFor_DifferentOwner_YieldsNothing()
    {
        var bolt = InHand(_alice, new Sorcery("Rift Bolt", "{2}{R}"));
        var probe = new SuspendAltCostProbe(SuspendAltCostProbe.DefaultLookup);
        probe.CandidatesFor(bolt, _bob, NewContext()).Should().BeEmpty(
            "only the card's owner may suspend it.");
    }

    [Fact]
    public void CandidatesFor_NoDescriptor_YieldsNothing()
    {
        var bolt = InHand(_alice, new Instant("Lightning Bolt", "{R}"));
        var probe = new SuspendAltCostProbe(SuspendAltCostProbe.DefaultLookup);
        probe.CandidatesFor(bolt, _alice, NewContext()).Should().BeEmpty(
            "Lightning Bolt has no printed suspend cost.");
    }

    [Fact]
    public void CandidatesFor_CustomLookup_OverridesDefault()
    {
        // Hypothetical "Greater Gargadon" card — actual values not important,
        // the probe must surface whatever the lookup returns.
        var gargadon = InHand(_alice, new Creature("Greater Gargadon", "{8}{R}", 9, 7));
        var probe = new SuspendAltCostProbe(c =>
            c.Name == "Greater Gargadon"
                ? new SuspendAltCostProbe.SuspendDescriptor(ManaCost.Parse("{R}"), 10)
                : null);

        var candidates = probe.CandidatesFor(gargadon, _alice, NewContext()).ToList();
        candidates.Should().HaveCount(1);
        ((SuspendAlternativeCost)candidates[0]).TimeCounters.Should().Be(10);
    }

    [Fact]
    public void Constructor_NullLookup_Throws()
    {
        Action act = () => new SuspendAltCostProbe(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
