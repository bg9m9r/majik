using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Silundi Vision // Silundi Isle (Zendikar Rising).
///
/// Land. Oracle text (back, verified against Scryfall):
///   "This land enters tapped."
///   "{T}: Add {U}."
///
/// Front face — <see cref="SilundiVisionFactory"/> (Instant {2}{U} —
/// "Look at the top six cards of your library. You may reveal an instant or
/// sorcery card from among them and put it into your hand. Put the rest on
/// the bottom of your library in a random order.").
///
/// ## MDFC infra
///
/// See <see cref="SilundiVisionFactory"/>'s class doc for the
/// cast-either-face design. This factory is the back-face dispatch arm:
/// when a player chooses to play the MDFC as a land,
/// <see cref="NamedCardFactory"/> resolves the back-face name
/// <c>"Silundi Isle"</c> and lands here. The card is constructed with its
/// <see cref="MdfcState"/> pre-flipped to the back face so the face tracker
/// reads as authoritative.
///
/// ## Card identity comes from JSON
///
/// Name / type and the <b>{T}: Add {U}</b> mana ability are loaded from the
/// embedded JSON definition (<c>silundi-isle.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The MDFC face tracker and the
/// unconditional ETB-tapped replacement are attached in code (the JSON
/// schema models neither).
///
/// ## Implemented (v1)
///
/// - Non-Basic <see cref="Land"/> with no printed subtype.
///   Owner / controller wired.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face
///   (mirrors <see cref="SoporificSpringsFactory"/>).
/// - <b>{T}: Add {U}</b> — single <see cref="Abilities.ManaAbility"/>
///   producing one blue mana (CR 605.1 — mana ability, no stack), from JSON.
/// - <b>ETB "This land enters tapped." (CR 614.1c)</b> — modelled via the
///   <em>unconditional</em> <see cref="EntersTappedReplacement"/> on the
///   supplied <see cref="ReplacementBus"/>. Unlike the pay-3-life back-face
///   lands (<see cref="SoporificSpringsFactory"/>,
///   <see cref="AgadeemTheUndercryptFactory"/>) Silundi Isle has no opt-out:
///   it always enters tapped.
/// - Single-arg dispatcher path: no <see cref="ReplacementBus"/> wired —
///   the ETB replacement is omitted (shape-only posture matching the rest of
///   the back-face land family).
///
/// ## References
///
/// - <see cref="SoporificSpringsFactory"/> — companion instant//land MDFC
///   back-face land; this factory mirrors its structure but uses the
///   unconditional <see cref="EntersTappedReplacement"/> in place of the
///   conditional pay-3-life replacement.
/// - <see cref="JwarIsleRefugeFactory"/> — plain "this land enters tapped"
///   blue-producing land showing the unconditional ETB-tapped posture.
/// </summary>
[CardName("Silundi Isle")]
public static class SilundiIsleFactory
{
    public const string CardName = "Silundi Isle";
    public const string FrontName = "Silundi Vision";

    /// <summary>
    /// Construct Silundi Isle without a <see cref="ReplacementBus"/>. The
    /// ETB-tapped replacement is omitted; the {T}: Add {U} mana ability
    /// (from JSON) is still wired. Suitable for card-shape / dispatcher
    /// tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Silundi Isle with an optional <see cref="ReplacementBus"/>
    /// for full ETB wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the unconditional "this
    /// land enters tapped" replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the {T}: Add {U} mana ability come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("silundi-isle");
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the
        // back face (Silundi Isle is the back face that actually exists on
        // the battlefield).
        var mdfc = new MdfcState(FrontName, CardName);
        mdfc.Transform();
        land.MdfcState = mdfc;

        // ----------------------------------------------------------------
        // ETB: "This land enters tapped." (CR 614.1c) — unconditional, no
        // opt-out. Registered only when a bus is supplied.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
