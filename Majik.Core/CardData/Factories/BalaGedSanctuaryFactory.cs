using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Bala Ged Recovery // Bala Ged Sanctuary (Zendikar Rising).
///
/// Land. Oracle text (back):
///   "This land enters tapped."
///   "{T}: Add {G}."
///
/// Front face — <see cref="BalaGedRecoveryFactory"/> (Sorcery {2}{G} —
/// "Return target card from your graveyard to your hand.").
///
/// ## MDFC infra
/// See <see cref="BalaGedRecoveryFactory"/>'s class doc for the
/// cast-either-face design. This factory is the back-face dispatch arm:
/// when a player chooses to play the MDFC as a land,
/// <see cref="NamedCardFactory"/> resolves the back-face name
/// <c>"Bala Ged Sanctuary"</c> and lands here. The card is constructed
/// with its <see cref="MdfcState"/> pre-flipped to the back face.
///
/// ## Implemented (v1)
/// - Non-Basic <see cref="Land"/>, no printed subtype. Owner / controller
///   wired.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face
///   (mirrors <see cref="LegionStrongholdFactory"/> back-face posture).
/// - <b>"This land enters tapped"</b> — unconditional
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/> (CR 614.1c). Single-arg path (no bus)
///   omits the ETB replacement; same shape-only posture as
///   <see cref="LegionStrongholdFactory"/>.
/// - <b>{T}: Add {G}</b> — single <see cref="ManaAbility"/> producing one
///   green mana (CR 605.1 — mana ability, no stack).
///
/// ## References
/// - <see cref="LegionStrongholdFactory"/> — identical tapland + mana
///   shape (back face of the Legion Leadership MDFC pair); this factory
///   directly mirrors it.
/// </summary>
[CardName("Bala Ged Sanctuary")]
public static class BalaGedSanctuaryFactory
{
    public const string CardName = "Bala Ged Sanctuary";
    public const string FrontName = "Bala Ged Recovery";

    /// <summary>
    /// Construct Bala Ged Sanctuary without a <see cref="ReplacementBus"/>.
    /// The ETB-tapped replacement is omitted; the {T}: Add {G} mana
    /// ability is still wired. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Bala Ged Sanctuary with an optional
    /// <see cref="ReplacementBus"/> for full ETB-tapped wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the unconditional
    /// "enters tapped" replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Bala Ged Sanctuary is a vanilla nonbasic land — no basic land
        // subtype, no supertype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the
        // back face (Bala Ged Sanctuary is the back face that actually
        // exists on the battlefield).
        var mdfc = new MdfcState(FrontName, CardName);
        mdfc.Transform();
        land.MdfcState = mdfc;

        // ----------------------------------------------------------------
        // ETB: "This land enters tapped." (CR 614.1c)
        // Unconditional replacement — no optional life payment.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {T}: Add {G}  (CR 605.1 — mana ability, no stack)
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("G")));

        return land;
    }
}
