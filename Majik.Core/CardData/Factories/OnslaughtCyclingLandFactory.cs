using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Parametric named-card factory for the Onslaught (and reprint)
/// monocolour cycling-land cycle — Tranquil Thicket, Lonely Sandbar,
/// Secluded Steppe, Barren Moor, Forgotten Cave.
///
/// Each cycle member shares the same shape:
///   "&lt;Land&gt; enters tapped.
///    {T}: Add {color}.
///    Cycling {color}."
///
/// Only the printed name, the printed land subtype, and the mana-cost
/// colour-letter differ across members, so a single parametric factory
/// dispatches them through the source-generator's <c>[CardName(...)]</c>
/// attribute path. v1 ships Tranquil Thicket + Lonely Sandbar; the
/// remaining three members slot in via additional attributes without
/// touching the body.
///
/// Args layout (forwarded by the source generator at dispatch time):
/// <c>[0] = printed card name</c>,
/// <c>[1] = produced mana colour (single-letter Scryfall code: W/U/B/R/G)</c>,
/// <c>[2] = printed land subtype (Forest / Island / Plains / Swamp / Mountain)</c>.
///
/// Cycling cost on every cycle member is exactly 1 mana of the produced
/// colour (CR 702.32 — the printed cycling cost is always
/// <c>{color}</c> for this cycle).
///
/// ## Implemented (v1)
/// - <b>Land</b> with the printed subtype (Forest / Island). NOT
///   Basic — these are nonbasic land-types-as-subtype lands (CR 305.6 —
///   the subtype grants the mana ability through the L4 type-derivation
///   pipeline) but the printed mana ability is also declared inline
///   here so dispatcher / shape tests see <c>{T}: Add {color}</c>
///   without an active <see cref="Majik.Core.Effects.ContinuousEffectsService"/>.
/// - <b>Enters-tapped replacement (CR 614.1c)</b> — unconditional
///   "&lt;Land&gt; enters tapped." Registered via
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Shape-only path (no
///   <see cref="ReplacementBus"/>) skips the registration, mirroring
///   <see cref="BojukaBogFactory"/>'s posture.
/// - <b>{T}: Add {color}</b> — vanilla <see cref="ManaAbility"/>
///   (CR 605.1 — mana abilities don't use the stack).
/// - <b>Cycling {color}</b> (CR 702.32) — wired through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>(<c>{color}</c>). When the bus is
///   supplied, cycling resolve publishes
///   <see cref="CardCycledEvent"/> (CR 702.32d "Whenever a player
///   cycles a card") so Lightning Rift / Astral Slide / Decree of
///   Justice triggers fire.
///
/// ## Deferred (v1 gaps)
/// - <b>Basic land subtype mana derivation via L4</b> — the printed
///   subtype (Forest / Island) is set on the card but the mana
///   ability is declared inline rather than derived through
///   <see cref="EffectiveManaAbilities"/>. Same posture as
///   <see cref="BojukaBogFactory"/>'s {T}: Add {B}.
/// </summary>
[CardName("Tranquil Thicket", "G", "Forest")]
[CardName("Lonely Sandbar",   "U", "Island")]
public static class OnslaughtCyclingLandFactory
{
    /// <summary>
    /// Fallback overload — only reachable when constructed by hand.
    /// Default-builds Tranquil Thicket.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, new[] { "Tranquil Thicket", "G", "Forest" });

    /// <summary>
    /// Construct the Onslaught cycling-land identified by
    /// <paramref name="args"/>. Single-arg path — no bus wiring (shape
    /// observability only; enters-tapped is omitted and cycling does not
    /// publish <see cref="CardCycledEvent"/>).
    /// </summary>
    public static Land Create(Player owner, string[] args) =>
        Create(owner, args, eventBus: null, replacements: null);

    /// <summary>
    /// Construct an Onslaught cycling-land with full bus wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="args">Source-generator-provided args. Layout:
    /// <c>[0] = printed card name</c>,
    /// <c>[1] = produced mana colour (single-letter)</c>,
    /// <c>[2] = printed land subtype</c>.</param>
    /// <param name="eventBus">Optional event bus the cycling resolve
    /// publishes <see cref="CardCycledEvent"/> against (CR 702.32d).</param>
    /// <param name="replacements">Optional replacement bus the
    /// enters-tapped restriction (CR 614.1c) is registered against.</param>
    public static Land Create(
        Player owner,
        string[] args,
        IEventBus? eventBus,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length < 3)
        {
            throw new ArgumentException(
                $"OnslaughtCyclingLandFactory needs args = [name, color, landSubtype] (got {args.Length}).",
                nameof(args));
        }

        var cardName = args[0];
        var color = args[1];
        var landSubtypeName = args[2];
        var landSubtype = ParseLandSubtype(landSubtypeName);

        var land = new Land(cardName, supertypes: null, subtypes: new[] { landSubtype });
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // Enters-tapped — CR 614.1c. Unconditional.
        // Shape-only path (no ReplacementBus) skips registration; the
        // land then enters untapped. Same posture as BojukaBogFactory.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {T}: Add {color}. CR 605.1 — mana ability (no stack).
        // Inline declaration; the printed land subtype (Forest / Island)
        // would also feed the L4 mana-derivation pipeline but the
        // shape-only test surface reads the explicit ManaAbility here.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse(color)));

        // ----------------------------------------------------------------
        // Cycling {color}. CR 702.32 — "{color}, Discard this card: Draw
        // a card." Cycle cost is ManaCostCost(color); the primitive
        // appends the DiscardSelfCost hand-zone gate (CR 702.32a) and
        // the CardCycledEvent publish (CR 702.32d).
        // ----------------------------------------------------------------
        CyclingFactory.Build(land, new ManaCostCost(color), eventBus);

        return land;
    }

    /// <summary>
    /// Map the args[2] string to a <see cref="CardSubtype"/>. Throws on
    /// unknown subtype — the parametric attribute list above is the
    /// single source of truth for supported members.
    /// </summary>
    private static CardSubtype ParseLandSubtype(string name) => name switch
    {
        "Forest" => CardSubtype.Forest,
        "Island" => CardSubtype.Island,
        "Plains" => CardSubtype.Plains,
        "Swamp" => CardSubtype.Swamp,
        "Mountain" => CardSubtype.Mountain,
        _ => throw new ArgumentException(
            $"OnslaughtCyclingLandFactory: unknown land subtype '{name}'.", nameof(name)),
    };
}
