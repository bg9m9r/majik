using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Snapping Gnarlid (Zendikar, {1}{G}).
///
/// Creature — Beast 2/2. Oracle text (verified against Scryfall 2026-06):
///   "Landfall — Whenever a land you control enters, this creature gets
///    +1/+1 until end of turn."
///
/// The green landfall two-drop — the {1}{G} analogue of
/// <see cref="AkoumHellhoundFactory"/> / <see cref="SteppeLynxFactory"/>,
/// differing only in base stats (2/2 vs 0/1) and the smaller +1/+1 pump.
/// Base shape (name, Creature, Beast subtype, {1}{G}, 2/2) is materialised
/// from the embedded JSON definition (<c>snapping-gnarlid.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>, the same JSON-first base path
/// Akoum Hellhound uses. The landfall trigger is layered on here because the
/// JSON <c>AbilityDefinition</c> schema doesn't express landfall.
///
/// ## Implemented (v1)
/// - 2/2 Creature — Beast at {1}{G}, owner / controller wired (from JSON).
/// - <b>Landfall triggered ability</b> (CR 603.1 / 603.6a / CR 702.142)
///   — fires on a <see cref="Majik.Core.Events.CardMovedEvent"/> filtered to
///   "a land entering the battlefield under the controller's control" via the
///   shared <see cref="Triggers.OnLandEntersUnderControl"/> predicate (same
///   predicate as Akoum Hellhound / Steppe Lynx / Hedron Crab / Lotus Cobra).
///   No <see cref="Majik.Core.Targeting.TargetRequest"/>: the pump always
///   affects the Gnarlid itself, so there is nothing to target (CR 603.6a —
///   the effect names "this creature").
/// - <b>Resolve — +1/+1 until end of turn</b>: registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+1, +1) on the Gnarlid's own
///   <see cref="Creature.ActiveEffects"/> (Layer 7c CR 613.1g; expiry
///   CR 514.2 — the cleanup step). When <see cref="Creature.ActiveEffects"/>
///   is null (shape-only tests with no live
///   <see cref="ContinuousEffectsService"/>) the registration is a no-op,
///   mirroring <see cref="AkoumHellhoundFactory"/> / Giant Growth.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only; the landfall trigger is
///   attached for inspection but not registered with a bus. This is the
///   overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager)"/> — fully wired; the trigger
///   is registered so a land entering under the controller's control queues
///   the ability automatically.
/// </summary>
[CardName("Snapping Gnarlid")]
public static class SnappingGnarlidFactory
{
    public const string CardName = "Snapping Gnarlid";
    public const string Slug = "snapping-gnarlid";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>Layer 7c +P/+T magnitude granted on each landfall
    /// (CR 613.1g).</summary>
    public const int PumpAmount = 1;

    /// <summary>
    /// Construct Snapping Gnarlid with no live <see cref="TriggerManager"/>
    /// wiring. The landfall trigger is attached for shape inspection but not
    /// registered with a bus. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Snapping Gnarlid. When <paramref name="triggers"/> is supplied
    /// the landfall trigger is registered so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> for a land entering
    /// under the controller's control automatically queues the ability.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Beast
        // subtype, {1}{G}, 2/2). The JSON carries no abilities — the landfall
        // trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Landfall — CR 603.1 / 603.6a / CR 702.142.
        //   "Whenever a land you control enters, this creature gets +1/+1
        //    until end of turn."
        // Predicate is shared with Akoum Hellhound / Steppe Lynx / Hedron
        // Crab / Lotus Cobra. No target: the pump always affects the Gnarlid
        // itself. On resolve, register a self-targeted +1/+1
        // PumpUntilEndOfTurnEffect (Layer 7c CR 613.1g; expiry CR 514.2) on
        // the Gnarlid's own ActiveEffects — the same pump primitive used by
        // Giant Growth.
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: landfall — this creature gets +{PumpAmount}/+{PumpAmount} until end of turn",
            () =>
            {
                // ActiveEffects is null in shape-only tests (no live
                // ContinuousEffectsService) — no-op, mirroring Akoum Hellhound.
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
