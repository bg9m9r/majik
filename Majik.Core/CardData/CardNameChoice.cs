using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData;

/// <summary>
/// CR 614.12 / CR 201.4 — shared resolver for an "as this enters / as you cast
/// this, choose a card name" decision (Meddling Mage, Pithing Needle, Sorcerous
/// Spyglass, Sanctum Prelate, The Stone Brain, Cavern of Souls, Phyrexian
/// Revoker, …). Pays down the <c>choose-card-name-agent-surface</c> v1 deferral:
/// before this, the name-choosing cards relied on a test-only multi-arg factory
/// overload that handed in the chosen name directly, and the production
/// single-arg build attached the static structurally with NO live name (the
/// restriction was inert).
///
/// <para>
/// This helper closes that gap by routing the choice through the controller's
/// <see cref="IPlayerAgent.ChooseCardNameAsync"/> surface (added alongside the
/// existing <c>ChooseColorAsync</c> / <c>ChooseModeAsync</c> sinks), the same
/// way <see cref="ColorChoice"/> + <c>ChooseColorReplacement</c> thread a chosen
/// colour. It surveys the chooser's known information (the names of cards
/// currently visible on opponents' battlefields and the stack — the "known
/// threats" a hate piece wants to shut off), hands that ranked pool to the
/// agent as a SUGGESTION (CR 201.4 — the agent may still name any card), and
/// returns the chosen name.
/// </para>
/// </summary>
public static class CardNameChoice
{
    /// <summary>
    /// Default human-readable constraint label — most name-choosing cards say
    /// "choose a card name" with no restriction.
    /// </summary>
    public const string AnyCardNameLabel = "a card name";

    /// <summary>Meddling Mage's printed restriction (CR 201.4 — "nonland card name").</summary>
    public const string NonlandCardNameLabel = "a nonland card name";

    /// <summary>
    /// CR 201.4 — build the ranked SUGGESTION pool of card names for
    /// <paramref name="chooser"/> from the live game: the names of cards
    /// currently visible to the chooser on OPPONENTS' battlefields and on the
    /// stack, most-threatening-first (highest mana value first), de-duplicated.
    /// This is the "known threats" set — exactly what a name-a-card hate piece
    /// wants to turn off. The pool is advisory, never a legality restriction
    /// (the chooser may name any card).
    /// <para>
    /// When <paramref name="nonlandOnly"/> is true (Meddling Mage), land card
    /// names are filtered out so the suggestion respects the printed "nonland"
    /// rider. Returns an empty list when the game is null or no opposing cards
    /// are visible.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> SuggestNames(
        GameContext? game, Player chooser, bool nonlandOnly = false)
    {
        if (game is null || chooser is null) return Array.Empty<string>();

        // Rank by mana value descending (most threatening first), then name for
        // a stable tiebreak, de-duped by name (one suggestion per distinct card).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ranked = new List<(string Name, int Mv)>();

        void Consider(ICard card)
        {
            if (card is null) return;
            var name = card.Name;
            if (string.IsNullOrEmpty(name)) return;
            if (nonlandOnly && card.HasType(Majik.Core.Cards.Types.CardType.Land)) return;
            if (!seen.Add(name)) return;
            var mv = SafeManaValue(card);
            ranked.Add((name, mv));
        }

        // Opponents' visible permanents (battlefield is public, CR 400.2).
        foreach (var opp in game.Opponents)
        {
            if (ReferenceEquals(opp, chooser)) continue;
            foreach (var card in opp.Zones.Battlefield.GetCards())
            {
                Consider(card);
            }
        }

        return ranked
            .OrderByDescending(t => t.Mv)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => t.Name)
            .ToList();
    }

    private static int SafeManaValue(ICard card)
    {
        // CR 202.3 — mana value of the card; use the already-parsed cost when
        // present (Card.ManaCostValue), else parse the raw cost string, else 0
        // (cards with no mana cost — lands).
        if (card is Card c && c.ManaCostValue is { } mv) return mv.TotalValue;
        if (string.IsNullOrEmpty(card.ManaCost)) return 0;
        try { return Majik.Core.ValueObjects.ManaCost.Parse(card.ManaCost).TotalValue; }
        catch { return 0; }
    }

    /// <summary>
    /// CR 601.2c / CR 614.12 — prompt <paramref name="chooser"/>'s agent to name
    /// a card. Resolves the agent off <paramref name="game"/> (via
    /// <see cref="AgentRegistry"/>), builds the ranked suggestion pool
    /// (<see cref="SuggestNames"/>), and awaits
    /// <see cref="IPlayerAgent.ChooseCardNameAsync"/>. Returns the chosen name,
    /// or <see cref="string.Empty"/> when no agent is available (the shape-only
    /// path — the static stays inert, exactly the pre-surface posture). Land
    /// names are excluded from the suggestion pool when
    /// <paramref name="nonlandOnly"/> is set (Meddling Mage).
    /// </summary>
    public static async Task<string> ChooseAsync(
        GameContext? game,
        Player chooser,
        string constraintLabel,
        bool nonlandOnly = false,
        CancellationToken ct = default)
    {
        if (chooser is null) return string.Empty;
        var agent = AgentRegistry.Get(chooser);
        if (agent is null) return string.Empty;

        var suggested = SuggestNames(game, chooser, nonlandOnly);
        var name = await agent
            .ChooseCardNameAsync(game, suggested, constraintLabel, fallback: string.Empty, ct)
            .ConfigureAwait(false);
        return name ?? string.Empty;
    }

    /// <summary>
    /// Synchronous bridge over <see cref="ChooseAsync"/> for the v1 sync
    /// selector model used by the printed-static lifecycle effects
    /// (PithingNeedleStaticEffect / MeddlingMageCastRestrictionEffect attach a
    /// <c>Func&lt;Player, string&gt;</c> at resolution time). Mirrors the
    /// established <c>.GetAwaiter().GetResult()</c> bridge other hand-disruption
    /// factories use (Thoughtseize / Despise / Mind Rot). The bot / scripted
    /// agents resolve synchronously, so this never blocks for them; a remote
    /// (human) agent's name choice rides the async surface in a follow-up (same
    /// posture as <c>ChooseColorReplacement</c>'s sync path keeping the seeded
    /// default).
    /// </summary>
    public static string ChooseSync(
        GameContext? game,
        Player chooser,
        string constraintLabel,
        bool nonlandOnly = false)
        => ChooseAsync(game, chooser, constraintLabel, nonlandOnly).GetAwaiter().GetResult();
}
