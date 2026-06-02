using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Jwari Disruption // Jwari Ruins (Zendikar Rising).
///
/// Land. Oracle text (back, verified against Scryfall):
///   "This land enters tapped."
///   "{T}: Add {U}."
///
/// Front face — <see cref="JwariDisruptionFactory"/> (Instant {1}{U} —
/// "Counter target spell unless its controller pays {1}.").
///
/// ## MDFC infra
///
/// See <see cref="JwariDisruptionFactory"/>'s class doc for the
/// cast-either-face design. This factory is the back-face dispatch arm:
/// when a player chooses to play the MDFC as a land,
/// <see cref="NamedCardFactory"/> resolves the back-face name
/// <c>"Jwari Ruins"</c> and lands here. The card is constructed with its
/// <see cref="MdfcState"/> pre-flipped to the back face so the face tracker
/// reads as authoritative. Mirrors <see cref="SilundiIsleFactory"/> exactly
/// (the structurally identical Zendikar Rising blue instant // tapland MDFC).
///
/// ## Card identity comes from JSON
///
/// Name / type and the <b>{T}: Add {U}</b> mana ability are loaded from the
/// embedded JSON definition (<c>jwari-ruins.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The MDFC face tracker and the
/// unconditional ETB-tapped replacement are attached in code (the JSON
/// schema models neither).
///
/// ## Implemented (v1)
///
/// - Non-Basic <see cref="Land"/> with no printed subtype.
///   Owner / controller wired.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face.
/// - <b>{T}: Add {U}</b> — single <see cref="Abilities.ManaAbility"/>
///   producing one blue mana (CR 605.1 — mana ability, no stack), from JSON.
/// - <b>ETB "This land enters tapped." (CR 614.1c)</b> — modelled via the
///   <em>unconditional</em> <see cref="EntersTappedReplacement"/> on the
///   supplied <see cref="ReplacementBus"/>. Jwari Ruins has no opt-out: it
///   always enters tapped (unlike the pay-3-life back-face lands).
/// - Single-arg dispatcher path: no <see cref="ReplacementBus"/> wired —
///   the ETB replacement is omitted (shape-only posture).
///
/// ## References
///
/// - <see cref="SilundiIsleFactory"/> — the structurally identical Zendikar
///   Rising blue instant // tapland MDFC back face this directly mirrors.
/// </summary>
[CardName("Jwari Ruins")]
public static class JwariRuinsFactory
{
    public const string CardName = "Jwari Ruins";
    public const string FrontName = "Jwari Disruption";

    /// <summary>
    /// Construct Jwari Ruins without a <see cref="ReplacementBus"/>. The
    /// ETB-tapped replacement is omitted; the {T}: Add {U} mana ability
    /// (from JSON) is still wired. Suitable for card-shape / dispatcher
    /// tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Jwari Ruins with an optional <see cref="ReplacementBus"/>
    /// for full ETB wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the unconditional "this
    /// land enters tapped" replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the {T}: Add {U} mana ability come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("jwari-ruins");
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the
        // back face (Jwari Ruins is the back face that actually exists on
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
