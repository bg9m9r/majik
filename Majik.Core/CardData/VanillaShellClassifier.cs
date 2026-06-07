using Majik.Core.Cards;

namespace Majik.Core.CardData;

/// <summary>
/// Shared "vanilla shell" classifier — used by BOTH card-build paths so the
/// <see cref="ICard.IsVanillaShell"/> flag is set consistently:
/// <list type="bullet">
///   <item><see cref="ScryfallCardFactory.Create"/> (binder-chain build with no
///   live services), and</item>
///   <item><c>Majik.Core.Api.GameFacade.BuildDeckCard</c> (the real deck-build
///   path, full binder set + live services).</item>
/// </list>
/// A card is a vanilla shell when the engine produced something that LOOKS like
/// the printed card (right name, cost, P/T, types) but does NOT enforce its
/// printed rules — so the bot's graceful-degrade path can deprioritise it and
/// emit a one-shot warning. The body reads only <paramref name="card"/> and
/// <paramref name="entity"/> — no instance state — so it is a pure static check.
/// </summary>
public static class VanillaShellClassifier
{
    /// <summary>
    /// Inspect the built card + its source row and decide whether it's a
    /// "vanilla shell" — see <see cref="ICard.IsVanillaShell"/>. The check
    /// is split by card kind:
    /// <list type="bullet">
    ///   <item>Instants/sorceries are NEVER flagged here — the compiled
    ///   spell-template cache was removed, so this classifier no longer
    ///   inspects them; the live resolver clears the flag at cast time when a
    ///   template walk binds.</item>
    ///   <item>Permanents are flagged only when they have NO abilities AND
    ///   have non-empty oracle text (printed rules the engine isn't
    ///   enforcing). True vanilla bodies (empty oracle text) are not flagged.</item>
    /// </list>
    /// </summary>
    public static bool IsLikelyVanillaShell(ICard card, CardEntity entity)
    {
        var oracle = entity.OracleText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(oracle))
        {
            // True vanilla creature / basic land — no printed rules text to
            // enforce. The engine plays these correctly as plain bodies, so
            // they are NOT vanilla shells from the bot's perspective.
            return false;
        }

        var isInstantOrSorcery =
            card.HasType(Majik.Core.Cards.Types.CardType.Instant)
            || card.HasType(Majik.Core.Cards.Types.CardType.Sorcery);

        if (isInstantOrSorcery)
        {
            // Compiled spell-template cache was removed when the SQLite
            // backing store was deleted. The bot now relies on the
            // resolver to clear the vanilla-shell flag when a live
            // template walk binds (see ClearVanillaShellOnSpellBind on
            // the production TurnDriver path). Default to NOT tagging
            // instants/sorceries as vanilla shells — the live walk
            // covers them at cast time.
            return false;
        }

        // Permanent path: has at least one ability → engine covers it.
        // The previous "keyword-only oracle text" fast path lived in
        // CoverageClassifier and consumed the Scryfall `keywords` JSON
        // array — that array is not carried by the embedded seed, so
        // tagging on oracle-text emptiness alone would over-flag cards
        // whose abilities are bound entirely from keywords. Be
        // conservative and only flag when there are no abilities AND no
        // oracle text at all (vanilla creatures / lands).
        var hasAnyAbility = card.Abilities.Count > 0;
        if (hasAnyAbility) return false;
        return !string.IsNullOrWhiteSpace(entity.OracleText);
    }
}
