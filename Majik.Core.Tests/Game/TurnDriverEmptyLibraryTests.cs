using FluentAssertions;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;

public class TurnDriverEmptyLibraryTests
{
    [Fact]
    public void Player_FlagSetMeansLoss_ViaSBA()
    {
        var alice = new Player("Alice", 20);
        alice.TriedToDrawFromEmptyLibrary = true;
        var sba = new StateBasedActions();
        sba.CheckStateBasedActions(new[] { alice }, Array.Empty<Majik.Core.Cards.ICard>());
        alice.HasLost.Should().BeTrue();
    }
}
