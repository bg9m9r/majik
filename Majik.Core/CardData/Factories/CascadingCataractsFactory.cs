using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cascading Cataracts (Kaladesh).
///
/// Land. Oracle text:
///   "Indestructible
///    {T}: Add {C}.
///    {5}, {T}: Add five mana in any combination of colors."
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — non-Basic, no subtype. Lands have no mana cost
///   (CR 305.1); the base <see cref="Land"/> constructor passes an empty cost.
/// - <b>Indestructible</b> (CR 702.12) — wired as a
///   <see cref="KeywordAbility"/> marker, exactly like
///   <see cref="DarksteelCitadelFactory"/>. Read by the non-creature
///   destroy gate.
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1, no
///   stack). Mirrors Darksteel Citadel / Wasteland's tap-for-{C} shape.
/// - <b>{5}, {T}: Add five mana in any combination of colors</b> — modelled
///   as SIX sibling <see cref="ManaAbility"/> slots (the five mono-colour
///   five-pip combos WWWWW / UUUUU / BBBBB / RRRRR / GGGGG, plus the
///   one-of-each WUBRG split). Each is built via the additional-cost
///   overload of <see cref="ManaAbility"/>:
///   <c>canActivateCheck = !land.IsTapped &amp;&amp;
///   controller.ManaPool.CanPay({5})</c>,
///   <c>additionalCostPayer = controller.PayMana({5})</c>. This is the same
///   {N}-cost mana-ability shape <see cref="FilterLandCycleFactory"/> uses
///   for its {1} filter modes, and the same any-colour fan-out posture
///   Chromatic Star / City of Brass / Cavern of Souls take — the bot's
///   source-picker selects the slot matching the colours it needs.
///
///   CR 605.1 — these are still mana abilities (they don't use the stack);
///   the {5} extra cost is paid as part of activation, atomically with the
///   {T} tap.
///
/// ## Deferred (v1 gaps)
/// - <b>Full "any combination" enumeration</b>: the printed oracle is a
///   single mana ability that can produce ANY multiset of five coloured
///   pips (e.g. WWUBR, GGGGW, …). v1 ships the six most representative
///   splits — the five mono-colour fives and the rainbow WUBRG — which
///   covers every fixing need a bot exercises in practice (mono-colour
///   ramp + five-colour fixing). A future modal-mana-ability primitive
///   that lets the activator name an arbitrary colour distribution would
///   collapse these slots into one. Same posture FilterLandCycleFactory's
///   three-slot split takes for its modal "Add {A}{A}, {A}{B}, or {B}{B}".
/// - <b>{5} affordability look-ahead</b>: activation requires {5} to already
///   be in the mana pool; the engine doesn't auto-tap other sources to feed
///   the cost (no mana-fixer planner) — identical posture to every other
///   additional-mana-cost activated ability (filter lands, Mind Stone, …).
/// </summary>
[CardName("Cascading Cataracts")]
public static class CascadingCataractsFactory
{
    public const string CardName = "Cascading Cataracts";

    /// <summary>
    /// Construct Cascading Cataracts owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // Indestructible (CR 702.12). Marker only — destroy gates read
        // KeywordAbility off Permanent. Mirrors Darksteel Citadel.
        // ----------------------------------------------------------------
        land.AddAbility(new KeywordAbility("Indestructible", land, owner));

        // ----------------------------------------------------------------
        // {T}: Add {C}. CR 605.1 — vanilla colourless mana ability, no
        // extra cost (the {5} rider applies only to the five-mana modes).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {5}, {T}: Add five mana in any combination of colors.
        // Six representative sibling slots (five mono-colour fives + the
        // rainbow split). Each pays {5} via the additional-cost overload.
        // ----------------------------------------------------------------
        AttachFiveManaMode(land, owner, "WWWWW");
        AttachFiveManaMode(land, owner, "UUUUU");
        AttachFiveManaMode(land, owner, "BBBBB");
        AttachFiveManaMode(land, owner, "RRRRR");
        AttachFiveManaMode(land, owner, "GGGGG");
        AttachFiveManaMode(land, owner, "WUBRG");

        return land;
    }

    /// <summary>
    /// Attach a <c>{5}, {T}: Add &lt;pips&gt;</c> mana ability. The {T} tap
    /// is paid by the default tap-as-cost path; the <paramref name="pips"/>
    /// output (five coloured pips) is the produced mana; the
    /// <c>additionalCostPayer</c> deducts {5} from the controller's mana
    /// pool. The <c>canActivateCheck</c> gates on both the untap state and
    /// the {5}-affordability check — without the latter, activation would
    /// tap the land and then no-op on the payment. Mirrors the filter-mode
    /// shape in <see cref="FilterLandCycleFactory"/>.
    /// </summary>
    private static void AttachFiveManaMode(Land land, Player controller, string pips)
    {
        var output = ManaCost.Parse(pips);
        var fiveGeneric = ManaCost.Parse("5");

        land.AddAbility(new ManaAbility(
            source: land,
            controller: controller,
            manaGenerated: output,
            canActivateCheck: () => !land.IsTapped && controller.ManaPool.CanPay(fiveGeneric),
            additionalCostPayer: p => p.PayMana(fiveGeneric)));
    }
}
