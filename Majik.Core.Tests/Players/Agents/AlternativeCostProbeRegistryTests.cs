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
/// Unit tests for <see cref="AlternativeCostProbeRegistry"/> — composite
/// fan-out of <see cref="IAlternativeCostProbe"/> probes plus the default
/// ship-list (Pitch / Delve / Overload / Cascade).
///
/// The per-mechanic probe tests live in sibling files:
/// <see cref="PitchAltCostProbeTests"/>, <see cref="DelveAltCostProbeTests"/>,
/// <see cref="OverloadAltCostProbeTests"/>, <see cref="CascadeAltCostProbeTests"/>.
/// </summary>
public class AlternativeCostProbeRegistryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Majik.Core.Stack.Stack _stack;

    public AlternativeCostProbeRegistryTests()
    {
        _stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
    }

    [Fact]
    public void CreateDefault_HasCoreProbes()
    {
        var registry = AlternativeCostProbeRegistry.CreateDefault();

        // Pitch + Delve + Overload + Cascade + Energy + Escape + Kicker + Suspend + Improvise + Convoke
        // (CR 118.9 + CR 106.13 + CR 702.138 + CR 702.33 + CR 702.62 + CR 702.127 + CR 702.51).
        registry.Probes.Should().HaveCount(10);
        registry.Probes.Should().ContainSingle(p => p is PitchAltCostProbe);
        registry.Probes.Should().ContainSingle(p => p is DelveAltCostProbe);
        registry.Probes.Should().ContainSingle(p => p is OverloadAltCostProbe);
        registry.Probes.Should().ContainSingle(p => p is CascadeAltCostProbe);
        registry.Probes.Should().ContainSingle(p => p is EnergyAltCostProbe);
        registry.Probes.Should().ContainSingle(p => p is EscapeAltCostProbe);
        registry.Probes.Should().ContainSingle(p => p is KickerAltCostProbe);
        registry.Probes.Should().ContainSingle(p => p is SuspendAltCostProbe);
        registry.Probes.Should().ContainSingle(p => p is ImproviseAltCostProbe);
        registry.Probes.Should().ContainSingle(p => p is ConvokeAltCostProbe);
    }

    [Fact]
    public void Register_NewProbe_NextEnumerationSeesIt()
    {
        var registry = new AlternativeCostProbeRegistry();
        var stubProbe = new StubProbe(c => c.Name == "Stub Card");
        registry.Register(stubProbe);

        var card = InHand(_alice, new Instant("Stub Card", "{U}"));
        var ctx = NewContext(activePlayer: _bob);

        var candidates = registry.CandidatesFor(card, _alice, ctx).ToList();
        candidates.Should().HaveCount(1);
        candidates[0].Description.Should().Contain("stub");
    }

    [Fact]
    public void Register_NullProbe_Throws()
    {
        var registry = new AlternativeCostProbeRegistry();
        Action act = () => registry.Register(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CandidatesFor_FansAcrossAllProbes()
    {
        var registry = new AlternativeCostProbeRegistry()
            .Register(new StubProbe(c => c.Name == "A", "alpha"))
            .Register(new StubProbe(c => c.Name == "A", "beta"));

        var card = InHand(_alice, new Instant("A", "{U}"));
        var ctx = NewContext(activePlayer: _bob);

        var candidates = registry.CandidatesFor(card, _alice, ctx).ToList();
        candidates.Should().HaveCount(2);
        candidates.Select(c => c.Description).Should().BeEquivalentTo(
            new[] { "stub-alpha", "stub-beta" });
    }

    [Fact]
    public void CreateDefault_PitchProbe_EmitsForceOfWillCandidate()
    {
        // Smoke test that the default registry actually wires Pitch.
        var registry = AlternativeCostProbeRegistry.CreateDefault();
        var fow = InHand(_alice, new Instant("Force of Will", "{3}{U}{U}"));
        InHand(_alice, new Instant("Brainstorm", "{U}"));
        var ctx = NewContext(activePlayer: _bob);

        var candidates = registry.CandidatesFor(fow, _alice, ctx).ToList();
        candidates.Should().ContainSingle(c => c is PitchAlternativeCost);
    }

    [Fact]
    public void CreateDefault_OverloadProbe_EmitsMizziumMortarsCandidate()
    {
        var registry = AlternativeCostProbeRegistry.CreateDefault();
        var mortars = InHand(_alice, MizziumMortarsFactory.Create(_alice));
        var ctx = NewContext(activePlayer: _alice);

        var candidates = registry.CandidatesFor(mortars, _alice, ctx).ToList();
        candidates.Should().ContainSingle(c => c is OverloadAlternativeCost);
    }

    [Fact]
    public void CreateDefault_DelveProbe_EmitsTreasureCruiseCandidate()
    {
        var registry = AlternativeCostProbeRegistry.CreateDefault();
        var cruise = InHand(_alice, TreasureCruiseFactory.Create(_alice));
        // Fuel the delve with a card in the graveyard.
        var fodder = new Instant("Brainstorm", "{U}");
        fodder.SetOwner(_alice);
        fodder.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(fodder);

        var ctx = NewContext(activePlayer: _alice);

        var candidates = registry.CandidatesFor(cruise, _alice, ctx).ToList();
        candidates.Should().ContainSingle(c => c is DelveAlternativeCost);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static T InHand<T>(Player owner, T card) where T : Card
    {
        card.SetOwner(owner);
        card.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(card);
        return card;
    }

    private GameContext NewContext(Player activePlayer) =>
        new(_alice, new[] { _alice, _bob }, activePlayer, 1, PhaseStateType.Main, _stack);

    private sealed class StubProbe : IAlternativeCostProbe
    {
        private readonly Func<ICard, bool> _match;
        private readonly string _tag;

        public StubProbe(Func<ICard, bool> match, string tag = "value")
        {
            _match = match;
            _tag = tag;
        }

        public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
        {
            if (_match(card)) yield return new StubCost(_tag);
        }

        private sealed class StubCost : IAlternativeCost
        {
            public string Description { get; }
            public ManaCost AlternativeManaCost => ManaCost.Zero;
            public StubCost(string tag) { Description = $"stub-{tag}"; }
            public bool CanCastFor(ICard card, Player caster) => true;
            public void OnResolved(ICard card, Player caster) { }
        }
    }
}
