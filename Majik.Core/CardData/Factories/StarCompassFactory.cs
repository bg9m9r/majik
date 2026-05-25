using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Star Compass (Prophecy / Ice Age reprint, {2}).
///
/// Artifact. Oracle text (verified Scryfall):
///   "Star Compass enters tapped.
///    {T}: Add one mana of any color that a basic land you control could
///    produce."
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {2}, owner / controller).
/// - <b>ETB tapped (CR 614.1c)</b> — registered as an
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Same shape as Sea Gate Wreckage /
///   Wishclaw Talisman / the cycle of always-tapped fixing artifacts.
///   Single-arg dispatcher path omits the replacement (mirrors every
///   other always-tapped factory).
/// - <b>{T}: Add one mana of any color that a basic land you control
///   could produce</b> — five <see cref="ManaAbility"/> instances (one
///   per WUBRG), each gated on:
///     - <c>!IsTapped</c> (the {T} half of the cost; engine taps in
///       <see cref="ManaAbility.Activate"/>),
///     - <c>Zone == Battlefield</c>,
///     - <c>controller controls a basic land with the matching subtype</c>
///       (Plains for W, Island for U, Swamp for B, Mountain for R, Forest
///       for G — printed CR 305.6 basic land type → colour mapping).
///   Same gating shape as Mox Opal's Metalcraft scan but keyed per
///   colour. Wastes (CR 305.6a: colourless basic land without a basic
///   land type) deliberately doesn't gate any of the abilities — Star
///   Compass only produces COLOURED mana, and CR 107.4c folds {C} into
///   the generic bucket, neither of which matches the "any color" pip.
///
/// ## Rules note — colour mapping
/// CR 305.6: basic land subtypes have intrinsic mana abilities. Plains →
/// {W}, Island → {U}, Swamp → {B}, Mountain → {R}, Forest → {G}. Star
/// Compass's "could produce" is read against the printed subtype only —
/// non-basic dual lands (Hallowed Fountain, Tundra) don't qualify even
/// though they tap for the same colours, because they aren't BASIC. The
/// scan also ignores tapping state (a tapped Forest still satisfies the
/// "could produce" predicate per the printed wording — the gate is
/// existence-of-source, not availability-now).
///
/// ## Deferred (v1 gaps)
/// - <b>Single modal-colour mana ability</b>: "One mana of any color
///   that ..." is bound as five separate <see cref="ManaAbility"/>
///   instances; the bot's source-picker selects the right colour at
///   payment time. Same posture as Lotus Petal / Mox Opal / Chromatic
///   Star / Delighted Halfling.
/// - <b>Layered subtype reads</b>: the basic-land scan reads printed
///   <see cref="Card.HasSubtype"/> directly rather than effective
///   subtypes after a layered CR 613 effect (e.g. Spreading Seas making
///   a Forest into an Island). When a layered subtype-read service lands
///   the predicate should consume effective subtypes — same v1 gap as
///   Tribal Flames / Domain counting.
/// </summary>
[CardName("Star Compass")]
public static class StarCompassFactory
{
    public const string CardName = "Star Compass";
    public const string PrintedManaCost = "{2}";

    /// <summary>
    /// CR 305.6 — basic land subtype → colour mana mapping. Plains→W,
    /// Island→U, Swamp→B, Mountain→R, Forest→G. Wastes (CR 305.6a) is
    /// deliberately excluded — it's a basic land WITHOUT a basic land
    /// type, and Star Compass's gate is per-colour.
    /// </summary>
    private static readonly (string Color, CardSubtype BasicType)[] ColorBasicMap =
    {
        ("W", CardSubtype.Plains),
        ("U", CardSubtype.Island),
        ("B", CardSubtype.Swamp),
        ("R", CardSubtype.Mountain),
        ("G", CardSubtype.Forest),
    };

    /// <summary>
    /// Construct Star Compass with no <see cref="ReplacementBus"/>
    /// wired. The ETB-tapped replacement is omitted (shape-only); the
    /// five colour mana abilities remain attached. Mirrors Sea Gate
    /// Wreckage's single-arg dispatcher path.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Star Compass.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">Replacement bus for the
    /// always-enters-tapped restriction (CR 614.1c). May be null.</param>
    public static Artifact Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var compass = new Artifact(CardName, PrintedManaCost);
        compass.SetOwner(owner);
        compass.SetController(owner);

        // ----------------------------------------------------------------
        // ETB-tapped restriction (CR 614.1c) — "Star Compass enters
        // tapped." Unconditional; no gate. Mirrors Sea Gate Wreckage's
        // wiring.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(compass));
        }

        // ----------------------------------------------------------------
        // {T}: Add one mana of any color that a basic land you control
        // could produce.
        //
        // Five sibling ManaAbility instances, each gated on:
        //   (1) !IsTapped (the {T} half; tap happens in ManaAbility.Activate),
        //   (2) Zone == Battlefield,
        //   (3) controller controls a basic of the matching subtype.
        //
        // CR 605.1 — mana ability (no stack). The basic-land scan reads
        // the LIVE controller (compass.Controller) so control-change
        // effects re-point the predicate.
        // ----------------------------------------------------------------
        foreach (var (color, basicType) in ColorBasicMap)
        {
            // Capture basicType in a local for the closure (avoid
            // foreach-variable capture pitfalls).
            var requiredBasic = basicType;

            compass.AddAbility(new ManaAbility(
                source: compass,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => !compass.IsTapped
                                         && compass.Zone == ZoneType.Battlefield
                                         && ControlsBasicOfType(compass, requiredBasic)));
        }

        return compass;
    }

    /// <summary>
    /// CR 305.6 — does <paramref name="compass"/>'s controller control
    /// a basic land of subtype <paramref name="basicType"/>? Reads the
    /// live controller (control-change-honouring). Returns false if the
    /// controller is null (e.g. Star Compass is off the battlefield).
    /// </summary>
    public static bool ControlsBasicOfType(Artifact compass, CardSubtype basicType)
    {
        ArgumentNullException.ThrowIfNull(compass);

        var controller = compass.Controller;
        if (controller is null) return false;

        foreach (var card in controller.Zones.Battlefield.GetCards())
        {
            if (!card.HasType(CardType.Land)) continue;
            if (!card.HasSupertype(CardSupertype.Basic)) continue;
            if (card.HasSubtype(basicType)) return true;
        }
        return false;
    }
}
