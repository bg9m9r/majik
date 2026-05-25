using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.SpellTemplates.Templates.Search;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Chord of Calling (Ravnica: City of Guilds, {X}{G}{G}{G}).
///
/// Instant. Oracle text:
///   "Flash
///    Convoke
///    Search your library for a creature card with mana value X or less,
///    put it onto the battlefield, then shuffle."
///
/// ## Implemented (v1)
///
/// - Instant shape, printed cost <c>{X}{G}{G}{G}</c>.
/// - <see cref="KeywordAbility"/> marker for Flash (CR 702.8) attached
///   inline so the <see cref="NamedCardFactory"/> dispatcher path mirrors
///   the data-driven load route.
/// - Convoke alt-cost surfaced via
///   <see cref="ConvokeAlternativeCost"/> + <see cref="BuildAlternativeCost"/>
///   (CR 702.51). The full Convoke cost-reduction flow is still v1-lossy —
///   see <see cref="ConvokeAlternativeCost"/> for the open work — but the
///   reduction primitive <see cref="ConvokeAlternativeCost.ReduceCost"/>
///   is the same one Chord of Calling will hook once the cast flow grows
///   a Convoke-aware reduction step.
/// - Resolve-time tutor: search the controller's library for a creature
///   card with mana value ≤ X, move it to the battlefield. Routed through
///   <see cref="Majik.Core.Services.ZoneService.MoveCard"/> when a live
///   <see cref="Majik.Core.Services.ZoneService"/> is supplied so the
///   reanimated permanent publishes <see cref="Majik.Core.Events.CardMovedEvent"/>
///   and ETB triggers fire (CR 603.6a — mirrors LivingEndFactory's PR
///   #165 / #174 wiring). When no <c>ZoneService</c> is supplied (the
///   single-arg test path) the move is done via direct zone mutation,
///   identical to <see cref="SearchSpellFactory.GreenSunsZenithSpell"/>.
/// - Agent prompt: tutor candidates are filtered to creatures with
///   <c>ManaCost.TotalValue ≤ X</c>, then the controller's registered
///   <see cref="IPlayerAgent"/> picks via <c>ChooseLibraryPickAsync</c>.
///   No agent registered = deterministic first-match fallback.
///   Empty candidates or null pick = no-op (CR 701.19a permits declining).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Convoke cost reduction</b>. <see cref="ConvokeAlternativeCost"/>
///   today returns the printed cost unchanged; once <c>SpellCastFlow</c>
///   grows a Convoke-aware reduction hook the chosen tapped creatures
///   will reduce the cast cost per CR 702.51b. Callers can still exercise
///   the pure-function reducer (<see cref="ConvokeAlternativeCost.ReduceCost"/>)
///   in isolation — that is what the test suite for this card uses.
/// </summary>
[CardName("Chord of Calling")]
public static class ChordOfCallingFactory
{
    public const string CardName = "Chord of Calling";
    public const string PrintedManaCost = "{X}{G}{G}{G}";

    /// <summary>
    /// Build a Chord of Calling instant owned by <paramref name="owner"/>.
    /// Card shape only — the resolve-time spell definition is built on
    /// demand via <see cref="BuildSpellDefinition"/> so the caster
    /// reference matches the player resolving the spell, and a live
    /// <see cref="Majik.Core.Services.ZoneService"/> can be threaded in.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash keyword marker. Inline attach mirrors the
        // SolitudeFactory / EnduranceFactory pattern so the dispatcher
        // path doesn't need KeywordBinder.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // CR 702.51 — Convoke keyword marker. The marker is purely
        // descriptive; the cost machinery lives on the IAlternativeCost
        // returned by BuildAlternativeCost.
        card.AddAbility(new KeywordAbility("Convoke", card, owner));

        return card;
    }

    /// <summary>
    /// Build the legacy marker-only <see cref="ConvokeAlternativeCost"/>
    /// that surfaces Convoke on this card without an attached creature
    /// selection. Returns the printed cost unchanged — useful for
    /// shape / template tests that just need a Convoke alt-cost marker.
    /// For actual per-cast reduction wire <see cref="BuildAdditionalCost"/>
    /// instead.
    /// </summary>
    public static ConvokeAlternativeCost BuildAlternativeCost() =>
        new(ManaCost.Parse(PrintedManaCost));

    /// <summary>
    /// CR 702.51 — build the Convoke additional cost for this Chord of
    /// Calling spell with the caller-selected untapped creatures. The
    /// caller threads the returned cost through
    /// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>'s
    /// <c>additionalCosts</c> parameter; the cast flow taps the chosen
    /// creatures and folds the per-tap reduction (generic OR a coloured
    /// pip matching the creature's colour, per CR 702.51b) into the mana
    /// payment. Tests + bots pre-select the creature list, mirroring the
    /// deferred agent prompt pattern used by
    /// <see cref="KappaCannoneerFactory.BuildAdditionalCost"/> for
    /// Improvise.
    /// </summary>
    public static ConvokeAdditionalCost BuildAdditionalCost(
        ICard card, IReadOnlyList<Creature> tappedCreatures) =>
        new(card, tappedCreatures);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Chord of Calling uses on
    /// resolution. <see cref="SpellDefinition.HasVariableX"/> is true so
    /// the engine prompts for X at cast time; the resolve-time effect
    /// reads <c>ChosenSpellParams.X</c> as the mana-value ceiling for the
    /// creature tutor.
    /// </summary>
    /// <param name="caster">Spell controller — the player whose library
    /// is searched and onto whose battlefield the picked creature lands.</param>
    /// <param name="zones">Optional live <see cref="Majik.Core.Services.ZoneService"/>.
    /// When supplied the Library → Battlefield move publishes a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> so ETB triggers on
    /// the tutored creature fire (CR 603.6a). When null the move is done
    /// via direct zone mutation, suitable for shape/test paths that don't
    /// need bus-driven trigger firing.</param>
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
                    new Effect($"Chord of Calling: tutor creature with mv ≤ {x} → battlefield", () =>
                    {
                        // CR 701.19a — search consults the controller's
                        // agent (if any). Pre-filter to creature cards
                        // whose printed mana value ≤ X (CR 202.3b — mana
                        // value is computed from the printed cost; the
                        // GSZ template uses the same ManaCost.Parse path).
                        var candidates = caster.Zones.Library.GetCards()
                            .Where(c =>
                                c.HasType(CardType.Creature) &&
                                ManaCost.Parse(c.ManaCost).TotalValue <= x)
                            .ToList();
                        if (candidates.Count == 0) return;

                        var agent = AgentRegistry.Get(caster);
                        ICard? pick = agent != null
                            ? agent.ChooseLibraryPickAsync(
                                ctx: null,
                                candidates: candidates,
                                kindLabel: $"creature card with mana value {x} or less")
                                .GetAwaiter().GetResult()
                            : candidates[0];
                        if (pick == null) return;

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
                            // SearchSpellFactory.GreenSunsZenithSpell. ETB
                            // triggers won't fire because no event publishes.
                            caster.Zones.Library.RemoveCard(pick);
                            caster.Zones.Battlefield.AddCard(pick);
                            pick.SetZone(ZoneType.Battlefield);
                            pick.SetController(caster);
                        }
                        // CR 701.20a — shuffle after a search effect.
                        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(caster, "chord-of-calling");
                    }),
                };
            });
    }
}
