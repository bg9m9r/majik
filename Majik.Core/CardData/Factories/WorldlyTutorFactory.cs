using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Worldly Tutor (Mirage, {G}).
///
/// Instant. Oracle text:
///   "Search your library for a creature card, reveal that card, then
///    shuffle and put that card on top of your library."
///
/// ## Why it gets its own factory
/// The "creature card → top of library" shape doesn't match
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Search.SearchSpellFactory.SearchLibrarySpell"/>,
/// which hard-codes the pick's destination as the controller's hand
/// (Eladamri's Call / Diabolic Tutor shape). Worldly Tutor delivers the
/// picked card to the top of the library instead — the green sibling of
/// Mystical Tutor / Vampiric Tutor. The resolve body therefore mirrors
/// <see cref="MysticalTutorFactory"/> with a creature-only predicate.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {G}.
/// - On-resolve effect: pre-filters the controller's library to
///   <see cref="CardType.Creature"/> cards (CR 205.3) and prompts the
///   controller's agent (via <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>)
///   for a pick. No agent registered = the deterministic first-match
///   fallback the rest of the search factories use. Empty candidate list
///   or null pick = no-op (CR 701.19a permits declining to find).
/// - CR 701.20a — shuffle is run BEFORE the picked card is placed at
///   index 0, matching the engine-wide convention so the picked card
///   ends up on top of an otherwise-randomized library (same sequencing
///   Mystical Tutor / Vampiric Tutor use).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>. The picked card moves Library → top-of-Library
///   without publishing a reveal event; same gap as
///   <see cref="MysticalTutorFactory"/> and the other search factories.
/// </summary>
[CardName("Worldly Tutor")]
public static class WorldlyTutorFactory
{
    public const string CardName = "Worldly Tutor";
    public const string PrintedManaCost = "{G}";

    /// <summary>CardDef DSL — card shape only. Tutor SpellDefinition is
    /// built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Worldly Tutor uses on
    /// resolution. The predicate accepts any library card whose type set
    /// includes <see cref="CardType.Creature"/> (CR 205.3). The pick is
    /// inserted at index 0 of the controller's library — the canonical
    /// "top of library" position read by <see cref="DrawAction"/> and
    /// friends.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                new Effect("tutor creature -> top of library", async ctx =>
                {
                    static bool Pred(ICard c) => c.HasType(CardType.Creature);

                    var candidates = caster.Zones.Library.GetCards().Where(Pred).ToList();
                    if (candidates.Count == 0) return;

                    // Mirror MysticalTutorFactory: agent-driven pick with a
                    // deterministic first-match fallback. The kindLabel is
                    // the prompt string surfaced to the agent so
                    // LibraryPickPolicy can score by oracle wording.
                    var agent = ctx.Agent ?? AgentRegistry.Get(caster);
                    ICard? pick = agent != null
                        ? (await agent.ChooseLibraryPickAsync( ctx: ctx.Game,
                            candidates,
                            "creature card").ConfigureAwait(false))
                        : candidates[0];
                    if (pick == null) return;

                    caster.Zones.Library.RemoveCard(pick);
                    // CR 701.20a — shuffle the library AFTER the search.
                    // Sequence the shuffle BEFORE the top-of-library
                    // placement so the picked card ends up on top of a
                    // randomized library (matches Mystical / Vampiric
                    // Tutor; the printed oracle "then shuffle and put
                    // that card on top of your library" is implemented
                    // as shuffle-the-rest + place-on-top, per the
                    // canonical engine convention used elsewhere).
                    Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(caster, "worldly-tutor");
                    caster.Zones.Library.InsertCardAt(0, pick);
                    pick.SetZone(ZoneType.Library);
                }),
            });
    }
}
