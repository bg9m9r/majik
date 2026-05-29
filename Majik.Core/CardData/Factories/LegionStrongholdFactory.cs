using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Legion Leadership // Legion Stronghold (Modern Horizons 3).
///
/// Land. Oracle text (back):
///   "This land enters tapped."
///   "{T}: Add {R} or {W}."
///
/// Front face — <see cref="LegionLeadershipFactory"/> (Instant {1}{R/W}
/// — "Until end of turn, double target creature's power and it gains
/// first strike.").
///
/// ## MDFC infra
/// See <see cref="LegionLeadershipFactory"/>'s class doc for the
/// cast-either-face design. This factory is the back-face dispatch arm:
/// when a player chooses to play the MDFC as a land,
/// <see cref="NamedCardFactory"/> resolves the back-face name
/// <c>"Legion Stronghold"</c> and lands here. The card is constructed
/// with its <see cref="MdfcState"/> pre-flipped to the back face.
///
/// ## Implemented (v1)
/// - Non-Basic <see cref="Land"/>, no printed subtype.
///   Owner / controller wired.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face
///   (mirrors <see cref="SoporificSpringsFactory"/> /
///   <see cref="RazorgrassFieldFactory"/> back-face posture).
/// - <b>"This land enters tapped"</b> — unconditional
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Single-arg path (no bus) omits the
///   ETB replacement; same shape-only posture as
///   <see cref="NeedleSpiresFactory"/>.
/// - <b>{T}: Add {R} or {W}</b> — two <see cref="ManaAbility"/> instances
///   (one producing {R}, one producing {W}), CR 605.1. Controller picks
///   which to activate. Same dual-mana pattern as
///   <see cref="NeedleSpiresFactory"/>.
/// </summary>
[CardName("Legion Stronghold")]
public static class LegionStrongholdFactory
{
    public const string CardName = "Legion Stronghold";
    public const string FrontName = "Legion Leadership";

    /// <summary>
    /// Construct Legion Stronghold without a <see cref="ReplacementBus"/>.
    /// The ETB-tapped replacement is omitted; both {T}: Add {R} / {W}
    /// mana abilities are still wired. Suitable for shape / dispatcher
    /// tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Legion Stronghold with an optional <see cref="ReplacementBus"/>
    /// for full ETB-tapped wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the unconditional
    /// "enters tapped" replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Legion Stronghold is a vanilla nonbasic land — no basic land
        // subtype, no supertype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the
        // back face (Legion Stronghold is the back face that actually
        // exists on the battlefield).
        var mdfc = new MdfcState(FrontName, CardName);
        mdfc.Transform();
        land.MdfcState = mdfc;

        // ----------------------------------------------------------------
        // ETB: "This land enters tapped." (CR 614.1c)
        // Unconditional replacement — no life-payment choice.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {T}: Add {R}   (CR 605.1 — mana ability, no stack)
        // {T}: Add {W}   Controller activates the appropriate one.
        // Same dual-mana pattern as NeedleSpiresFactory.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("R")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("W")));

        return land;
    }
}
