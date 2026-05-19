using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Rules.Sba;
using Majik.Core.Rules.Sba.Checks;
using Xunit;

namespace Majik.Core.Tests.Rules;

/// <summary>
/// Locks the strategy contract introduced in Phase 2 slice 3 (SBA
/// refactor): the coordinator iterates an injectable list of
/// IStateBasedActionCheck instances, defaults to the CR 704.5 ordering,
/// and lets callers swap or extend the list.
/// </summary>
public class SbaStrategyTests
{
    [Fact]
    public void DefaultChecks_AreInRule704Order()
    {
        var sba = new StateBasedActions();

        sba.Checks.Select(c => c.Name).Should().Equal(
            "PlayerLife",
            "CounterCancellation",
            "TokensCeaseToExist",
            "AttachmentLegality",
            "BattleDestroyed",
            "SagaSacrificed",
            "SpellWithNoCard",
            "CreatureDeath",
            "PlaneswalkerDeath",
            "LegendRule",
            "PlaneswalkerUniqueness");
    }

    [Fact]
    public void CustomChecks_AreUsed_WhenSupplied()
    {
        var marker = new MarkerCheck();
        var sba = new StateBasedActions(checks: new IStateBasedActionCheck[] { marker });

        sba.CheckStateBasedActions(new[] { new Player("Alice") }, Array.Empty<ICard>());

        marker.Invocations.Should().Be(1, "the loop quiesces after one no-op pass");
        sba.Checks.Should().ContainSingle().Which.Should().BeSameAs(marker);
    }

    private sealed class MarkerCheck : IStateBasedActionCheck
    {
        public string Name => "Marker";
        public int Invocations { get; private set; }
        public bool Execute(SbaContext ctx) { Invocations++; return false; }
    }
}
