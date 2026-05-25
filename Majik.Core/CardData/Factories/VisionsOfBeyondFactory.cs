using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Visions of Beyond (Magic 2012, {U}).
///
/// Instant. Oracle text:
///   "Draw a card. If a graveyard has twenty or more cards in it, draw
///    three cards instead."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>) inspects the
///   provided <see cref="IReadOnlyList{Player}"/> at resolution time and
///   draws three cards via <see cref="Fx.DrawCards"/> when ANY player's
///   graveyard contains 20 or more cards; otherwise draws a single card.
///   "Instead" (CR 614.1a) replaces the entire draw clause — there is no
///   stacking of "draw a card" + "draw three cards"; the engine chooses
///   one branch at resolution.
/// - The graveyard threshold is checked across ALL players (CR 109.4 —
///   "a graveyard" unqualified means any graveyard in the game), so the
///   opponent's milled-out yard counts the same as the controller's.
/// - Empty library on the post-replacement draw flags the standard
///   draw-from-empty-library SBA via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> inside
///   <see cref="Fx.DrawCards"/>.
///
/// ## Deferred (v1 gaps)
/// - Shape-only callers (no <c>allPlayers</c> supplied) fall back to the
///   controller's own graveyard count. The default <see cref="Create"/>
///   path attaches no resolve effect; the threshold is sampled live by
///   <see cref="BuildResolveEffect"/> at the time the effect runs, so a
///   late-arriving graveyard fill (e.g. an opponent's self-mill earlier
///   in the same turn) is observed correctly.
///
/// CR 614.1a — "instead" replacement.
/// CR 121.1 — "Draw a card" base draw step.
/// CR 109.4 — "a graveyard" applies to any player's graveyard.
/// </summary>
[CardName("Visions of Beyond")]
public static class VisionsOfBeyondFactory
{
    public const string CardName = "Visions of Beyond";
    public const string PrintedManaCost = "{U}";

    /// <summary>Threshold (inclusive) for the "instead" branch.</summary>
    public const int GraveyardThreshold = 20;

    /// <summary>Cards drawn when the threshold is met.</summary>
    public const int BigDrawCount = 3;

    /// <summary>Cards drawn when the threshold is not met.</summary>
    public const int SmallDrawCount = 1;

    /// <summary>CardDef DSL — card shape only. Draw body lives in
    /// <see cref="BuildResolveEffect"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build Visions of Beyond's resolve effect — draw 1, or 3 if any
    /// player's graveyard has at least 20 cards. <paramref name="allPlayers"/>
    /// is the live player list used to check the threshold; pass
    /// <c>null</c> (shape-only callers) to fall back to the caster's
    /// graveyard only.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        IReadOnlyList<Player>? allPlayers)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect(
                "Visions of Beyond: draw 3 if any graveyard ≥ 20, else draw 1.",
                () =>
                {
                    // CR 109.4 — "a graveyard" = any graveyard in the game.
                    // Threshold checked at resolution per CR 614.1a so a
                    // late-arriving fill (mill earlier same turn) is seen.
                    var count = MeetsThreshold(caster, allPlayers)
                        ? BigDrawCount
                        : SmallDrawCount;

                    // Route through Fx.DrawCards so a ReplacementBus
                    // (Dredge etc.) gets a shot per draw; empty-library
                    // flagging happens inside Fx.
                    Fx.DrawCards(caster, count);
                }),
        };
    }

    /// <summary>
    /// Whether the "draw three cards instead" branch fires. True iff any
    /// of the supplied players has at least <see cref="GraveyardThreshold"/>
    /// cards in their graveyard. With a null player list, only the
    /// caster's own graveyard is sampled.
    /// </summary>
    public static bool MeetsThreshold(
        Player caster,
        IReadOnlyList<Player>? allPlayers)
    {
        ArgumentNullException.ThrowIfNull(caster);

        if (allPlayers == null)
        {
            return caster.Zones.Graveyard.GetCards().Count() >= GraveyardThreshold;
        }

        foreach (var p in allPlayers)
        {
            if (p.Zones.Graveyard.GetCards().Count() >= GraveyardThreshold)
            {
                return true;
            }
        }
        return false;
    }
}
