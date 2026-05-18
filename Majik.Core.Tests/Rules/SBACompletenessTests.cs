using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Xunit;

public class SBACompletenessTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly StateBasedActions _sba;

    public SBACompletenessTests()
    {
        _zones = new ZoneService(_bus);
        _sba = new StateBasedActions(_bus, _zones);
    }

    [Fact]
    public void EmptyLibrary_DrawAttempt_PlayerLoses()
    {
        var alice = new Player("Alice", 20);
        alice.TriedToDrawFromEmptyLibrary = true;

        _sba.CheckStateBasedActions(new[] { alice }, System.Array.Empty<ICard>());

        alice.HasLost.Should().BeTrue();
    }

    [Fact]
    public void TenPoison_PlayerLoses()
    {
        var alice = new Player("Alice", 20) { PoisonCounters = 10 };

        _sba.CheckStateBasedActions(new[] { alice }, System.Array.Empty<ICard>());

        alice.HasLost.Should().BeTrue();
    }

    [Fact]
    public void NinePoison_PlayerLives()
    {
        var alice = new Player("Alice", 20) { PoisonCounters = 9 };

        _sba.CheckStateBasedActions(new[] { alice }, System.Array.Empty<ICard>());

        alice.HasLost.Should().BeFalse();
    }
}
