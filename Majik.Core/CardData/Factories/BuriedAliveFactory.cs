using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Buried Alive (Odyssey, {2}{B}).
///
/// Sorcery. Oracle text:
///   "Search your library for up to three creature cards, put them into
///    your graveyard, then shuffle."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{B}.
/// - <see cref="BuildSpellDefinition"/> exposes a resolve-time tutor: up
///   to three sequential <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>
///   prompts, each filtered to creature cards still in the caster's
///   library. The agent may return <see langword="null"/> to "find
///   nothing" (CR 701.19a — search is decline-able), which short-circuits
///   the remaining picks. No agent registered → deterministic first-three
///   creatures (or fewer if the library has fewer creatures), same shape
///   as <see cref="ChordOfCallingFactory"/>'s tutor fallback.
/// - Each pick is moved library → caster's graveyard. Routes through
///   <see cref="ZoneService.MoveCard"/> when a live <see cref="ZoneService"/>
///   is supplied (graveyard arrival triggers fire — CR 603.6a covers any
///   "when a creature is put into your graveyard" wiring like Bloodghast /
///   Narcomoeba); raw-zone fallback otherwise. After the picks resolve
///   the library shuffles (CR 701.20a).
///
/// ## Deferred (v1 gaps)
/// - <b>"Up to three" as a single agent prompt</b>: v1 walks three
///   sequential single-pick prompts. A future agent-API enhancement could
///   accept a min/max count and return a list in one round-trip, but the
///   observable outcome is the same — the agent still chooses how many
///   creatures to mill (0..3) and which.
/// - <b>Cast restriction "from your library"</b>: only the caster's
///   library is scanned; no opponent-library reach (printed wording).
/// </summary>
[CardName("Buried Alive")]
public static class BuriedAliveFactory
{
    public const string CardName = "Buried Alive";
    public const string PrintedManaCost = "{2}{B}";
    public const int MaxCreatureCount = 3;

    /// <summary>Printed oracle text — cross-checked at import time
    /// against Scryfall.</summary>
    public const string OracleText =
        "Search your library for up to three creature cards, put them into your graveyard, then shuffle.";

    /// <summary>
    /// Build a Buried Alive sorcery owned by <paramref name="owner"/>.
    /// Card shape only — see <see cref="BuildSpellDefinition"/> for the
    /// resolve-time tutor.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Buried
    /// Alive. No targets; effect-only. On resolution: prompt for up to
    /// three creature cards from the caster's library, move each to the
    /// caster's graveyard, then shuffle (CR 701.19a / CR 701.20a).
    /// </summary>
    /// <param name="caster">Spell controller — the player whose library
    /// is searched and whose graveyard receives the cards.</param>
    /// <param name="zones">Optional. When supplied each library →
    /// graveyard move routes through <see cref="ZoneService.MoveCard"/>
    /// so <see cref="Majik.Core.Events.CardMovedEvent"/> publishes and
    /// graveyard-arrival triggers fire (CR 603.6a). When null the move
    /// is done via direct zone mutation.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                new Effect(
                    $"{CardName}: search library for up to {MaxCreatureCount} creature cards → graveyard, then shuffle",
                    ctx => ResolveAsync(caster, zones, ctx)),
            });
    }

    private static async ValueTask ResolveAsync(Player caster, ZoneService? zones, ResolutionContext ctx)
    {
        var effectiveZones = zones ?? ZoneServiceRegistry.Get(caster);

        // Track already-picked cards so the second / third prompt cannot
        // re-select the same physical card (the library state mutates
        // between picks once the move lands, but in the raw-zone fallback
        // we filter pre-move too for safety).
        var alreadyPicked = new HashSet<ICard>(ReferenceEqualityComparer.Instance);

        for (var i = 0; i < MaxCreatureCount; i++)
        {
            var candidates = caster.Zones.Library.GetCards()
                .Where(c => c.HasType(CardType.Creature))
                .Where(c => !alreadyPicked.Contains(c))
                .ToList();
            // CR 701.19a — on the FIRST slot always prompt the agent
            // (even with empty candidates so a human searcher SEES the
            // failed search). Subsequent slots short-circuit on empty
            // candidates since the player has already acknowledged the
            // search and there's nothing more to surface.
            if (candidates.Count == 0 && i > 0) break;

            var pick = await Majik.Core.Zones.LibrarySearch.PromptOnlyAsync(
                ctx, caster, candidates,
                $"creature card #{i + 1} of up to {MaxCreatureCount}").ConfigureAwait(false);

            // CR 701.19a — "find nothing" / decline short-circuits the
            // remaining picks (the printed "up to three" caps but doesn't
            // require finding three).
            if (pick == null) break;

            alreadyPicked.Add(pick);

            if (effectiveZones != null)
            {
                effectiveZones.MoveCard(pick, ZoneType.Library, ZoneType.Graveyard, caster);
            }
            else
            {
                caster.Zones.Library.RemoveCard(pick);
                caster.Zones.Graveyard.AddCard(pick);
                pick.SetZone(ZoneType.Graveyard);
            }
        }

        // CR 701.20a — shuffle the library after any search effect, even
        // if zero cards were found.
        LibraryShuffle.ShuffleLibrary(caster, "buried-alive");
    }
}
