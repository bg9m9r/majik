using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.SpellTemplates.Templates.Search;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mystical Tutor (Mirage / reprinted, {U}).
///
/// Instant. Oracle text (modern wording):
///   "Search your library for an instant or sorcery card, reveal it, put
///    it on top of your library, then shuffle."
///
/// ## Why it gets its own factory
/// The instant-or-sorcery predicate is a small disjunction over the
/// existing kind-predicate set in <see cref="SearchSpellFactory"/>, and
/// the final destination is the top of the library (index 0) rather than
/// the hand (which the shared <see cref="SearchSpellFactory.SearchLibrarySpell"/>
/// hard-codes). Rather than thread a "destination" enum through that
/// shared closure for a single card, this factory hosts the bespoke
/// resolve effect directly, mirroring the pattern used by
/// <see cref="SylvanScryingFactory"/> and the Brainstorm template.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}.
/// - On-resolve effect: ask the controller's agent (via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>) for an instant or
///   sorcery card from the library; place pick on top of library (index
///   0) via <see cref="IZone.InsertCardAt"/>. No agent registered = the
///   deterministic first-match fallback used elsewhere in
///   <see cref="SearchSpellFactory"/>. Empty candidate list or null pick
///   = no-op (CR 701.19a permits declining to find).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>. The picked card moves Library → top-of-Library
///   without publishing a reveal event; same gap as the other search
///   factories.
/// </summary>
[CardName("Mystical Tutor")]
public static class MysticalTutorFactory
{
    public const string CardName = "Mystical Tutor";
    public const string PrintedManaCost = "{U}";

    /// <summary>CardDef DSL — card shape only. Tutor SpellDefinition is
    /// built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Mystical Tutor uses on
    /// resolution. The predicate accepts any library card whose type set
    /// includes <see cref="CardType.Instant"/> or
    /// <see cref="CardType.Sorcery"/> (CR 205.3). The pick is inserted
    /// at index 0 of the controller's library — the canonical "top of
    /// library" position read by <see cref="DrawAction"/> and friends.
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
                new Effect("tutor instant/sorcery -> top of library", () =>
                {
                    static bool Pred(ICard c) =>
                        c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery);

                    var candidates = caster.Zones.Library.GetCards().Where(Pred).ToList();
                    if (candidates.Count == 0) return;

                    // Mirror SearchSpellFactory: agent-driven pick with a
                    // deterministic first-match fallback. The kindLabel is
                    // the prompt string surfaced to the agent so policies
                    // can score / filter by oracle wording.
                    var agent = AgentRegistry.Get(caster);
                    ICard? pick = agent != null
                        ? agent.ChooseLibraryPickAsync(
                            ctx: null,
                            candidates,
                            "instant or sorcery card")
                            .GetAwaiter().GetResult()
                        : candidates[0];
                    if (pick == null) return;

                    caster.Zones.Library.RemoveCard(pick);
                    // CR 701.20a — shuffle the library AFTER the search.
                    // Sequence the shuffle BEFORE the top-of-library
                    // placement so the picked card ends up on top of a
                    // randomized library (matches every other Vampiric /
                    // Mystical Tutor implementation; the printed oracle
                    // "then shuffle" historically means shuffle the rest
                    // of the deck while preserving the just-placed card —
                    // see ruling on Vampiric Tutor / CR 701.20).
                    Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(caster, "mystical-tutor");
                    caster.Zones.Library.InsertCardAt(0, pick);
                    pick.SetZone(ZoneType.Library);
                }),
            });
    }
}
