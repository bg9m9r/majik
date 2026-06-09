using FluentAssertions;
using Majik.Bot.Search;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Unit tests for the determinization context carried by <see cref="SimState"/>.
/// A captured state defaults to perfect-info (both <see cref="SimState.WorldSeed"/>
/// and <see cref="SimState.OpponentDecklist"/> null); <c>WithDeterminization</c> and
/// <c>WithWorldSeed</c> produce copies that opt into determinization while preserving
/// every other field (LivePlayers, SearchedSeat, ActivePlayer, TurnNumber, Phase).
/// </summary>
public class SimStateDeterminizationTests
{
    private static SimState BuildRoot(out Player active, out Player searched)
    {
        active = new Player("Active", 20);
        searched = new Player("Searched", 20);
        var players = new[] { active, searched };
        return SimState.Capture(
            livePlayers: players,
            activePlayer: active,
            turnNumber: 3,
            phase: PhaseStateType.PreCombatMain,
            searchedSeat: searched);
    }

    [Fact]
    public void Capture_DefaultsToPerfectInfo()
    {
        var root = BuildRoot(out _, out _);

        root.WorldSeed.Should().BeNull();
        root.OpponentDecklist.Should().BeNull();
    }

    [Fact]
    public void WithDeterminization_SetsContext_AndPreservesEveryOtherField()
    {
        var root = BuildRoot(out var active, out var searched);
        var deck = new[] { "Lightning Bolt", "Mountain", "Goblin Guide" };

        var det = root.WithDeterminization(deck, 5);

        det.WorldSeed.Should().Be(5);
        det.OpponentDecklist.Should().BeSameAs(deck);

        // Every other field preserved.
        det.SearchedSeat.Should().BeSameAs(searched);
        det.ActivePlayer.Should().BeSameAs(active);
        det.TurnNumber.Should().Be(root.TurnNumber);
        det.Phase.Should().Be(root.Phase);
        det.LivePlayers.Should().BeSameAs(root.LivePlayers);
    }

    [Fact]
    public void WithWorldSeed_PreservesDecklist_AndSetsSeed()
    {
        var root = BuildRoot(out var active, out var searched);
        var deck = new[] { "Lightning Bolt", "Mountain" };
        var det = root.WithDeterminization(deck, 5);

        var reseeded = det.WithWorldSeed(9);

        reseeded.WorldSeed.Should().Be(9);
        reseeded.OpponentDecklist.Should().BeSameAs(deck);

        // The second hop must NOT drop any other field either (same drop-a-field
        // risk as the first hop — guards the K-world reseed path).
        reseeded.ActivePlayer.Should().BeSameAs(active);
        reseeded.TurnNumber.Should().Be(root.TurnNumber);
        reseeded.Phase.Should().Be(root.Phase);
        reseeded.LivePlayers.Should().BeSameAs(root.LivePlayers);
        reseeded.SearchedSeat.Should().BeSameAs(searched);
    }
}
