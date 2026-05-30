using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Plated Geopede (Zendikar, {1}{R}).
///
/// Creature — Insect 1/1. Oracle text (verified against Scryfall):
///   "First strike
///    Landfall — Whenever a land you control enters, this creature gets
///    +2/+2 until end of turn."
///
/// The red sibling of <see cref="SteppeLynxFactory"/>: a landfall aggro
/// two-drop that swings as a first-striking 3/3 every turn you make a land
/// drop. Base shape (name, Creature, Insect subtype, {1}{R}, 1/1) is
/// materialised from the embedded JSON definition (<c>plated-geopede.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; First strike and the landfall
/// trigger are layered on top (the JSON <c>AbilityDefinition</c> schema does
/// not yet express keyword markers or landfall triggers — same posture as
/// the other JSON-backed cards whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - 1/1 Creature — Insect, mana cost {1}{R}, owner / controller wired.
/// - <b>First strike (CR 702.7)</b> — attached as a <see cref="KeywordAbility"/>
///   marker so <see cref="Majik.Core.Combat.CombatAbilities.HasFirstStrike"/>
///   surfaces; the combat-damage step assigns first-strike damage before
///   regular damage. Same shape as <see cref="YouthfulKnightFactory"/>.
/// - <b>Landfall triggered ability</b> (CR 603.1 / 603.6a / CR 702.142) —
///   fires on a <see cref="Majik.Core.Events.CardMovedEvent"/> filtered to
///   "a land entering the battlefield under the controller's control" via the
///   shared <see cref="Triggers.OnLandEntersUnderControl"/> predicate. No
///   <see cref="TargetRequest"/>: the pump always affects the Geopede itself
///   (CR 603.6a — the effect names "this creature").
/// - <b>Resolve — +2/+2 until end of turn</b>: registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+2, +2) on the Geopede's own
///   <see cref="Creature.ActiveEffects"/> (Layer 7c CR 613.1g; expiry
///   CR 514.2). When <see cref="Creature.ActiveEffects"/> is null (shape-only
///   tests with no live <see cref="Majik.Core.Services.ContinuousEffectsService"/>)
///   the registration is a no-op — mirrors <see cref="SteppeLynxFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Trigger registration</b>: the shape-only <see cref="Create(Player)"/>
///   path attaches the trigger for inspection but does not register it with a
///   bus. Use the <see cref="Create(Player, TriggerManager)"/> overload for
///   live firing.
/// </summary>
[CardName("Plated Geopede")]
public static class PlatedGeopedeFactory
{
    public const string CardName = "Plated Geopede";
    public const string Slug = "plated-geopede";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>Layer 7c +P/+T magnitude granted on each landfall
    /// (CR 613.1g).</summary>
    public const int PumpAmount = 2;

    /// <summary>
    /// Construct Plated Geopede with no live <see cref="TriggerManager"/>
    /// wiring. First strike + the landfall trigger are attached for shape
    /// inspection; the landfall trigger is not registered with a bus.
    /// Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Plated Geopede. When <paramref name="triggers"/> is supplied
    /// the landfall trigger is registered so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> for a land entering
    /// under the controller's control automatically queues the ability.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Insect subtype, {1}{R}, 1/1). The JSON carries no abilities —
        // First strike + landfall are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.7 — First strike marker. Combat-damage step enforces
        // first-strike damage assignment before regular combat damage.
        card.AddAbility(new KeywordAbility("First strike", card, owner));

        // ----------------------------------------------------------------
        // Landfall — CR 603.1 / 603.6a / CR 702.142.
        //   "Whenever a land you control enters, this creature gets +2/+2
        //    until end of turn."
        // Predicate shared with Steppe Lynx / Hedron Crab / Lotus Cobra.
        // No target: the pump always affects the Geopede itself. On resolve,
        // register a self-targeted +2/+2 PumpUntilEndOfTurnEffect (Layer 7c
        // CR 613.1g; expiry CR 514.2) on the Geopede's own ActiveEffects.
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: landfall — this creature gets +{PumpAmount}/+{PumpAmount} until end of turn",
            () =>
            {
                // ActiveEffects is null in shape-only tests (no live
                // ContinuousEffectsService) — no-op, mirroring Steppe Lynx.
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
