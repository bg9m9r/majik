using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Kazandu Mammoth // Kazandu Valley (Zendikar Rising, {1}{G}{G}).
///
/// Creature — Elephant 3/3. Oracle text (front, verified against Scryfall):
///   "Landfall — Whenever a land you control enters, this creature gets
///    +2/+2 until end of turn."
///
/// Back face — <see cref="KazanduValleyFactory"/> (Land — "This land enters
/// tapped." / "{T}: Add {G}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="TurntimberSymbiosisFactory"/> /
/// <see cref="TurntimberSerpentineWoodFactory"/>. The front-face card carries
/// a castable <see cref="MdfcFace.Land"/> back-face descriptor on its
/// <see cref="MdfcState"/> so <see cref="Majik.Core.Game.MdfcCastFlow"/> can
/// offer the controller a face choice at play time and materialize a fresh
/// back-face land instance (Kazandu Valley) when chosen. No transform happens
/// — only the chosen face exists (CR 712.4).
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost / 3/3 P/T are loaded from the embedded JSON
/// definition (<c>kazandu-mammoth.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
/// tracker and the landfall trigger are attached in code (the JSON
/// <c>AbilityDefinition</c> schema models neither MDFC faces nor landfall
/// triggers).
///
/// ## Implemented (v1)
///
/// - 3/3 Creature — Elephant, mana cost {1}{G}{G}, owner / controller wired.
/// - <see cref="MdfcState"/> attached (front = "Kazandu Mammoth", back =
///   "Kazandu Valley") with a castable <see cref="MdfcFace.Land"/> back face;
///   starts on the front face.
/// - <b>Landfall triggered ability</b> (CR 603.1 / 603.6a / CR 702.142) —
///   fires on a <see cref="Majik.Core.Events.CardMovedEvent"/> filtered to
///   "a land entering the battlefield under the controller's control" via the
///   shared <see cref="Triggers.OnLandEntersUnderControl"/> predicate. No
///   <see cref="Targeting.TargetRequest"/>: the pump always affects the
///   Mammoth itself (CR 603.6a — the effect names "this creature"). Same
///   shape as <see cref="PlatedGeopedeFactory"/> (minus First strike).
/// - <b>Resolve — +2/+2 until end of turn</b>: registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+2, +2) on the Mammoth's own
///   <see cref="Creature.ActiveEffects"/> (Layer 7c CR 613.1g; expiry
///   CR 514.2). When <see cref="Creature.ActiveEffects"/> is null (shape-only
///   tests with no live <see cref="Majik.Core.Services.ContinuousEffectsService"/>)
///   the registration is a no-op — mirrors <see cref="PlatedGeopedeFactory"/>.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Trigger registration</b>: the shape-only <see cref="Create(Player)"/>
///   path attaches the trigger for inspection but does not register it with a
///   bus. Use the <see cref="Create(Player, TriggerManager)"/> overload for
///   live firing.
///
/// ## References
///
/// - <see cref="PlatedGeopedeFactory"/> — the landfall self-pump body this
///   directly cribs (+2/+2 until end of turn on a land you control entering).
/// - <see cref="TurntimberSymbiosisFactory"/> — companion ZNR MDFC front face
///   with the same castable-land-back MdfcState shape.
/// </summary>
[CardName("Kazandu Mammoth")]
public static class KazanduMammothFactory
{
    public const string CardName = "Kazandu Mammoth";
    public const string BackName = "Kazandu Valley";
    public const string Slug = "kazandu-mammoth";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>Layer 7c +P/+T magnitude granted on each landfall
    /// (CR 613.1g).</summary>
    public const int PumpAmount = 2;

    /// <summary>
    /// Construct Kazandu Mammoth with no live <see cref="TriggerManager"/>
    /// wiring. The landfall trigger is attached for shape inspection but not
    /// registered with a bus. The <see cref="MdfcState"/> with the castable
    /// land back face is attached. Suitable for shape / dispatcher tests.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Kazandu Mammoth. When <paramref name="triggers"/> is supplied
    /// the landfall trigger is registered so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> for a land entering
    /// under the controller's control automatically queues the ability.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elephant subtype, {1}{G}{G}, 3/3). The JSON carries no abilities —
        // the MDFC face tracker + landfall are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor (real cast-either-face). The back face is the
        // LAND back face played with no stack; MdfcCastFlow offers the
        // controller a face choice at cast/play time and materializes a fresh
        // back-face land instance (wired to its ETB "enters tapped"
        // replacement via the supplied ReplacementBus) when chosen. No
        // transform happens.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                KazanduValleyFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        // ----------------------------------------------------------------
        // Landfall — CR 603.1 / 603.6a / CR 702.142.
        //   "Whenever a land you control enters, this creature gets +2/+2
        //    until end of turn."
        // Predicate shared with Plated Geopede / Steppe Lynx / Hedron Crab.
        // No target: the pump always affects the Mammoth itself. On resolve,
        // register a self-targeted +2/+2 PumpUntilEndOfTurnEffect (Layer 7c
        // CR 613.1g; expiry CR 514.2) on the Mammoth's own ActiveEffects.
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: landfall — this creature gets +{PumpAmount}/+{PumpAmount} until end of turn",
            () =>
            {
                // ActiveEffects is null in shape-only tests (no live
                // ContinuousEffectsService) — no-op, mirroring Plated Geopede.
                card.ActiveEffects?.Register(
                    new PumpUntilEndOfTurnEffect(card, PumpAmount, PumpAmount));
            });

        var landfallTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLandEntersUnderControl(owner),
            effects: new IEffect[] { pumpEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(landfallTrigger);
        triggers?.RegisterTriggeredAbility(landfallTrigger);

        return card;
    }
}
