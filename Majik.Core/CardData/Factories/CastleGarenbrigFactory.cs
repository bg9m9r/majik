using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Castle Garenbrig (Throne of Eldraine / reprints).
///
/// Land. Oracle text (Scryfall-confirmed):
///   "Castle Garenbrig enters tapped unless you control a Forest.
///    {T}: Add {G}.
///    {2}{G}{G}, {T}: Add six {G}. Spend this mana only to cast creature
///    spells or activate abilities of creatures."
///
/// Scryfall-confirmed type line: Land (no basic supertype, no subtypes).
/// Castle Garenbrig is NOT itself a Forest.
///
/// This is the green member of the Eldraine Castle cycle and the direct
/// sibling of <see cref="CastleLocthwainFactory"/> (the black member). It
/// reuses the same two shapes that are already proven elsewhere:
/// <list type="bullet">
///   <item><b>ETB tapped unless you control a basic-land-type</b> —
///   <see cref="ConditionalEntersTappedReplacement"/>, exactly as
///   <see cref="CastleLocthwainFactory"/> (Swamp -> Forest here).</item>
///   <item><b>Pay-mana-as-part-of-a-mana-ability</b> — the {2}{G}{G},{T}
///   ability is a mana ability (CR 605.1a: produces mana, no target,
///   doesn't use the stack) whose activation cost includes the {2}{G}{G}.
///   Same shape as <see cref="CabalCoffersFactory"/> ("{2},{T}: Add {B}
///   for each Swamp").</item>
/// </list>
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — plain nonbasic Land, no supertype, no subtype.
/// - <b>ETB tapped unless you control a Forest (CR 614.1c)</b> — registered
///   as a <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. The predicate checks whether the
///   controller controls at least one other permanent with the
///   <see cref="CardSubtype.Forest"/> subtype (dual lands / shocklands with
///   the Forest subtype, snow-covered Forests etc. all qualify). The card
///   itself is excluded via reference equality — Castle Garenbrig has no
///   Forest subtype anyway, so it can never satisfy its own predicate.
///   Single-arg dispatcher path omits the replacement (shape-only posture,
///   mirroring <see cref="CastleLocthwainFactory"/>).
/// - <b>{T}: Add {G}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1).
/// - <b>{2}{G}{G}, {T}: Add six {G}.</b> — a <see cref="ManaAbility"/>
///   producing a fixed six green. The {2}{G}{G} portion of the activation
///   cost is paid up front via the <c>additionalCostPayer</c> overload
///   (CR 602.2a — costs paid as a single step; CR 605.1a — still a mana
///   ability, no stack). The <c>canActivateCheck</c> gates on both the
///   untapped state (the {T} cost) and affordability of {2}{G}{G}
///   (read-only <see cref="ManaPool.CanPay"/>), so the cost is only ever
///   paid when the full activation is legal.
///
/// ## Spend-restriction posture (v1 data, payment-gate deferred)
/// "Spend this mana only to cast creature spells or activate abilities of
/// creatures." The engine ships a <see cref="SpendRestriction"/> primitive
/// (see <see cref="CavernOfSoulsFactory"/> / <see cref="EldraziTempleFactory"/>),
/// but its <c>ManaAbility</c> overloads that carry a restriction take a
/// <i>fixed</i> generated cost with no <c>additionalCostPayer</c>, and the
/// payment-gate enforcement (filtering tagged pool entries when paying a
/// non-creature cost) is deferred until <see cref="ManaPool"/> grows
/// per-slot provenance. To use only existing engine shapes — and because
/// the rider is observational-only today — the six-green ability is built
/// through the <c>additionalCostPayer</c> overload (which pays the
/// {2}{G}{G}) and the creature-only rider is documented but not stamped, a
/// conservative omission identical to Eldrazi Temple's deferred
/// "or activate abilities of Eldrazi" half. The six green produced is
/// untagged generic-usable green in v1; it unlocks the creature-only gate
/// at the same time as the rest of the spend-restriction surface.
/// </summary>
[CardName("Castle Garenbrig")]
public static class CastleGarenbrigFactory
{
    public const string CardName = "Castle Garenbrig";

    /// <summary>The {2}{G}{G} portion of the big ability's activation cost.</summary>
    private static readonly ManaCost BigActivationCost = ManaCost.Parse("{2}{G}{G}");

    /// <summary>The six {G} the big ability produces.</summary>
    private static readonly ManaCost SixGreen = ManaCost.Parse("{G}{G}{G}{G}{G}{G}");

    /// <summary>
    /// Construct Castle Garenbrig without a <see cref="ReplacementBus"/>
    /// wired. The ETB-tapped-unless-Forest predicate is omitted (shape-only
    /// posture); both mana abilities are still attached.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Castle Garenbrig.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the "enters tapped unless
    /// you control a Forest" replacement is registered (CR 614.1c). May be
    /// null.</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Non-basic Land — no supertype, no subtype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // ETB tapped unless you control a Forest (CR 614.1c).
        //
        // Predicate: entersUntappedIf returns true ⟺ the controller
        // controls at least one land (other than this card) with the
        // CardSubtype.Forest subtype. Reference-equality exclusion of self
        // mirrors CastleLocthwainFactory's single-type predicate shape.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    controller.Zones.Battlefield.GetCards()
                        .Any(c => !ReferenceEquals(c, self) && c.HasSubtype(CardSubtype.Forest))));
        }

        // ----------------------------------------------------------------
        // {T}: Add {G} — vanilla mana ability (CR 605.1).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("G")));

        // ----------------------------------------------------------------
        // {2}{G}{G}, {T}: Add six {G}.
        //
        // CR 605.1a — this is a mana ability (produces mana, no target,
        // doesn't use the stack) even though it has a mana cost in its
        // activation cost. We use the additionalCostPayer overload:
        //   canActivateCheck   — untapped (the {T} cost) AND the pool can
        //                        afford {2}{G}{G} (read-only CanPay).
        //   manaGenerated      — fixed six {G}.
        //   additionalCostPayer— drains {2}{G}{G} from the controller's
        //                        pool (runs after the {T} tap, before the
        //                        mana is returned — same atomic activation
        //                        step per CR 602.2a).
        //
        // Spend-restriction ("only to cast creature spells / activate
        // abilities of creatures") is documented but not stamped — the
        // restriction-carrying ManaAbility overload is incompatible with
        // the additionalCostPayer overload, and the payment gate is
        // deferred engine-wide (see class xmldoc + EldraziTempleFactory).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: SixGreen,
            canActivateCheck: () =>
                !land.IsTapped
                && (land.Controller ?? owner).ManaPool.CanPay(BigActivationCost),
            // Pay from the live controller (land.Controller) so a
            // control-change effect is honoured; the `payer` arg is the
            // ctor-time controller and is intentionally ignored.
            additionalCostPayer: _ => (land.Controller ?? owner).PayMana(BigActivationCost)));

        return land;
    }
}
