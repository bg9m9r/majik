using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Valakut Awakening // Valakut Stoneforge (Zendikar Rising).
///
/// Land. Oracle text (back, verified against Scryfall):
///   "This land enters tapped."
///   "{T}: Add {R}."
///
/// Front face — <see cref="ValakutAwakeningFactory"/> (Instant {2}{R} —
/// "Put any number of cards from your hand on the bottom of your library,
/// then draw that many cards plus one.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="BalaGedRecoveryFactory"/> / <see cref="BalaGedSanctuaryFactory"/>
/// (MDFC spell-front + tapland-back). When a player chooses to play the MDFC
/// as a land, <see cref="NamedCardFactory"/> resolves the back-face name
/// <c>"Valakut Stoneforge"</c> and lands here. The card is constructed with
/// its <see cref="MdfcState"/> pre-flipped to the back face.
///
/// ## Card identity comes from JSON
///
/// Name / type and the <b>{T}: Add {R}</b> mana ability are loaded from the
/// embedded JSON definition (<c>valakut-stoneforge.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The MDFC face tracker and the
/// unconditional ETB-tapped replacement are attached in code (the JSON
/// schema models neither).
///
/// ## Implemented (v1)
///
/// - Non-Basic <see cref="Land"/>, no printed subtype. Owner / controller
///   wired.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face
///   (mirrors <see cref="BalaGedSanctuaryFactory"/> back-face posture).
/// - <b>"This land enters tapped"</b> — unconditional
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/> (CR 614.1c). Single-arg path (no bus)
///   omits the ETB replacement; same shape-only posture as
///   <see cref="BalaGedSanctuaryFactory"/>.
/// - <b>{T}: Add {R}</b> — single <see cref="Majik.Core.Abilities.ManaAbility"/>
///   producing one red mana (CR 605.1 — mana ability, no stack), from JSON.
///
/// ## References
/// - <see cref="BalaGedSanctuaryFactory"/> — identical tapland + mana shape
///   (back face of the Bala Ged Recovery MDFC pair); this factory directly
///   mirrors it (swapping {G} → {R}).
/// </summary>
[CardName("Valakut Stoneforge")]
public static class ValakutStoneforgeFactory
{
    public const string CardName = "Valakut Stoneforge";
    public const string FrontName = "Valakut Awakening";
    public const string Slug = "valakut-stoneforge";

    /// <summary>
    /// Construct Valakut Stoneforge without a <see cref="ReplacementBus"/>.
    /// The ETB-tapped replacement is omitted; the {T}: Add {R} mana ability
    /// (from JSON) is still wired. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Valakut Stoneforge with an optional
    /// <see cref="ReplacementBus"/> for full ETB-tapped wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the unconditional
    /// "enters tapped" replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the {T}: Add {R} mana ability come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the
        // back face (Valakut Stoneforge is the back face that actually
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

        return land;
    }
}
