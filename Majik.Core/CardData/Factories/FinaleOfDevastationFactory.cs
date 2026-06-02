using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Finale of Devastation (War of the Spark,
/// {X}{G}{G}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-02):
///   "Search your library and/or graveyard for a creature card with mana
///    value X or less and put it onto the battlefield. If you search your
///    library this way, shuffle. If X is 10 or more, creatures you control
///    get +X/+X and gain haste until end of turn."
///
/// Pairs two analogue shapes already in the engine:
/// - <see cref="GreenSunsZenithFactory"/> / <see cref="GenesisWaveFactory"/>
///   — variable-X tutor idiom (<see cref="SpellDefinition.HasVariableX"/> =
///   true; the resolve closure reads <c>ChosenSpellParams.X</c> as the
///   mana-value ceiling) with library manipulation routed through
///   <see cref="LibrarySearch"/> + <see cref="LibraryShuffle"/>.
/// - <see cref="CraterhoofBehemothFactory"/> — the "creatures you control get
///   +X/+X and gain &lt;keyword&gt; until end of turn" anthem rider, applied
///   here via <see cref="PumpUntilEndOfTurnEffect"/> (Layer 7c) +
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/>("Haste", Layer 6).
///
/// The base card shape (name / Sorcery / {X}{G}{G}) is materialised from the
/// embedded JSON definition (<c>finale-of-devastation.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the resolve-time effect is
/// layered on here because the JSON <c>AbilityDefinition</c> schema does not
/// express the X-driven tutor / conditional anthem (same posture as
/// <see cref="GenesisWaveFactory"/>).
///
/// ## Implemented (v1)
/// - Sorcery shape, printed cost {X}{G}{G}.
/// - <b>Tutor (CR 701.19a)</b>: search the caster's library AND graveyard for
///   a creature card (ANY colour — unlike Green Sun's Zenith there is no
///   colour predicate) whose mana value ≤ X (CR 202.3 — mana value off the
///   printed cost). Candidates from both zones are offered in one prompt; the
///   caster's <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> picks via
///   <see cref="LibrarySearch.PromptOnlyAsync"/> (no agent ⇒ deterministic
///   first match; empty candidates ⇒ no-op find, CR 701.19a permits declining).
/// - <b>Put onto battlefield</b>: the picked creature moves from whichever
///   zone it was found in (Library or Graveyard) → Battlefield via
///   <see cref="ZoneService.MoveCard"/> when a service is supplied / registered
///   (CR 603.6a — ETB triggers fire; <c>CardMovedEvent</c> publishes); raw
///   zone mutation fallback otherwise.
/// - <b>Shuffle (CR 701.20a)</b>: "If you search your library this way,
///   shuffle." The library is always searched here (it is part of the search
///   set), so the library shuffles whether or not a card was actually found —
///   matching the Green Sun's Zenith / GenesisWave posture.
/// - <b>Conditional anthem (CR 608.2)</b>: "If X is 10 or more, creatures you
///   control get +X/+X and gain haste until end of turn." When X ≥ 10, every
///   creature the caster controls at resolution gets a +X/+X pump
///   (<see cref="PumpUntilEndOfTurnEffect"/>, Layer 7c) and gains Haste
///   (<see cref="GrantKeywordUntilEndOfTurnEffect"/>, CR 702.10, Layer 6),
///   both until end of turn (CR 514.2). Creatures without a wired
///   <see cref="ContinuousEffectsService"/> no-op cleanly (shape-only guard,
///   mirrors <see cref="CraterhoofBehemothFactory.ApplyTrampleAndPump"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>"and/or" zone selection</b>: the search set unions library + graveyard
///   and shuffles the library unconditionally. A human who chose to search
///   ONLY the graveyard would, by the strict CR reading, not shuffle. Modelled
///   as "library is always searched" — the same lossy simplification every
///   other tutor in the engine makes (the player can always decline to find).
/// </summary>
[CardName("Finale of Devastation")]
public static class FinaleOfDevastationFactory
{
    public const string CardName = "Finale of Devastation";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "finale-of-devastation";

    /// <summary>Reason tag published on the post-search shuffle (CR 701.20a).</summary>
    public const string ShuffleReason = "finale-of-devastation";

    /// <summary>Granted keyword when X ≥ 10 — CR 702.10 Haste.</summary>
    public const string GrantedHaste = "Haste";

    /// <summary>The X threshold that switches on the anthem rider.</summary>
    public const int AnthemThreshold = 10;

    /// <summary>
    /// Construct Finale of Devastation owned and controlled by
    /// <paramref name="owner"/>. Base shape (name / Sorcery / {X}{G}{G}) is
    /// materialised from the embedded JSON; the resolve-time spell definition
    /// is built on demand via <see cref="BuildSpellDefinition"/>.
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
    /// Build the <see cref="SpellDefinition"/> Finale of Devastation uses on
    /// resolution. <see cref="SpellDefinition.HasVariableX"/> is true so the
    /// engine prompts for X at cast time; the resolve-time effect reads
    /// <c>ChosenSpellParams.X</c> as the mana-value ceiling for the tutor AND
    /// as the anthem magnitude / gate.
    /// </summary>
    /// <param name="caster">Spell controller — the player whose library and
    /// graveyard are searched and onto whose battlefield the picked creature
    /// lands.</param>
    /// <param name="card">The Finale of Devastation card instance (kept for
    /// parity with the analogue factories; a sorcery goes to the graveyard via
    /// the normal stack resolver, so the effect does not relocate it).</param>
    /// <param name="zoneService">Optional. When supplied, the tutored
    /// creature's move routes through this service so ETB triggers (CR 603.6a)
    /// + <c>CardMovedEvent</c> listeners fire. When null,
    /// <see cref="ZoneServiceRegistry.Get"/> is consulted, falling back to raw
    /// zone manipulation.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ICard card,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(card);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: p =>
            {
                var x = p.X ?? 0;
                return new IEffect[]
                {
                    new Effect(
                        $"Finale of Devastation: tutor a creature with mv ≤ {x} from " +
                        $"library and/or graveyard → battlefield.",
                        ctx => TutorAsync(caster, x, ctx, zoneService)),
                    new Effect(
                        $"Finale of Devastation: if X ({x}) ≥ {AnthemThreshold}, " +
                        $"creatures you control get +{x}/+{x} and gain haste until end of turn.",
                        () => ApplyAnthemIfBig(caster, x)),
                };
            });
    }

    /// <summary>
    /// Tutor step. Searches <paramref name="caster"/>'s library and graveyard
    /// for a creature card with mana value ≤ <paramref name="x"/>, prompts the
    /// agent for one pick (CR 701.19a), moves it to the battlefield, then
    /// shuffles the library (CR 701.20a). Public so tests / bots can drive
    /// resolution without going through SpellCastFlow.
    /// </summary>
    public static async ValueTask TutorAsync(
        Player caster,
        int x,
        ResolutionContext ctx,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        // CR 701.19a — eligible: creature card with mana value ≤ X, in either
        // the library or the graveyard. No colour restriction (unlike Green
        // Sun's Zenith). CR 202.3 — mana value reads off the printed cost.
        bool IsEligible(ICard c) =>
            c.HasType(CardType.Creature) &&
            ManaCost.Parse(c.ManaCost ?? string.Empty).TotalValue <= x;

        var libraryCandidates = caster.Zones.Library.GetCards().Where(IsEligible).ToList();
        var graveyardCandidates = caster.Zones.Graveyard.GetCards().Where(IsEligible).ToList();

        // Offer both zones' candidates in a single prompt — library first so
        // the deterministic first-match fallback prefers the library pick.
        var candidates = libraryCandidates.Concat(graveyardCandidates).ToList();

        var pick = await LibrarySearch.PromptOnlyAsync(
            ctx, caster, candidates,
            $"creature card with mana value {x} or less").ConfigureAwait(false);

        var effectiveZones = zoneService ?? ZoneServiceRegistry.Get(caster);
        if (pick != null)
        {
            // CR 400.7 — the source zone is wherever the picked card currently
            // lives (library or graveyard).
            var fromZone = pick.Zone;
            if (effectiveZones != null)
            {
                effectiveZones.MoveCard(pick, fromZone, ZoneType.Battlefield, caster);
            }
            else
            {
                // Direct mutation fallback — ETB triggers won't fire (no event
                // publishes), same shape as the Green Sun's Zenith fallback.
                switch (fromZone)
                {
                    case ZoneType.Graveyard:
                        caster.Zones.Graveyard.RemoveCard(pick);
                        break;
                    default:
                        caster.Zones.Library.RemoveCard(pick);
                        break;
                }
                caster.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                pick.SetController(caster);
            }
        }

        // CR 701.20a — "If you search your library this way, shuffle." The
        // library is part of the search set, so it shuffles whether or not a
        // card was found.
        LibraryShuffle.ShuffleLibrary(caster, ShuffleReason);
    }

    /// <summary>
    /// Anthem step (CR 608.2). "If X is 10 or more, creatures you control get
    /// +X/+X and gain haste until end of turn." When <paramref name="x"/> ≥
    /// <see cref="AnthemThreshold"/>, every creature
    /// <paramref name="controller"/> controls at resolution gets a +X/+X pump
    /// (CR 613.1c Layer 7c) and gains Haste (CR 702.10, Layer 6), both until
    /// end of turn (CR 514.2). Creatures without a wired
    /// <see cref="ContinuousEffectsService"/> no-op cleanly.
    /// </summary>
    public static void ApplyAnthemIfBig(Player controller, int x)
    {
        ArgumentNullException.ThrowIfNull(controller);

        // CR 608.2 — the "if X is 10 or more" intervening condition. Below the
        // threshold the rider does nothing.
        if (x < AnthemThreshold) return;

        // Snapshot to a list so any same-step side effects don't disturb the
        // enumeration (mirrors CraterhoofBehemothFactory.ApplyTrampleAndPump).
        var creatures = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .ToList();

        foreach (var creature in creatures)
        {
            // Shape-only safety — without a live ContinuousEffectsService the
            // grant/pump silently no-ops rather than NRE'ing.
            if (creature.ActiveEffects == null) continue;

            // CR 613.1c Layer 7c — +X/+X pump.
            creature.ActiveEffects.Register(
                new PumpUntilEndOfTurnEffect(creature, x, x));

            // CR 613.1c Layer 6 — Haste grant (CR 702.10).
            creature.ActiveEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(creature, GrantedHaste));
        }
    }
}
