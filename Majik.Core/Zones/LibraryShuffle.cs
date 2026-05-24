using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Random;

namespace Majik.Core.Zones;

/// <summary>
/// CR 701.20 — shared entry point for tutor-family library shuffles.
///
/// Effect closures (tutor factories, search templates, etc.) call
/// <see cref="ShuffleLibrary"/> after they mutate the searching
/// player's library. The helper:
///   1. Pulls the active <see cref="GameRandom"/> from
///      <see cref="GameRandomRegistry"/> (falls back to a shared
///      default RNG when nothing is registered).
///   2. Invokes <see cref="IZone.Shuffle"/> on the player's library.
///   3. Publishes a <see cref="LibraryShuffledEvent"/> if any
///      <see cref="IEventBus"/> is registered for the player or
///      process-wide.
///
/// Centralising this here means every search call site shares the
/// same RNG + event semantics — factories don't each re-implement
/// Fisher-Yates / event emission.
/// </summary>
public static class LibraryShuffle
{
    /// <summary>
    /// Shuffle <paramref name="player"/>'s library and publish a
    /// <see cref="LibraryShuffledEvent"/> tagged with
    /// <paramref name="reason"/> (free-form, used for diagnostics /
    /// replay logs only).
    /// </summary>
    public static void ShuffleLibrary(Player player, string reason)
    {
        if (player is null) return;
        var rng = GameRandomRegistry.Get(player);
        var lib = player.Zones.Library;
        var countBefore = lib.Count;
        lib.Shuffle(rng);

        var bus = EventBusRegistry.Get(player);
        bus?.Publish(new LibraryShuffledEvent(player, reason, countBefore));
    }
}
