using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Steelshaper's Gift (Darksteel, {W}).
///
/// Sorcery. Oracle text (verified against the printed card / Scryfall;
/// unchanged since Darksteel 2004):
///   "Search your library for an Equipment card, reveal that card, put it
///    into your hand, then shuffle."
///
/// ## Why it gets its own factory (vs. pure template binding)
/// This is the white Equipment analogue of <see cref="EladamrisCallFactory"/>
/// (creature → hand) and Diabolic-Tutor-shape spells. The shared
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Search.SearchSpellFactory.SearchLibrarySpell"/>
/// helper only understands top-level card-TYPE predicates ("creature",
/// "artifact", …) — it has no "Equipment" branch, because Equipment is an
/// artifact SUBTYPE (CR 205.3g), not a card type. Rather than widen that
/// shared helper, the resolve body here is written inline with an
/// <see cref="CardSubtype.Equipment"/> predicate — the same subtype filter
/// <see cref="StoneforgeMysticFactory"/> uses for its ETB Equipment tutor —
/// while reusing the engine-standard prompt/move/shuffle primitives so the
/// agent prompt, decline semantics, and post-search shuffle event match
/// every other library tutor.
///
/// The base card shape (name / Sorcery type / {W} cost) is materialised from
/// the embedded JSON definition (<c>steelshapers-gift.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="ArdentPleaFactory"/>); the search SpellDefinition is layered
/// on via <see cref="BuildSpellDefinition"/> because the JSON
/// <c>AbilityDefinition</c> schema does not yet express a library search.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {W}.
/// - On-resolve effect: pre-filters the controller's library to cards whose
///   subtypes include <see cref="CardSubtype.Equipment"/> (CR 205.3g), then
///   prompts the controller's agent (via
///   <see cref="LibrarySearch.PromptOnlyAsync"/>) for a pick; the picked
///   card is moved Library → Hand. No agent registered = the deterministic
///   first-match fallback the rest of the search factories use. Null pick =
///   no-op (CR 701.19a permits declining to find).
/// - CR 701.20a — the library is shuffled after the search via the shared
///   <see cref="LibraryShuffle"/> helper (publishes a
///   <c>LibraryShuffledEvent</c> when an EventBus is registered), whether or
///   not a card was actually found.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>. The picked Equipment moves Library → Hand without
///   publishing a reveal event; same gap as
///   <see cref="EladamrisCallFactory"/> / <see cref="StoneforgeMysticFactory"/>
///   and the other library-tutor factories.
/// </summary>
[CardName("Steelshaper's Gift")]
public static class SteelshapersGiftFactory
{
    public const string CardName = "Steelshaper's Gift";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "steelshapers-gift";

    /// <summary>
    /// Dispatcher path (used by <see cref="NamedCardFactory"/>). Materialises
    /// the Sorcery card shape from the embedded JSON. The resolve-time tutor
    /// body is built via <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Sorcery card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Sorcery but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Steelshaper's Gift uses on
    /// resolution. The predicate accepts any library card whose subtype set
    /// includes <see cref="CardSubtype.Equipment"/> (CR 205.3g). The pick is
    /// moved to the controller's hand; the library is shuffled afterward
    /// (CR 701.20a).
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: p => new IEffect[]
            {
                new Effect("tutor Equipment -> hand", async ctx =>
                {
                    // CR 205.3g — "Equipment" is an artifact SUBTYPE; match by
                    // subtype, not card type (mirrors Stoneforge Mystic).
                    static bool Pred(ICard c) => c.HasSubtype(CardSubtype.Equipment);

                    var candidates = caster.Zones.Library.GetCards().Where(Pred).ToList();

                    // CR 701.19a — prompt the agent (even on an empty candidate
                    // list, so a human searcher SEES the failed search rather
                    // than a silent no-op). Returning null = decline to find,
                    // which is legal. No agent registered = deterministic
                    // first-match fallback (shape / dispatcher test path).
                    var pick = await LibrarySearch.PromptOnlyAsync(
                        ResolutionContext.For(
                            caster, ctx.Agent ?? AgentRegistry.Get(caster),
                            game: ctx.Game, chosenTargets: null, ctx.Ct),
                        caster, candidates, "Equipment card")
                        .ConfigureAwait(false);
                    if (pick != null)
                    {
                        caster.Zones.Library.RemoveCard(pick);
                        caster.Zones.Hand.AddCard(pick);
                        pick.SetZone(ZoneType.Hand);
                    }

                    // CR 701.20a — shuffle after the search effect, regardless
                    // of whether a card was actually found.
                    LibraryShuffle.ShuffleLibrary(caster, "steelshapers-gift");
                }),
            });
    }
}
