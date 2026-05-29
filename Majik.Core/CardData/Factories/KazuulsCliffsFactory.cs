using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Kazuul's Fury // Kazuul's Cliffs (Zendikar Rising).
///
/// Land. Oracle text (back):
///   "This land enters tapped."
///   "{T}: Add {R}."
///
/// Front face — <see cref="KazuulsFuryFactory"/> (Instant {2}{R} —
/// "As an additional cost to cast this spell, sacrifice a creature.
/// Kazuul's Fury deals damage equal to the sacrificed creature's power
/// to any target.").
///
/// ## MDFC infra
/// See <see cref="KazuulsFuryFactory"/>'s class doc for the cast-either-face
/// design. This factory is the back-face dispatch arm: when a player
/// chooses to play the MDFC as a land, <see cref="NamedCardFactory"/>
/// resolves the back-face name <c>"Kazuul's Cliffs"</c> and lands here.
/// The card is constructed with its <see cref="MdfcState"/> pre-flipped
/// to the back face.
///
/// ## Implemented (v1)
/// - Non-Basic <see cref="Land"/>, no printed subtype. Owner / controller
///   wired.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face
///   (mirrors <see cref="BalaGedSanctuaryFactory"/> back-face posture).
/// - <b>"This land enters tapped"</b> — unconditional
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/> (CR 614.1c). Single-arg path (no bus)
///   omits the ETB replacement; same shape-only posture as
///   <see cref="BalaGedSanctuaryFactory"/>.
/// - <b>{T}: Add {R}</b> — single <see cref="ManaAbility"/> producing one
///   red mana (CR 605.1 — mana ability, no stack).
///
/// ## References
/// - <see cref="BalaGedSanctuaryFactory"/> — identical unconditional
///   tapland + single-color mana shape (this factory directly mirrors it,
///   {R} instead of {G}).
/// </summary>
[CardName("Kazuul's Cliffs")]
public static class KazuulsCliffsFactory
{
    public const string CardName = "Kazuul's Cliffs";
    public const string FrontName = "Kazuul's Fury";

    /// <summary>
    /// Construct Kazuul's Cliffs without a <see cref="ReplacementBus"/>.
    /// The ETB-tapped replacement is omitted; the {T}: Add {R} mana
    /// ability is still wired. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Kazuul's Cliffs with an optional
    /// <see cref="ReplacementBus"/> for full ETB-tapped wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the unconditional
    /// "enters tapped" replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Kazuul's Cliffs is a vanilla nonbasic land — no basic land
        // subtype, no supertype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the
        // back face (Kazuul's Cliffs is the back face that actually exists
        // on the battlefield).
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
        // {T}: Add {R}  (CR 605.1 — mana ability, no stack)
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("R")));

        return land;
    }
}
