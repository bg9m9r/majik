using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Glasspool Mimic // Glasspool Shore (Zendikar Rising, {2}{U}).
///
/// Creature — Shapeshifter Rogue 0/0. Oracle text (front, verified against
/// Scryfall):
///   "You may have this creature enter as a copy of a creature you control,
///    except it's a Shapeshifter Rogue in addition to its other types."
///
/// Back face — <see cref="GlasspoolShoreFactory"/> (Land — "This land enters
/// tapped." / "{T}: Add {U}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="AkoumWarriorFactory"/> / <see cref="AkoumTeethFactory"/> (ZNR
/// creature-front + tapland-back MDFC). The front-face card carries a castable
/// <see cref="MdfcFace.Land"/> back-face descriptor on its
/// <see cref="MdfcState"/> so <see cref="Majik.Core.Game.MdfcCastFlow"/> can
/// offer the controller a face choice at play time and materialize a fresh
/// back-face land instance (Glasspool Shore) when chosen. No transform happens
/// — only the chosen face exists (CR 712.4).
///
/// ## Card identity comes from JSON
///
/// Name / type / Shapeshifter Rogue subtypes / printed cost {2}{U} / printed
/// 0/0 P/T are loaded from the embedded JSON definition
/// (<c>glasspool-mimic.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
/// tracker, the enters-as-copy replacement, and the type-adding riders are
/// attached in code (the JSON <c>AbilityDefinition</c> schema models none of
/// these).
///
/// ## Implemented (v1)
///
/// - 0/0 Creature — Shapeshifter Rogue, mana cost {2}{U}, owner / controller
///   wired. Printed 0/0 per CR 706.10 — Glasspool Mimic's printed P/T is
///   overwritten by <see cref="CopyEffect"/> when it enters as a copy; if it
///   doesn't copy, the 0/0 dies to the state-based action per CR 704.5f. Same
///   printed-0/0 posture as <see cref="PhantasmalImageFactory"/>.
/// - <see cref="MdfcState"/> attached (front = "Glasspool Mimic", back =
///   "Glasspool Shore") with a castable <see cref="MdfcFace.Land"/> back face;
///   starts on the front face.
/// - <b>Enters-as-copy replacement (CR 706.10)</b> via the shared
///   <see cref="EntersAsCopyReplacement"/> with pool
///   <see cref="EntersAsCopyReplacement.CopyPool.BattlefieldYouControl"/>
///   — "a copy of a creature you control" restricts the source pool to the
///   controller's own battlefield (unlike Phantasmal Image's
///   <see cref="EntersAsCopyReplacement.CopyPool.AnyBattlefield"/>). The
///   replacement is registered against the supplied <see cref="ReplacementBus"/>
///   when the binder-aware overload is used; the single-arg dispatcher path
///   produces shape only.
/// - <b>"a Shapeshifter Rogue in addition to its other types"
///   (CR 613.1d Layer 4 type-adding rider)</b> via two
///   <see cref="AddSubtypeEffect"/> riders (Shapeshifter + Rogue). The printed
///   subtypes are already on the card; registering the riders keeps them
///   correct under a future <see cref="CopyEffect"/> that mirrors subtypes
///   too (at v1, CopyEffect mirrors P/T + keywords only, so the printed
///   Shapeshifter Rogue subtypes already stick). Same pattern as Phantasmal
///   Image's Illusion rider, here doubled because the rider adds two subtypes.
///
/// ## Deferred (v1 gaps)
///
/// - <b>"You may" choice</b> — <see cref="EntersAsCopyReplacement"/> auto-yes
///   when any candidate exists; no agent prompt yet. Tests cover "decline" by
///   leaving the controller's battlefield empty (no candidates → enters as the
///   printed 0/0). Same posture as Phantasmal Image.
///
/// ## References
///
/// - <see cref="PhantasmalImageFactory"/> — the enters-as-copy + Layer-4
///   subtype-rider analogue this factory mirrors (pool tightened to
///   BattlefieldYouControl; subtype rider doubled to Shapeshifter + Rogue; no
///   targeted-sacrifice trigger).
/// - <see cref="AkoumWarriorFactory"/> — sibling ZNR creature-front MDFC whose
///   castable-land-back MdfcState shape this factory reuses.
/// </summary>
[CardName("Glasspool Mimic")]
public static class GlasspoolMimicFactory
{
    public const string CardName = "Glasspool Mimic";
    public const string BackName = "Glasspool Shore";
    public const string Slug = "glasspool-mimic";

    /// <summary>
    /// Construct Glasspool Mimic with no live replacement / continuous-effects
    /// wiring. Identity (name / Creature / Shapeshifter Rogue / {2}{U} / 0/0)
    /// comes from the embedded JSON definition; the <see cref="MdfcState"/>
    /// with the castable land back face is layered on. The enters-as-copy
    /// replacement is NOT registered on this path (shape-only posture — no
    /// <see cref="ReplacementBus"/> available). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null, effects: null);

    /// <summary>
    /// Construct Glasspool Mimic with optional replacement-bus +
    /// continuous-effects wiring. When both are supplied:
    /// <list type="bullet">
    ///   <item>The enters-as-copy replacement (CR 706.10) is registered on
    ///         <paramref name="replacements"/> with pool
    ///         <see cref="EntersAsCopyReplacement.CopyPool.BattlefieldYouControl"/>
    ///         so a <see cref="ZoneService"/> move onto the battlefield triggers
    ///         a <see cref="CopyEffect"/> against a creature the controller
    ///         controls.</item>
    ///   <item>The "Shapeshifter Rogue in addition" riders are registered on
    ///         <paramref name="effects"/> as two <see cref="AddSubtypeEffect"/>
    ///         instances (CR 613.1d Layer 4).</item>
    /// </list>
    /// </summary>
    public static Creature Create(
        Player owner,
        ReplacementBus? replacements,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Shapeshifter Rogue subtypes, {2}{U}, printed 0/0). The JSON carries
        // no abilities — the MDFC face tracker, enters-as-copy replacement, and
        // the Layer-4 subtype riders are layered on in code.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor (real cast-either-face). The back face is the
        // LAND back face played with no stack; MdfcCastFlow offers the
        // controller a face choice at play time and materializes a fresh
        // back-face land instance (wired to its ETB "enters tapped"
        // replacement via the supplied ReplacementBus) when chosen. No
        // transform happens.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, landReplacements) =>
                GlasspoolShoreFactory.Create(landOwner, landReplacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        // ----------------------------------------------------------------
        // Enters-as-copy replacement (CR 706.10). Reuses the shared
        // EntersAsCopyReplacement with pool BattlefieldYouControl —
        // "a copy of a creature you control". When the continuous-effects
        // service is supplied, the replacement registers a CopyEffect against
        // the entering Glasspool Mimic (using the v1 deterministic
        // first-candidate pick from the controller's battlefield).
        // ----------------------------------------------------------------
        if (replacements != null && effects != null)
        {
            replacements.Register(new EntersAsCopyReplacement(
                card,
                EntersAsCopyReplacement.CopyPool.BattlefieldYouControl,
                effects));

            // CR 613.1d Layer 4 — "except it's a Shapeshifter Rogue in
            // addition to its other types". The printed Shapeshifter + Rogue
            // subtypes are already on the card, but registering the riders
            // keeps them correct under a future CopyEffect that mirrors
            // subtypes (today CopyEffect handles P/T + keywords only).
            effects.Register(new AddSubtypeEffect(card, CardSubtype.Shapeshifter));
            effects.Register(new AddSubtypeEffect(card, CardSubtype.Rogue));

            // Plumb ContinuousEffects into the card so P/T lookups consult the
            // layer system (CR 613). The CopyEffect registered by the
            // replacement's Replace() callback writes the source's P/T, which
            // is read back via Creature.GetPower/GetToughness.
            card.ActiveEffects = effects;
        }

        return card;
    }
}
