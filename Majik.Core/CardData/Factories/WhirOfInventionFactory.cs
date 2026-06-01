using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Whir of Invention (Aether Revolt, {X}{U}{U}{U}).
///
/// Instant. Oracle text (Scryfall verified):
///   "Improvise (Your artifacts can help cast this spell. Each artifact you
///    tap after you're done activating mana abilities pays for {1}.)
///    Search your library for an artifact card with mana value X or less,
///    put it onto the battlefield, then shuffle."
///
/// Structurally this is <see cref="ChordOfCallingFactory"/> with two swaps:
/// the resolve-time tutor names an <b>artifact</b> card (not a creature) and
/// the cost helper is <b>Improvise</b> (CR 702.127, the artifact analogue of
/// Convoke) instead of Convoke. Both halves reuse existing engine primitives —
/// the X-bounded library tutor (mirroring Chord of Calling) and the
/// <see cref="ImproviseAdditionalCost"/> cast-flow rail (mirroring
/// <see cref="KappaCannoneerFactory"/>) — so no new mechanic is introduced.
///
/// ## Base shape
/// Name / Instant / {X}{U}{U}{U} are materialised from the embedded JSON
/// definition (<c>whir-of-invention.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same JSON-backed posture as
/// <see cref="StaggershockFactory"/>. The resolve-time tutor lives in
/// <see cref="BuildSpellDefinition"/> because a <see cref="SpellDefinition"/>
/// needs the live caster reference + an optional
/// <see cref="Majik.Core.Services.ZoneService"/> that the data-only JSON
/// schema can't express.
///
/// ## Implemented (v1)
///
/// - <b>Instant shape</b>, printed cost <c>{X}{U}{U}{U}</c>.
/// - <b>Improvise (CR 702.127)</b> — wired identically to Kappa Cannoneer:
///   a <see cref="KeywordAbility"/>("Improvise") marker keeps the discovery
///   surface uniform (probes scan for the marker), and the working cost-side
///   primitive is surfaced via <see cref="BuildAdditionalCost"/>, which builds
///   an <see cref="ImproviseAdditionalCost"/> bound to the caller-selected
///   untapped artifacts. The cast flow's CR 601.2f additional-cost loop taps
///   the chosen artifacts and folds {1} of generic reduction per tap into the
///   mana payment (coloured pips preserved — CR 702.127). The
///   <see cref="Majik.Core.Players.Agents.ImproviseAltCostProbe"/> already
///   surfaces this on the bot-discovery rail for any Improvise card.
/// - <b>Resolve-time tutor (CR 701.19a / CR 701.20a)</b>: search the
///   controller's library for an <em>artifact</em> card with mana value ≤ X,
///   put it onto the battlefield, then shuffle. The Library → Battlefield move
///   routes through <see cref="Majik.Core.Services.ZoneService.MoveCard"/>
///   when a live service is supplied (so the tutored artifact publishes
///   <see cref="Majik.Core.Events.CardMovedEvent"/> and ETB triggers fire —
///   CR 603.6a); otherwise it falls back to
///   <see cref="Majik.Core.Services.ZoneServiceRegistry"/>, and finally to a
///   direct zone mutation (the shape/test path). Candidate filtering, the
///   agent prompt, and the post-search shuffle are identical to
///   <see cref="ChordOfCallingFactory"/> — only the type predicate differs
///   (<see cref="CardType.Artifact"/> vs Creature).
///
/// ## Deferred (v1 gaps — same as the existing Improvise card)
/// - <b>Improvise agent prompt</b>: <see cref="Majik.Core.Players.IPlayerAgent"/>
///   does not yet expose a "tap N artifacts" prompt, so the artifact selection
///   is caller-supplied (tests / bots pre-select). Same posture as Kappa
///   Cannoneer / Delve — see <see cref="ImproviseAdditionalCost"/>.
/// </summary>
[CardName("Whir of Invention")]
public static class WhirOfInventionFactory
{
    public const string CardName = "Whir of Invention";
    public const string Slug = "whir-of-invention";
    public const string PrintedManaCost = "{X}{U}{U}{U}";

    /// <summary>
    /// Build the card shape from the embedded JSON definition and attach the
    /// Improvise keyword marker (CR 702.127). The resolve-time tutor is built
    /// on demand via <see cref="BuildSpellDefinition"/> so the caster
    /// reference matches the player resolving the spell. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);

        // CR 702.127 — Improvise marker. The marker is descriptive; the
        // cost-reduction machinery lives on the ImproviseAdditionalCost
        // returned by BuildAdditionalCost. Mirrors KappaCannoneerFactory.
        card.AddAbility(new KeywordAbility("Improvise", card, owner));

        return card;
    }

    /// <summary>
    /// CR 702.127 — build the Improvise additional cost for this Whir of
    /// Invention spell with the caller-selected untapped artifacts. The caller
    /// threads the returned cost through
    /// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>'s
    /// <c>additionalCosts</c> parameter; the cast flow taps the chosen
    /// artifacts and folds {1} of generic reduction per tap into the mana
    /// payment (coloured pips preserved). Tests + bots pre-select the artifact
    /// list, mirroring <see cref="KappaCannoneerFactory.BuildAdditionalCost"/>.
    /// </summary>
    public static ImproviseAdditionalCost BuildAdditionalCost(
        ICard card, IReadOnlyList<Permanent> tappedArtifacts) =>
        new(card, tappedArtifacts);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Whir of Invention uses on
    /// resolution. <see cref="SpellDefinition.HasVariableX"/> is true so the
    /// engine prompts for X at cast time; the resolve-time effect reads
    /// <c>ChosenSpellParams.X</c> as the mana-value ceiling for the artifact
    /// tutor.
    /// </summary>
    /// <param name="caster">Spell controller — the player whose library is
    /// searched and onto whose battlefield the picked artifact lands.</param>
    /// <param name="zones">Optional live
    /// <see cref="Majik.Core.Services.ZoneService"/>. When supplied the
    /// Library → Battlefield move publishes a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> so ETB triggers on the
    /// tutored artifact fire (CR 603.6a). When null the move falls back to the
    /// <see cref="Majik.Core.Services.ZoneServiceRegistry"/> and ultimately to
    /// direct zone mutation (shape/test path).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Majik.Core.Services.ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: p =>
            {
                var x = p.X ?? 0;
                return new IEffect[]
                {
                    new Effect($"Whir of Invention: tutor artifact with mv ≤ {x} → battlefield", async ctx =>
                    {
                        // CR 701.19a — search consults the controller's agent.
                        // Pre-filter to artifact cards whose printed mana value
                        // ≤ X (CR 202.3b — mana value is computed from the
                        // printed cost). Only the type predicate differs from
                        // Chord of Calling (Artifact vs Creature).
                        var candidates = caster.Zones.Library.GetCards()
                            .Where(c =>
                                c.HasType(CardType.Artifact) &&
                                ManaCost.Parse(c.ManaCost).TotalValue <= x)
                            .ToList();

                        // CR 701.19a — prompt agent even on zero candidates so
                        // the human searcher sees the failed search.
                        var pick = await Majik.Core.Zones.LibrarySearch.PromptOnlyAsync(
                            ctx, caster, candidates,
                            $"artifact card with mana value {x} or less").ConfigureAwait(false);

                        if (pick != null)
                        {
                            // CR 603.6a — prefer the caller-supplied ZoneService;
                            // fall back to ZoneServiceRegistry so the
                            // dispatcher-driven cast flow (which calls
                            // BuildSpellDefinition without a service ref) still
                            // routes through the live ZoneService.
                            var effectiveZones = zones ?? Majik.Core.Services.ZoneServiceRegistry.Get(caster);
                            if (effectiveZones != null)
                            {
                                effectiveZones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, caster);
                            }
                            else
                            {
                                // Direct mutation fallback — same shape used by
                                // ChordOfCallingFactory. ETB triggers won't fire
                                // because no event publishes.
                                caster.Zones.Library.RemoveCard(pick);
                                caster.Zones.Battlefield.AddCard(pick);
                                pick.SetZone(ZoneType.Battlefield);
                                pick.SetController(caster);
                            }
                        }

                        // CR 701.20a — shuffle after a search effect, whether or
                        // not a card was found.
                        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(caster, "whir-of-invention");
                    }),
                };
            });
    }
}
