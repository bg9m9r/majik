using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Suppression Ray // Orderly Plaza (Murders at Karlov Manor).
///
/// Land. Oracle text (back, verified against Scryfall):
///   "This land enters tapped."
///   "{T}: Add {W} or {U}."
///
/// Front face — <see cref="SuppressionRayFactory"/> (Sorcery {3}{W/U}{W/U} —
/// "Tap all creatures target player controls. You may pay any amount of {E}.
/// If you do, choose up to that many creatures tapped this way. Put a stun
/// counter on each of them.").
///
/// ## MDFC infra
///
/// See <see cref="SuppressionRayFactory"/>'s class doc for the
/// cast-either-face design. This factory is the back-face dispatch arm:
/// when a player chooses to play the MDFC as a land,
/// <see cref="NamedCardFactory"/> resolves the back-face name
/// <c>"Orderly Plaza"</c> and lands here. The card is constructed with its
/// <see cref="MdfcState"/> pre-flipped to the back face so the face tracker
/// reads as authoritative. Mirrors <see cref="InundatedArchiveFactory"/>
/// exactly (the structurally identical spell // tapland MDFC back face),
/// differing only in producing two colours ({W}/{U}) like the dual tapland
/// <see cref="SubmergedBoneyardFactory"/>.
///
/// ## Card identity comes from JSON
///
/// Name / type and the two <b>{T}: Add {W}</b> / <b>{T}: Add {U}</b> mana
/// abilities are loaded from the embedded JSON definition
/// (<c>orderly-plaza.json</c>) via
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
/// - <b>{T}: Add {W} or {U}</b> — two <see cref="Abilities.ManaAbility"/>
///   instances, one per colour (CR 605.1 — mana abilities, no stack), from
///   JSON.
/// - <b>ETB "This land enters tapped." (CR 614.1c)</b> — modelled via the
///   <em>unconditional</em> <see cref="EntersTappedReplacement"/> on the
///   supplied <see cref="ReplacementBus"/>. Orderly Plaza has no opt-out:
///   it always enters tapped.
/// - Single-arg dispatcher path: no <see cref="ReplacementBus"/> wired —
///   the ETB replacement is omitted (shape-only posture).
///
/// ## References
///
/// - <see cref="InundatedArchiveFactory"/> — the structurally identical
///   spell // tapland MDFC back face this directly mirrors.
/// - <see cref="SubmergedBoneyardFactory"/> — the tapped dual whose
///   two-mana-ability JSON shape this matches.
/// </summary>
[CardName("Orderly Plaza")]
public static class OrderlyPlazaFactory
{
    public const string CardName = "Orderly Plaza";
    public const string FrontName = "Suppression Ray";
    public const string Slug = "orderly-plaza";

    /// <summary>
    /// Construct Orderly Plaza without a <see cref="ReplacementBus"/>. The
    /// ETB-tapped replacement is omitted; the two {T}: Add {W}/{U} mana
    /// abilities (from JSON) are still wired. Suitable for card-shape /
    /// dispatcher tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Orderly Plaza with an optional <see cref="ReplacementBus"/>
    /// for full ETB wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the unconditional "this land
    /// enters tapped" replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the two {T}: Add {W} / {T}: Add {U} mana abilities come
        // from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the back
        // face (Orderly Plaza is the back face that actually exists on the
        // battlefield).
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
