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
/// Named-card factory for Grim Tutor (Starter 1999 / reprinted, {1}{B}{B}).
///
/// Sorcery. Oracle text (Scryfall, verified):
///   "Search your library for a card, put that card into your hand, then
///    shuffle. You lose 3 life."
///
/// ## Why it gets its own factory
/// Grim Tutor is the sorcery sibling of
/// <see cref="VampiricTutorFactory"/>: any card in the library is a legal
/// pick (no type filter), but the destination is the controller's HAND
/// (not the top of library, as with Vampiric / Worldly / Mystical Tutor),
/// and a flat 3-life loss fires regardless of whether a card was found
/// (CR 701.19a permits declining; the life loss is a separate instruction
/// on the resolve effect). The shared
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Search.SearchSpellFactory"/>
/// pick-to-hand path doesn't compose a post-search life-loss rider, so
/// this card hosts its own resolve closure, mirroring the pattern in
/// <see cref="VampiricTutorFactory"/> (search-to-hand + flat life loss).
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{B}{B}.
/// - On-resolve effect: ask the controller's agent (via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>) for ANY card from
///   the library; place pick into the controller's hand via
///   <see cref="IZone.AddCard"/>. No agent registered = the deterministic
///   first-match fallback used elsewhere (e.g.
///   <see cref="VampiricTutorFactory"/>). Empty library / null pick = no
///   tutor (CR 701.19a permits declining).
/// - CR 701.20a — the library is shuffled AFTER the (optional) search,
///   whether or not a card was found.
/// - Life-loss instruction: controller loses 3 life via
///   <see cref="Player.LoseLife"/> AFTER the (optional) tutor + shuffle,
///   regardless of whether a card was found. Printed order is
///   "search → hand → shuffle → lose 3 life".
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>. The picked card moves Library → Hand without
///   publishing a reveal event; same gap as the other search factories.
/// </summary>
[CardName("Grim Tutor")]
public static class GrimTutorFactory
{
    public const string CardName = "Grim Tutor";
    public const string PrintedManaCost = "{1}{B}{B}";

    /// <summary>CardDef DSL — card shape only. Tutor + life-loss body
    /// lives in <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Grim Tutor uses on
    /// resolution. No predicate (any library card is eligible). The pick
    /// is added to the controller's hand. The library is then shuffled
    /// (CR 701.20a) and the controller loses 3 life (CR 119.3),
    /// regardless of whether a card was found.
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
                new Effect("tutor any card -> hand; shuffle; lose 3 life", async ctx =>
                {
                    var candidates = caster.Zones.Library.GetCards().ToList();
                    if (candidates.Count > 0)
                    {
                        // Mirror VampiricTutorFactory: agent-driven pick
                        // with a deterministic first-match fallback. The
                        // kindLabel ("any card") is the prompt string
                        // surfaced to the agent so policies can score /
                        // filter on oracle wording.
                        var agent = ctx.Agent ?? AgentRegistry.Get(caster);
                        ICard? pick = agent != null
                            ? (await agent.ChooseLibraryPickAsync(ctx: ctx.Game,
                                candidates,
                                "any card").ConfigureAwait(false))
                            : candidates[0];

                        if (pick != null)
                        {
                            // CR 701.20a — "put that card into your hand,
                            // then shuffle." Move the pick to hand first,
                            // then shuffle the remaining library.
                            caster.Zones.Library.RemoveCard(pick);
                            caster.Zones.Hand.AddCard(pick);
                            pick.SetZone(ZoneType.Hand);
                        }
                    }

                    // CR 701.20a — the library is shuffled after the
                    // search whether or not a card was actually found.
                    Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(caster, "grim-tutor");

                    // CR 119.3 — the 3-life loss is unconditional. It
                    // fires whether or not the tutor found a card (CR
                    // 701.19a allows declining, but the life loss is a
                    // separate resolve instruction).
                    caster.LoseLife(3);
                }),
            });
    }
}
