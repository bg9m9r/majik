using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Khalni Ambush // Khalni Territory (Zendikar Rising).
///
/// Land. Oracle text (back):
///   "This land enters tapped."
///   "{T}: Add {G}."
///
/// Front face — <see cref="KhalniAmbushFactory"/> (Instant {2}{G} —
/// "Target creature you control fights target creature you don't control.").
///
/// ## MDFC infra
/// See <see cref="KhalniAmbushFactory"/>'s class doc for the cast-either-face
/// design. This factory is the back-face dispatch arm: when a player chooses
/// to play the MDFC as a land, <see cref="NamedCardFactory"/> resolves the
/// back-face name <c>"Khalni Territory"</c> and lands here. The card is
/// constructed with its <see cref="MdfcState"/> pre-flipped to the back face.
///
/// ## Shape source
/// Card identity (name, Land, {T}: Add {G} mana ability) is loaded from
/// <c>Majik.Core/CardData/Cards/khalni-ambush.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="BalaGedSanctuaryFactory"/>. The "enters tapped" replacement
/// (CR 614.1c) is not expressible in the JSON ability schema, so it is wired
/// in code below onto the supplied <see cref="ReplacementBus"/>.
///
/// ## Implemented (v1)
/// - Non-Basic <see cref="Land"/>, no printed subtype. Owner / controller
///   wired.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face
///   (mirrors <see cref="BalaGedSanctuaryFactory"/>).
/// - <b>"This land enters tapped"</b> — unconditional
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/> (CR 614.1c). Single-arg path (no bus)
///   omits the ETB replacement; same shape-only posture as
///   <see cref="BalaGedSanctuaryFactory"/>.
/// - <b>{T}: Add {G}</b> — single <see cref="ManaAbility"/> producing one
///   green mana (CR 605.1 — mana ability, no stack), supplied by the JSON
///   definition.
///
/// ## References
/// - <see cref="BalaGedSanctuaryFactory"/> — identical tapland + {T}: Add {G}
///   shape (back face of the Bala Ged Recovery MDFC pair); this factory
///   directly mirrors it.
/// </summary>
[CardName("Khalni Territory")]
public static class KhalniTerritoryFactory
{
    public const string CardName = "Khalni Territory";
    public const string FrontName = "Khalni Ambush";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("khalni-ambush");

    /// <summary>
    /// Construct Khalni Territory without a <see cref="ReplacementBus"/>.
    /// The ETB-tapped replacement is omitted; the {T}: Add {G} mana
    /// ability is still wired. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Khalni Territory with an optional <see cref="ReplacementBus"/>
    /// for full ETB-tapped wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the unconditional
    /// "enters tapped" replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);
        land.SetOwner(owner);
        land.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the
        // back face (Khalni Territory is the back face that actually exists
        // on the battlefield).
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
