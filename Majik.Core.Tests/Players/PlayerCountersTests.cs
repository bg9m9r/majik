using FluentAssertions;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Xunit;

namespace Majik.Core.Tests.Players;

/// <summary>
/// Unit tests for the player-scoped counter store (CR 122 — counters on
/// PLAYERS): poison (CR 704.5c), energy (CR 107.16), experience (CR 107.14),
/// and generic, plus the <see cref="PlayerCounterAddIntent"/> /
/// <see cref="PlayerCountersService"/> replacement seam (CR 614).
/// </summary>
public class PlayerCountersTests
{
    // ── Store: read / add / remove / remove-all ──────────────────────────

    [Fact]
    public void GetCounters_DefaultsToZero()
    {
        var p = new Player("Alice", 20);

        p.GetCounters(CounterType.Poison).Should().Be(0);
        p.GetCounters(CounterType.Energy).Should().Be(0);
        p.GetCounters(CounterType.Experience).Should().Be(0);
    }

    [Fact]
    public void AddCounters_Poison_RoutesToPoisonField()
    {
        var p = new Player("Alice", 20);

        p.AddCounters(CounterType.Poison, 3);

        p.GetCounters(CounterType.Poison).Should().Be(3);
        p.PoisonCounters.Should().Be(3, "the unified store and the poison field are one and the same");
    }

    [Fact]
    public void AddCounters_Energy_RoutesToEnergyField()
    {
        var p = new Player("Alice", 20);

        p.AddCounters(CounterType.Energy, 5);

        p.GetCounters(CounterType.Energy).Should().Be(5);
        p.EnergyCounters.Should().Be(5);
        p.PayEnergy(5).Should().BeTrue("energy added via the store is still spendable");
    }

    [Fact]
    public void AddCounters_Experience_Accumulates()
    {
        var p = new Player("Alice", 20);

        p.AddCounters(CounterType.Experience, 1);
        p.AddCounters(CounterType.Experience, 2);

        p.GetCounters(CounterType.Experience).Should().Be(3);
    }

    [Fact]
    public void AddCounters_Generic_TrackedDistinctly()
    {
        var p = new Player("Alice", 20);
        var glory = new CounterType("Glory");

        p.AddCounters(glory, 4);

        p.GetCounters(glory).Should().Be(4);
        p.GetCounters(CounterType.Poison).Should().Be(0, "types are independent");
    }

    [Fact]
    public void RemoveCounters_ClampsAtZero()
    {
        var p = new Player("Alice", 20);
        p.AddCounters(CounterType.Experience, 2);

        var removed = p.RemoveCounters(CounterType.Experience, 5);

        removed.Should().Be(2, "you can't remove counters that aren't there (CR 122.6)");
        p.GetCounters(CounterType.Experience).Should().Be(0);
    }

    [Fact]
    public void RemoveAllCounters_WipesEveryType()
    {
        var p = new Player("Alice", 20);
        p.AddCounters(CounterType.Poison, 3);
        p.AddCounters(CounterType.Energy, 4);
        p.AddCounters(CounterType.Experience, 2);
        p.AddCounters(new CounterType("Glory"), 1);

        var total = p.RemoveAllCounters();

        total.Should().Be(10);
        p.PoisonCounters.Should().Be(0);
        p.EnergyCounters.Should().Be(0);
        p.GetCounters(CounterType.Experience).Should().Be(0);
        p.AllCounters.Should().BeEmpty();
    }

    [Fact]
    public void AddPoisonCounters_NegativeThrows()
    {
        var p = new Player("Alice", 20);
        var act = () => p.AddPoisonCounters(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── Replacement seam: PlayerCounterAddIntent through the bus ─────────

    [Fact]
    public void AddPoisonCounters_RoutesThroughAttachedBus()
    {
        var p = new Player("Alice", 20);
        var bus = new ReplacementBus();
        p.AttachReplacementBus(bus);

        // A "can't get counters" lock — rewrite every player-counter intent to 0.
        bus.Register<PlayerCounterAddIntent>(new LambdaReplacement<PlayerCounterAddIntent>(
            applies: (i, _) => i.Amount > 0,
            replace: (i, _) => i with { Amount = 0 }));

        p.AddPoisonCounters(3);

        p.PoisonCounters.Should().Be(0, "the bus prevented the counter (CR 614)");
    }

    [Fact]
    public void GainEnergy_RoutesThroughAttachedBus()
    {
        var p = new Player("Alice", 20);
        var bus = new ReplacementBus();
        p.AttachReplacementBus(bus);

        bus.Register<PlayerCounterAddIntent>(new LambdaReplacement<PlayerCounterAddIntent>(
            applies: (i, _) => i.Type == CounterType.Energy,
            replace: (_, _) => null)); // cancel outright

        p.GainEnergy(2);

        p.EnergyCounters.Should().Be(0, "the energy gain was cancelled by the replacement");
    }

    [Fact]
    public void NoBus_AddsDirectly()
    {
        var p = new Player("Alice", 20);

        p.AddPoisonCounters(2);
        p.GainEnergy(3);

        p.PoisonCounters.Should().Be(2);
        p.EnergyCounters.Should().Be(3);
    }

    [Fact]
    public void PlayerCountersService_ReturnsCommittedAmount()
    {
        var p = new Player("Alice", 20);

        var placed = PlayerCountersService.Add(p, CounterType.Experience, 2, replacements: null);

        placed.Should().Be(2);
        p.GetCounters(CounterType.Experience).Should().Be(2);
    }

    [Fact]
    public void PlayerCountersService_ReturnsZeroWhenPrevented()
    {
        var p = new Player("Alice", 20);
        var bus = new ReplacementBus();
        bus.Register<PlayerCounterAddIntent>(new LambdaReplacement<PlayerCounterAddIntent>(
            applies: (_, _) => true,
            replace: (i, _) => i with { Amount = 0 }));

        var placed = PlayerCountersService.Add(p, CounterType.Poison, 5, bus);

        placed.Should().Be(0);
        p.PoisonCounters.Should().Be(0);
    }
}
