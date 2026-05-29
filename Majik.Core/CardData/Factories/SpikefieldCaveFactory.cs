using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Spikefield Hazard // Spikefield Cave (Zendikar Rising).
///
/// Land. Oracle text (back):
///   "This land enters tapped."
///   "{T}: Add {R}."
///
/// Front face — <see cref="SpikefieldHazardFactory"/> (Instant {R} —
/// "Spikefield Hazard deals 1 damage to any target. If a permanent dealt
/// damage this way would die this turn, exile it instead.").
///
/// ## MDFC infra
/// See <see cref="SpikefieldHazardFactory"/>'s class doc for the
/// cast-either-face design. This factory is the back-face dispatch arm:
/// when a player chooses to play the MDFC as a land,
/// <see cref="NamedCardFactory"/> resolves the back-face name
/// <c>"Spikefield Cave"</c> and lands here. The card is constructed with its
/// <see cref="MdfcState"/> pre-flipped to the back face.
///
/// ## Implemented (v1)
/// - Non-Basic <see cref="Land"/>, no printed subtype. Owner / controller
///   wired.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face.
/// - <b>"This land enters tapped"</b> — unconditional
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/> (CR 614.1c). Single-arg path (no bus)
///   omits the ETB replacement; same shape-only posture as
///   <see cref="BalaGedSanctuaryFactory"/>.
/// - <b>{T}: Add {R}</b> — single <see cref="ManaAbility"/> producing one
///   red mana (CR 605.1 — mana ability, no stack).
///
/// ## References
/// - <see cref="BalaGedSanctuaryFactory"/> — identical "enters tapped" +
///   single mana shape (back face of another Zendikar Rising MDFC pair);
///   this factory directly mirrors it.
/// </summary>
[CardName("Spikefield Cave")]
public static class SpikefieldCaveFactory
{
    public const string CardName = "Spikefield Cave";
    public const string FrontName = "Spikefield Hazard";

    /// <summary>
    /// Construct Spikefield Cave without a <see cref="ReplacementBus"/>. The
    /// ETB-tapped replacement is omitted; the {T}: Add {R} mana ability is
    /// still wired. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Spikefield Cave with an optional <see cref="ReplacementBus"/>
    /// for full ETB-tapped wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the unconditional "enters
    /// tapped" replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Spikefield Cave is a vanilla nonbasic land — no basic land
        // subtype, no supertype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the back
        // face (Spikefield Cave is the back face that actually exists on the
        // battlefield).
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
