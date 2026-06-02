using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Kabira Takedown // Kabira Plateau (Zendikar Rising).
///
/// Land. Oracle text (back, verified against Scryfall):
///   "This land enters tapped."
///   "{T}: Add {W}."
///
/// Front face — <see cref="KabiraTakedownFactory"/> (Instant {1}{W}).
///
/// ## MDFC infra
///
/// See <see cref="KabiraTakedownFactory"/>'s class doc for the cast-either-face
/// design. This factory is the back-face dispatch arm: when a player chooses to
/// play the MDFC as a land, <see cref="NamedCardFactory"/> resolves the
/// back-face name <c>"Kabira Plateau"</c> and lands here. The card is
/// constructed with its <see cref="MdfcState"/> pre-flipped to the back face so
/// the face tracker reads as authoritative.
///
/// ## Card identity comes from JSON
///
/// Name / type and the <b>{T}: Add {W}</b> mana ability are loaded from the
/// embedded JSON definition (<c>kabira-plateau.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The MDFC face tracker and the
/// enters-tapped replacement are attached in code (the JSON schema models
/// neither).
///
/// ## Implemented (v1)
///
/// - Non-Basic <see cref="Land"/> with no printed subtype. Owner / controller
///   wired.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face (mirrors
///   <see cref="EmeriaShatteredSkyclaveFactory"/>).
/// - <b>{T}: Add {W}</b> — single <see cref="ManaAbility"/> producing one white
///   mana (CR 605.1 — mana ability, no stack), from JSON.
/// - <b>"This land enters tapped." (CR 614.1c)</b> — modelled via the
///   UNCONDITIONAL <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Unlike the pay-3-life ZNR MDFC lands
///   (Emeria, Shattered Skyclave / Sea Gate, Reborn), Kabira Plateau always
///   enters tapped — there is no choice to make, so the simpler unconditional
///   replacement is used (same shape as the Bloomburrow / Abraded Bluffs tap
///   lands).
/// - Single-arg dispatcher path: no <see cref="ReplacementBus"/> wired — the
///   enters-tapped replacement is omitted (shape-only posture).
///
/// ## References
///
/// - <see cref="EmeriaShatteredSkyclaveFactory"/> — companion ZNR MDFC back
///   face ({W} tap-for-one-mana land); this factory mirrors its JSON-identity +
///   MdfcState shape but swaps the pay-3-life replacement for the unconditional
///   enters-tapped one.
/// - <see cref="AbradedBluffsFactory"/> — unconditional
///   <see cref="EntersTappedReplacement"/> usage.
/// </summary>
[CardName("Kabira Plateau")]
public static class KabiraPlateauFactory
{
    public const string CardName = "Kabira Plateau";
    public const string FrontName = "Kabira Takedown";

    /// <summary>
    /// Construct Kabira Plateau without a <see cref="ReplacementBus"/>. The
    /// enters-tapped replacement is omitted; the {T}: Add {W} mana ability
    /// (from JSON) is still wired. Suitable for card-shape / dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Kabira Plateau with an optional <see cref="ReplacementBus"/>
    /// for full ETB wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the unconditional "this land
    /// enters tapped" replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the {T}: Add {W} mana ability come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("kabira-plateau");
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the back
        // face (Kabira Plateau is the back face that actually exists on the
        // battlefield).
        var mdfc = new MdfcState(FrontName, CardName);
        mdfc.Transform();
        land.MdfcState = mdfc;

        // ----------------------------------------------------------------
        // ETB: "This land enters tapped." (CR 614.1c) — unconditional.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
