using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Waterlogged Teachings // Inundated Archive (Modern Horizons 3, {3}{U/B}).
///
/// Instant. Oracle text (front, verified against Scryfall):
///   "Search your library for an instant card or a card with flash, reveal
///    it, put it into your hand, then shuffle."
///
/// Back face — <see cref="InundatedArchiveFactory"/> (Land — "This land
/// enters tapped."; "{T}: Add {U} or {B}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6) — real cast-either-face
///
/// This card is a Modal Double-Faced Card: the two faces share a physical
/// card but each face has its own complete characteristics (cost, type,
/// effect). At cast / play time the controller CHOOSES which face to use
/// (CR 712.3), the cost / effect of that face is what applies, and the
/// resulting stack object / permanent is the chosen face. No transform
/// happens (CR 712.4 — MDFC faces don't transform); the OTHER face simply
/// isn't there.
///
/// The front-face card built here carries an <see cref="MdfcState"/> with a
/// castable <see cref="MdfcFace"/> back-face descriptor (the back face is the
/// LAND Inundated Archive). At cast time <see cref="MdfcCastFlow"/> reads that
/// descriptor and prompts the controller to pick a face:
/// <list type="bullet">
///   <item><b>Front</b> — cast this <see cref="Instant"/> via the normal spell
///     path with {3}{U/B} and the instant/flash tutor effect.</item>
///   <item><b>Back (Inundated Archive)</b> — played as a LAND with no stack
///     (CR 305): <see cref="MdfcCastFlow"/> materializes a fresh Inundated
///     Archive land instance via
///     <see cref="InundatedArchiveFactory.Create(Player, Majik.Core.Effects.ReplacementBus?)"/>
///     (wired to the live <see cref="Majik.Core.Effects.ReplacementBus"/> so
///     its "enters tapped" ETB fires), and the front-face card is removed from
///     hand — only the chosen land enters.</item>
/// </list>
/// Mirrors <see cref="JwariDisruptionFactory"/> / <see cref="SinkIntoStuporFactory"/>
/// — the instant-front + land-back MDFC posture.
///
/// ## Implemented (v1)
/// - Instant identity at {3}{U/B} (identity + printed cost from JSON), owner /
///   controller wired.
/// - <see cref="MdfcState"/> attached (front = "Waterlogged Teachings",
///   back = "Inundated Archive") WITH a castable back-land descriptor so the
///   land face is playable via the cast-either-face flow / the bot's back-land
///   enumeration.
/// - <b>Front-face effect</b> (CR 701.19a / 701.20a) — search the caster's
///   library for an INSTANT card OR a card with FLASH, put it into the
///   caster's hand, then shuffle. The agent is prompted via
///   <see cref="LibrarySearch.PromptOnlyAsync"/> (so a human searcher always
///   sees the failed search rather than a silent no-op); the library is
///   shuffled afterward whether or not a card was found.
///
/// ## Notes
/// - The "reveal it" clause is purely informational (the card goes to the
///   searcher's own hand) — v1 doesn't model the public reveal, matching the
///   other tutors in this cycle.
/// </summary>
[CardName("Waterlogged Teachings")]
public static class WaterloggedTeachingsFactory
{
    public const string CardName = "Waterlogged Teachings";
    public const string BackName = "Inundated Archive";

    /// <summary>
    /// Construct Waterlogged Teachings as an Instant (identity from JSON) with
    /// the <see cref="MdfcState"/> face tracker attached, carrying a castable
    /// back-face land descriptor. The resolve-time tutor
    /// <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + printed cost come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("waterlogged-teachings");
        var card = (Instant)CardDefinitionFactory.Build(definition, owner);

        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor (real cast-either-face). The back face is the
        // LAND back face played with no stack; MdfcCastFlow offers the
        // controller a face choice at cast time and materializes a fresh
        // back-face land instance when chosen. No transform happens.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                InundatedArchiveFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        return card;
    }

    /// <summary>
    /// Build the resolve-time tutor <see cref="SpellDefinition"/>: search the
    /// caster's library for an INSTANT card OR a card with FLASH, put it into
    /// the caster's hand, then shuffle (CR 701.19a / 701.20a). The shape
    /// mirrors <see cref="SpellTemplates.Templates.Search.SearchSpellFactory"/>'s
    /// shared tutor body, specialised to the instant-or-flash predicate the
    /// generic kind list doesn't express.
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
                new Effect(
                    "Waterlogged Teachings — tutor instant / flash card -> hand",
                    async ctx =>
                    {
                        var candidates = caster.Zones.Library.GetCards()
                            .Where(IsInstantOrHasFlash)
                            .ToList();

                        // CR 701.19a — always prompt (PromptOnly) so a human
                        // searcher sees a failed search rather than a silent
                        // no-op; the agent picks zero or one.
                        var pick = await LibrarySearch.PromptOnlyAsync(
                            ResolutionContext.For(
                                caster,
                                ctx.Agent ?? AgentRegistry.Get(caster),
                                ctx.Game,
                                chosenTargets: null,
                                ctx.Ct),
                            caster,
                            candidates,
                            "instant card or a card with flash")
                            .ConfigureAwait(false);

                        if (pick != null)
                        {
                            caster.Zones.Library.RemoveCard(pick);
                            caster.Zones.Hand.AddCard(pick);
                            pick.SetZone(ZoneType.Hand);
                        }

                        // CR 701.20a — shuffle after the search, whether or not
                        // a card was found.
                        LibraryShuffle.ShuffleLibrary(caster, "waterlogged-teachings");
                    }),
            });
    }

    /// <summary>
    /// Predicate for the tutor: an INSTANT card (CR 205) or any card with the
    /// FLASH keyword (CR 702.8 — modelled as a <see cref="KeywordAbility"/>
    /// named "Flash"). Matches the printed "an instant card or a card with
    /// flash".
    /// </summary>
    private static bool IsInstantOrHasFlash(ICard c) =>
        c.HasType(CardType.Instant)
        || c.Abilities.OfType<KeywordAbility>().Any(k =>
            string.Equals(k.Keyword, "Flash", StringComparison.OrdinalIgnoreCase));
}
