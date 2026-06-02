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
/// Kazandu Mammoth // Kazandu Valley (Zendikar Rising).
///
/// Land. Oracle text (back, verified against Scryfall):
///   "This land enters tapped."
///   "{T}: Add {G}."
///
/// Front face — <see cref="KazanduMammothFactory"/> (Creature — Elephant
/// {1}{G}{G} 3/3 with a landfall +2/+2 trigger).
///
/// ## MDFC infra
///
/// See <see cref="KazanduMammothFactory"/>'s class doc for the
/// cast-either-face design. This factory is the back-face dispatch arm:
/// when a player chooses to play the MDFC as a land,
/// <see cref="NamedCardFactory"/> resolves the back-face name
/// <c>"Kazandu Valley"</c> and lands here. The card is constructed with its
/// <see cref="MdfcState"/> pre-flipped to the back face so the face tracker
/// reads as authoritative even though the back face is the permanent that
/// actually exists.
///
/// ## Card identity comes from JSON
///
/// Name / type and the <b>{T}: Add {G}</b> mana ability are loaded from the
/// embedded JSON definition (<c>kazandu-valley.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The MDFC face tracker and the ETB
/// "enters tapped" replacement are attached in code (the JSON schema models
/// neither).
///
/// ## Implemented (v1)
///
/// - Non-Basic <see cref="Land"/> with no printed subtype.
///   Owner / controller wired.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face (mirrors
///   <see cref="SoporificSpringsFactory"/>'s back-face posture).
/// - <b>{T}: Add {G}</b> — single <see cref="ManaAbility"/> producing one
///   green mana (CR 605.1 — mana ability, no stack), from JSON.
/// - <b>ETB "This land enters tapped." (CR 614.1c)</b> — modelled via the
///   unconditional <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Unlike <see cref="SoporificSpringsFactory"/>
///   (a "pay 3 life or enters tapped" conditional taps land), Kazandu Valley
///   ALWAYS enters tapped — no choice, no life payment — so the plain
///   <see cref="EntersTappedReplacement"/> is the right primitive.
/// - Single-arg dispatcher path: no <see cref="ReplacementBus"/> wired —
///   the ETB replacement is omitted (shape-only posture matching
///   <see cref="SoporificSpringsFactory"/>'s no-bus overload).
///
/// ## References
///
/// - <see cref="SoporificSpringsFactory"/> — sibling ZNR/BLB MDFC back-face
///   land that taps for a single color; this factory mirrors its MDFC /
///   mana-ability wiring (swapping the conditional pay-3-life replacement for
///   the plain unconditional enters-tapped one).
/// - <see cref="TurntimberSerpentineWoodFactory"/> — companion ZNR MDFC
///   back-face green tap land.
/// </summary>
[CardName("Kazandu Valley")]
public static class KazanduValleyFactory
{
    public const string CardName = "Kazandu Valley";
    public const string FrontName = "Kazandu Mammoth";
    public const string Slug = "kazandu-valley";

    /// <summary>
    /// Construct Kazandu Valley without a <see cref="ReplacementBus"/>. The
    /// ETB "enters tapped" replacement is omitted; the {T}: Add {G} mana
    /// ability (from JSON) is still wired. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to. Suitable for card-shape /
    /// dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Kazandu Valley with an optional <see cref="ReplacementBus"/>
    /// for full ETB wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the unconditional
    /// "this land enters tapped" replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the {T}: Add {G} mana ability come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the back
        // face (the land is the back face that actually exists on the
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
