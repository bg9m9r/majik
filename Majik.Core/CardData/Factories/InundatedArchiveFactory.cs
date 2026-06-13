using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Waterlogged Teachings // Inundated Archive (Modern Horizons 3).
///
/// Land. Oracle text (back, verified against Scryfall):
///   "This land enters tapped."
///   "{T}: Add {U} or {B}."
///
/// Front face — <see cref="WaterloggedTeachingsFactory"/> (Instant {3}{U/B} —
/// "Search your library for an instant card or a card with flash, reveal it,
/// put it into your hand, then shuffle.").
///
/// ## MDFC infra
///
/// See <see cref="WaterloggedTeachingsFactory"/>'s class doc for the
/// cast-either-face design. This factory is the back-face dispatch arm:
/// when a player chooses to play the MDFC as a land,
/// <see cref="NamedCardFactory"/> resolves the back-face name
/// <c>"Inundated Archive"</c> and lands here. The card is constructed with
/// its <see cref="MdfcState"/> pre-flipped to the back face so the face
/// tracker reads as authoritative. Mirrors <see cref="JwariRuinsFactory"/>
/// exactly (the structurally identical instant // tapland MDFC back face),
/// differing only in producing two colours ({U}/{B}) like the dual tapland
/// <see cref="SubmergedBoneyardFactory"/>.
///
/// ## Card identity comes from JSON
///
/// Name / type and the two <b>{T}: Add {U}</b> / <b>{T}: Add {B}</b> mana
/// abilities are loaded from the embedded JSON definition
/// (<c>inundated-archive.json</c>) via
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
/// - <b>{T}: Add {U} or {B}</b> — two <see cref="Abilities.ManaAbility"/>
///   instances, one per colour (CR 605.1 — mana abilities, no stack), from
///   JSON.
/// - <b>ETB "This land enters tapped." (CR 614.1c)</b> — modelled via the
///   <em>unconditional</em> <see cref="EntersTappedReplacement"/> on the
///   supplied <see cref="ReplacementBus"/>. Inundated Archive has no opt-out:
///   it always enters tapped.
/// - Single-arg dispatcher path: no <see cref="ReplacementBus"/> wired —
///   the ETB replacement is omitted (shape-only posture).
///
/// ## References
///
/// - <see cref="JwariRuinsFactory"/> — the structurally identical instant //
///   tapland MDFC back face this directly mirrors.
/// - <see cref="SubmergedBoneyardFactory"/> — the {U}/{B} tapped dual whose
///   two-mana-ability JSON shape this matches.
/// </summary>
[CardName("Inundated Archive")]
public static class InundatedArchiveFactory
{
    public const string CardName = "Inundated Archive";
    public const string FrontName = "Waterlogged Teachings";

    /// <summary>
    /// Construct Inundated Archive without a <see cref="ReplacementBus"/>. The
    /// ETB-tapped replacement is omitted; the two {T}: Add {U}/{B} mana
    /// abilities (from JSON) are still wired. Suitable for card-shape /
    /// dispatcher tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Inundated Archive with an optional <see cref="ReplacementBus"/>
    /// for full ETB wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the unconditional "this land
    /// enters tapped" replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the two {T}: Add {U} / {T}: Add {B} mana abilities come
        // from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("inundated-archive");
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the back
        // face (Inundated Archive is the back face that actually exists on the
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
