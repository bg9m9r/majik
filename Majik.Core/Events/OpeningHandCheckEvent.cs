using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Events;

/// <summary>
/// CR 103.5 — opening-hand check. Fired by <see cref="Majik.Core.Game.GameDriver"/>
/// once per player at game start AFTER the initial draw + mulligan
/// resolution but BEFORE the first turn begins. Carries the player and
/// a snapshot of the post-mulligan hand so subscribers can iterate
/// without re-fetching the live <see cref="Player.Zones"/> mid-loop
/// (a subscriber may move a card out of hand → battlefield mid-iteration
/// for Leyline keyword's alt-cost).
///
/// ## Wiring
///
/// Used today by:
/// - <see cref="Majik.Core.Game.OpeningHandLeylineAlternativeCost"/>
///   (Leyline cycle, CR 702.95) — prompts the player whether to begin
///   the game with each <c>KeywordAbility("OpeningHandLeyline")</c>-tagged
///   card in their opening hand on the battlefield.
///
/// Future subscribers (deferred):
/// - Gemstone Caverns (CR 702.95 sibling — "begin with this on the
///   battlefield" rider on a Land with the additional "if you weren't
///   the starting player" gate + luck-counter rider).
/// - Chancellor cycle (Modern Horizons / New Phyrexia — reveal-from-hand
///   start-of-game triggers).
/// - Power Nine "alt-cost" surfaces (not in scope but the hook is the
///   same).
///
/// ## CR citation
/// CR 103.5 — "If any cards in any player's opening hand allow actions
/// to be taken with them from a player's opening hand, any player who
/// wants to take such actions does so in turn order, starting with the
/// starting player. Then mulligans are resolved." The mulligan-loop
/// here resolves BEFORE the event fires, matching the engine's London-
/// mulligan order (mulligan resolves first, then the check).
/// </summary>
public sealed class OpeningHandCheckEvent : GameEvent
{
    /// <summary>The player whose opening hand is being checked.</summary>
    public Player Player { get; }

    /// <summary>Snapshot of the player's post-mulligan hand. The list is
    /// independent of <see cref="Player.Zones"/> so subscribers can move
    /// cards out of hand mid-iteration without invalidating their
    /// enumerator.</summary>
    public IReadOnlyList<ICard> OpeningHand { get; }

    public OpeningHandCheckEvent(Player player, IReadOnlyList<ICard> openingHand)
        : base(EventType.OpeningHandCheck)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
        OpeningHand = openingHand ?? throw new ArgumentNullException(nameof(openingHand));
    }
}
