using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Worldly Tutor (Mirage / reprinted, {G}).
///
/// Instant. Oracle text (modern wording):
///   "Search your library for a creature card, reveal it, put it on top
///    of your library, then shuffle."
///
/// ## Why it gets its own factory
/// Worldly Tutor is the green creature-tutor counterpart of
/// <see cref="MysticalTutorFactory"/> (instant or sorcery → top of
/// library). The shared <see cref="Majik.Core.CardData.SpellTemplates.Templates.Search.SearchSpellFactory.SearchLibrarySpell"/>
/// hard-codes the picked card's destination to the caster's hand, so a
/// "top of library" tutor needs its own resolve closure. Mirrors the
/// pattern used by Mystical Tutor and <see cref="VampiricTutorFactory"/>.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {G}.
/// - On-resolve effect: ask the controller's agent (via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>) for a creature
///   card from the library; insert the pick at index 0 of the caster's
///   library AFTER the CR 701.20a shuffle, so it ends up on top of an
///   otherwise-randomized deck (matches every other Vampiric / Mystical
///   / Worldly Tutor implementation). No agent registered = the
///   deterministic first-match fallback used elsewhere in
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Search.SearchSpellFactory"/>.
///   Empty candidate list or null pick = no-op (CR 701.19a permits
///   declining to find).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>. The picked card moves Library → top-of-Library
///   without publishing a reveal event; same gap as the other search
///   factories.
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
    /// includes <see cref="CardType.Creature"/> (CR 205.3); the pick is
    /// inserted at index 0 of the caster's library after a CR 701.20a
    /// shuffle of the remaining deck.
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
                new Effect("tutor creature -> top of library", () =>
                {
                    static bool Pred(ICard c) => c.HasType(CardType.Creature);

                    var candidates = caster.Zones.Library.GetCards().Where(Pred).ToList();
                    if (candidates.Count == 0) return;

                    // Agent-driven pick with deterministic first-match
                    // fallback (mirrors MysticalTutorFactory).
                    var agent = AgentRegistry.Get(caster);
                    ICard? pick = agent != null
                        ? agent.ChooseLibraryPickAsync(
                            ctx: null,
                            candidates,
                            "creature card")
                            .GetAwaiter().GetResult()
                        : candidates[0];
                    if (pick == null) return;

                    caster.Zones.Library.RemoveCard(pick);
                    // CR 701.20a — shuffle the library AFTER the search.
                    // Sequence the shuffle BEFORE the top-of-library
                    // placement so the picked card ends up on top of a
                    // randomized library (matches Mystical / Vampiric
                    // Tutor — the printed "then shuffle" historically
                    // means shuffle the rest of the deck while preserving
                    // the just-placed card).
                    LibraryShuffle.ShuffleLibrary(caster, "worldly-tutor");
                    caster.Zones.Library.InsertCardAt(0, pick);
                    pick.SetZone(ZoneType.Library);
                }),
            });
    }
}
