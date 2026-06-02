using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Blackbloom Rogue // Blackbloom Bog (Zendikar Rising).
///
/// Land. Oracle text (back, verified against Scryfall 2026-06):
///   "This land enters tapped."
///   "{T}: Add {B}."
///
/// Front face — <see cref="BlackbloomRogueFactory"/> (Creature — Human Rogue
/// {2}{B} 2/3 with Menace + a graveyard-conditional self-pump).
///
/// ## MDFC infra
///
/// See <see cref="BlackbloomRogueFactory"/>'s class doc for the
/// cast-either-face design. This factory is the back-face dispatch arm: when a
/// player chooses to play the MDFC as a land, <see cref="NamedCardFactory"/>
/// resolves the back-face name <c>"Blackbloom Bog"</c> and lands here. The card
/// is constructed with its <see cref="MdfcState"/> pre-flipped to the back face
/// so the face tracker reads as authoritative even though the back face is the
/// permanent that actually exists. Mirrors <see cref="AkoumTeethFactory"/>.
///
/// ## Card identity comes from JSON
///
/// Name / type and the <b>{T}: Add {B}</b> mana ability are loaded from the
/// embedded JSON definition (<c>blackbloom-bog.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The MDFC face tracker and the ETB
/// "enters tapped" replacement are attached in code (the JSON schema models
/// neither).
///
/// ## Implemented (v1)
///
/// - Non-Basic <see cref="Land"/> with no printed subtype. Owner / controller
///   wired (from JSON).
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face (mirrors
///   <see cref="AkoumTeethFactory"/>).
/// - <b>{T}: Add {B}</b> — single <see cref="ManaAbility"/> producing one black
///   mana (CR 605.1 — mana ability, no stack), from JSON.
/// - <b>ETB "This land enters tapped." (CR 614.1c)</b> — modelled via the
///   unconditional <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Blackbloom Bog ALWAYS enters tapped — no
///   choice, no life payment — so the plain
///   <see cref="EntersTappedReplacement"/> is the right primitive.
/// - Single-arg dispatcher path: no <see cref="ReplacementBus"/> wired — the
///   ETB replacement is omitted (shape-only posture matching
///   <see cref="AkoumTeethFactory"/>'s no-bus overload).
///
/// ## References
///
/// - <see cref="AkoumTeethFactory"/> — sibling ZNR MDFC back-face tapland that
///   taps for a single color; this factory mirrors its MDFC / mana-ability /
///   unconditional enters-tapped wiring (swapping red for black).
/// </summary>
[CardName("Blackbloom Bog")]
public static class BlackbloomBogFactory
{
    public const string CardName = "Blackbloom Bog";
    public const string FrontName = "Blackbloom Rogue";
    public const string Slug = "blackbloom-bog";

    /// <summary>
    /// Construct Blackbloom Bog without a <see cref="ReplacementBus"/>. The ETB
    /// "enters tapped" replacement is omitted; the {T}: Add {B} mana ability
    /// (from JSON) is still wired. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to. Suitable for card-shape /
    /// dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Blackbloom Bog with an optional <see cref="ReplacementBus"/>
    /// for full ETB wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the unconditional
    /// "this land enters tapped" replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the {T}: Add {B} mana ability come from JSON.
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
