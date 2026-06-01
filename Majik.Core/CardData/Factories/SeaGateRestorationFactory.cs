using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Sea Gate Restoration // Sea Gate, Reborn (Zendikar Rising, {4}{U}{U}{U}).
///
/// Sorcery. Oracle text (front, verified against Scryfall):
///   "Draw cards equal to the number of cards in your hand plus one. You have
///    no maximum hand size for the rest of the game."
///
/// Back face — <see cref="SeaGateRebornFactory"/> (Land —
/// "As this land enters, you may pay 3 life. If you don't, it enters tapped."
/// / "{T}: Add {U}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="AgadeemsAwakeningFactory"/> /
/// <see cref="AgadeemTheUndercryptFactory"/> (the ZNR spell//pay-3-life-land
/// MDFC pair this factory directly mirrors).
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>sea-gate-restoration.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
/// tracker and the resolve-time draw behaviour are attached in code (the
/// JSON schema models neither MDFC faces nor count-from-hand draws).
///
/// ## Implemented (v1)
///
/// - Sorcery identity at <c>{4}{U}{U}{U}</c>, mono-blue (three {U} pips),
///   owner / controller wired.
/// - <see cref="MdfcState"/> attached (front = "Sea Gate Restoration",
///   back = "Sea Gate, Reborn"); starts on the front face.
/// - No targets, not an X-spell.
/// - Resolution (CR 121.1 / CR 608.2): "Draw cards equal to the number of
///   cards in your hand plus one." The hand count is sampled at resolution
///   (CR 608.2 — instructions use the game state when the spell began
///   resolving) and <c>count + 1</c> cards are drawn through
///   <see cref="Fx.DrawCards"/> so the replacement bus gets a shot per draw
///   and the empty-library SBA flag (CR 704.5c) is stamped if the library
///   runs out.
///
/// ## Deferred (v1 gaps)
///
/// - <b>"You have no maximum hand size for the rest of the game."</b>
///   (CR 402.2 — the 7-card maximum hand size; CR 103.5 / cleanup-step
///   discard). The engine does not yet enforce a maximum hand size at all:
///   <see cref="Majik.Core.Game.Phases.CleanupStep.DiscardToHandSize"/> is
///   not wired into the cleanup step (it is never invoked with a limit), so
///   no player is ever forced to discard down to 7. With no maximum hand
///   size to remove, this rider is a no-op against the current engine — the
///   observable draw is fully modelled. When a real maximum-hand-size
///   mechanic lands, this clause should set a per-player "no maximum hand
///   size" flag consulted by the cleanup step. Tracked as the only gap; the
///   card's primary effect (the draw) is complete.
///
/// ## References
///
/// - <see cref="AgadeemsAwakeningFactory"/> — companion ZNR MDFC front face
///   with the same JSON-identity + MdfcState shape (this factory mirrors its
///   structure, swapping the X-spell graveyard return for the draw clause).
/// - <see cref="ReadTheBonesFactory"/> / <see cref="VisionsOfBeyondFactory"/>
///   — <see cref="Fx.DrawCards"/>-routed draw bodies.
/// </summary>
[CardName("Sea Gate Restoration")]
public static class SeaGateRestorationFactory
{
    public const string CardName = "Sea Gate Restoration";
    public const string BackName = "Sea Gate, Reborn";

    /// <summary>
    /// Construct Sea Gate Restoration as a Sorcery (identity from JSON) with
    /// the <see cref="MdfcState"/> face tracker attached. The resolve-time
    /// <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + printed cost come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("sea-gate-restoration");
        var card = (Sorcery)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name is observable from the front-face card object.
        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor (deferral #3, real cast-either-face). The
        // back face is the LAND back face played with no stack; MdfcCastFlow
        // offers the controller a face choice at cast time and materializes
        // a fresh back-face land instance when chosen. No transform happens.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                SeaGateRebornFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        return card;
    }

    /// <summary>
    /// Build the resolve-time "draw cards equal to the number of cards in
    /// your hand plus one" <see cref="SpellDefinition"/> (no targets, not an
    /// X-spell).
    /// </summary>
    /// <param name="caster">Spell controller — the hand counted and drawn
    /// into (CR 121.1 — "your hand").</param>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster));
    }

    /// <summary>
    /// Build Sea Gate Restoration's resolve effect — draw (hand size + 1)
    /// cards. The "no maximum hand size for the rest of the game" rider is a
    /// no-op against the current engine (see class doc) and is intentionally
    /// not modelled.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            Fx.Inline(
                $"{CardName}: draw (cards in hand + 1) cards.",
                () =>
                {
                    // CR 608.2 — sample the hand size at resolution.
                    var handCount = caster.Zones.Hand.GetCards().Count();

                    // CR 121.1 — draw (hand size + 1). Routed through
                    // Fx.DrawCards so the replacement bus gets a shot per
                    // draw and an empty library stamps the draw-from-empty
                    // SBA flag (CR 704.5c).
                    Fx.DrawCards(caster, handCount + 1);
                }),
        };
    }
}
