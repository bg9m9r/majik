using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Hagra Mauling // Hagra Broodpit (Zendikar Rising).
///
/// Land. Oracle text (back, verified against Scryfall):
///   "This land enters tapped."
///   "{T}: Add {B}."
///
/// Front face — <see cref="HagraMaulingFactory"/> (Instant {2}{B}{B} —
/// "This spell costs {1} less to cast if an opponent controls no basic
/// lands. Destroy target creature.").
///
/// ## MDFC infra (CR 711 / 712)
///
/// See <see cref="HagraMaulingFactory"/>'s class doc for the cast-either-face
/// design. This factory is the back-face dispatch arm: when a player chooses
/// to play the MDFC as a land, <see cref="NamedCardFactory"/> resolves the
/// back-face name <c>"Hagra Broodpit"</c> and lands here. The card is
/// constructed with its <see cref="MdfcState"/> pre-flipped to the back face
/// so the face tracker reads as authoritative.
///
/// ## Implemented (v1)
///
/// - Non-Basic <see cref="Land"/> with no printed subtype.
///   Owner / controller wired.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face.
/// - <b>{T}: Add {B}</b> — single <see cref="ManaAbility"/> producing one
///   black mana (CR 605.1 — mana ability, no stack).
/// - <b>"This land enters tapped." (CR 614.1c)</b> — modelled via the
///   UNCONDITIONAL <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Unlike Sea Gate, Reborn / Soporific
///   Springs (which offer a "pay 3 life or enter tapped" choice), Hagra
///   Broodpit always enters tapped — no agent prompt.
/// - Single-arg dispatcher path: no <see cref="ReplacementBus"/> wired —
///   the ETB replacement is omitted (shape-only posture matching the other
///   MDFC back-face land factories).
///
/// ## References
///
/// - <see cref="SeaGateRebornFactory"/> — MDFC back-face tap-land sharing the
///   pre-flipped <see cref="MdfcState"/> + {T}: Add mana shape (Hagra
///   Broodpit's enters-tapped is unconditional rather than the pay-3-life
///   variant).
/// </summary>
[CardName("Hagra Broodpit")]
public static class HagraBroodpitFactory
{
    public const string CardName = "Hagra Broodpit";
    public const string FrontName = "Hagra Mauling";

    /// <summary>
    /// Construct Hagra Broodpit without a <see cref="ReplacementBus"/>.
    /// The enters-tapped replacement is omitted; the {T}: Add {B} mana
    /// ability is still wired. Suitable for card-shape / dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Hagra Broodpit with an optional <see cref="ReplacementBus"/>
    /// for full ETB wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the unconditional
    /// "this land enters tapped" replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Hagra Broodpit is a vanilla nonbasic land — no basic land subtype,
        // no supertype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the back
        // face (Hagra Broodpit is the back face that actually exists on the
        // battlefield).
        var mdfc = new MdfcState(FrontName, CardName);
        mdfc.Transform();
        land.MdfcState = mdfc;

        // ----------------------------------------------------------------
        // "This land enters tapped." (CR 614.1c) — UNCONDITIONAL.
        // ----------------------------------------------------------------
        replacements?.Register(new EntersTappedReplacement(land));

        // ----------------------------------------------------------------
        // {T}: Add {B}  (CR 605.1 — mana ability, no stack)
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("B")));

        return land;
    }
}
