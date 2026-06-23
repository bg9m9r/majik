using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Adventuring Gear (Zendikar, {1}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified 2026-06-23):
///   "Landfall — Whenever a land you control enters, equipped creature gets
///    +2/+2 until end of turn.
///    Equip {1} ({1}: Attach to target creature you control. Equip only as a
///    sorcery.)"
///
/// ## Why a hand-rolled C# factory (not the pure JSON CardDefinition path)
///
/// The data-driven
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/> only
/// supports the effect/ability shapes enumerated in its dispatch; it has NO
/// equip ability and NO landfall trigger, so the shipped
/// <c>adventuring-gear.json</c> mirrors <c>lavaspur-boots.json</c> /
/// <c>nettlecyst.json</c> and carries only the vanilla Artifact shell. The
/// functioning equipment analogues (<see cref="LavaspurBootsFactory"/>,
/// <see cref="ColossusHammerFactory"/>) are likewise hand-rolled, so this
/// follows that established pattern.
///
/// ## Implementation
///
/// - <b>Landfall — "equipped creature gets +2/+2 until end of turn"</b>
///   (CR 603.1 / 603.6a / CR 702.142). The same landfall predicate as
///   <see cref="SteppeLynxFactory"/> / <see cref="PlatedGeopedeFactory"/>
///   (<see cref="Triggers.OnLandEntersUnderControl"/>). The difference from
///   those self-pumping creatures: the pump lands on the EQUIPPED creature
///   (the Gear's <see cref="Permanent.AttachedTo"/>), not the Gear itself.
///   On resolve, if the Gear is attached to a creature, a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+2, +2) is registered on that
///   creature's <see cref="Creature.ActiveEffects"/> (Layer 7c CR 613.1g;
///   expiry CR 514.2). If unattached (or its bearer has no live
///   <see cref="ContinuousEffectsService"/> — shape-only tests), the resolve
///   is a no-op, matching the printed "equipped creature" phrasing: with no
///   equipped creature, the ability does nothing on resolution
///   (CR 608.2b — an instruction about an object that no longer exists / is
///   absent is ignored).
/// - <b>Equip {1}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive, threading the
///   Puresteel-Paladin zero-equip cost-provider hook for cycle parity.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits the live
/// <see cref="TriggerManager"/> wiring and produces the correct card shape
/// only (factory-shape / dispatch tests). Use the two-arg overload to
/// register the landfall trigger with a bus for live firing.
///
/// ## Deferred (v1 gaps)
/// - <b>Trigger registration</b>: the shape-only <see cref="Create(Player)"/>
///   path attaches the landfall trigger for inspection but does not register
///   it with a bus.
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically (same gap as the rest of the
///   equipment cycle).
/// </summary>
[CardName("Adventuring Gear")]
public static class AdventuringGearFactory
{
    public const string CardName = "Adventuring Gear";
    public const string Slug = "adventuring-gear";
    public const string Cost = "{1}";
    public const string EquipCost = "{1}";

    /// <summary>Layer 7c +P/+T magnitude granted to the equipped creature on
    /// each landfall (CR 613.1g).</summary>
    public const int PumpAmount = 2;

    /// <summary>
    /// Construct Adventuring Gear with no live <see cref="TriggerManager"/>
    /// wiring. The landfall trigger is attached for shape inspection but not
    /// registered with a bus. Suitable for shape / dispatcher tests. This is
    /// the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Adventuring Gear. When <paramref name="triggers"/> is supplied
    /// the landfall trigger is registered so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> for a land entering under
    /// the controller's control automatically queues the +2/+2 pump on the
    /// equipped creature.
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Artifact,
        // Equipment subtype, {1}). The JSON carries no abilities — the
        // landfall trigger + Equip are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Artifact)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Landfall — CR 603.1 / 603.6a / CR 702.142.
        //   "Whenever a land you control enters, equipped creature gets
        //    +2/+2 until end of turn."
        // Predicate shared with Steppe Lynx / Plated Geopede / Hedron Crab.
        // Unlike those self-pumpers, the pump lands on the EQUIPPED creature
        // (the Gear's AttachedTo). On resolve, register a +2/+2
        // PumpUntilEndOfTurnEffect (Layer 7c CR 613.1g; expiry CR 514.2) on
        // the equipped creature's own ActiveEffects. If unattached — or the
        // bearer has no live ContinuousEffectsService (shape-only tests) —
        // the resolve is a no-op (CR 608.2b: no equipped creature => nothing
        // to pump).
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: landfall — equipped creature gets +{PumpAmount}/+{PumpAmount} until end of turn",
            () =>
            {
                if (card.AttachedTo is Creature equipped)
                {
                    equipped.ActiveEffects?.Register(
                        new PumpUntilEndOfTurnEffect(equipped, PumpAmount, PumpAmount));
                }
            });

        var landfallTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLandEntersUnderControl(owner),
            effects: new IEffect[] { pumpEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(landfallTrigger);
        triggers?.RegisterTriggeredAbility(landfallTrigger);

        // ----------------------------------------------------------------
        // Equip {1} — standard equipment-cycle Equip activated ability
        // (CR 702.6) via the shared primitive. Threads the Puresteel
        // zero-cost provider hook for cycle parity.
        // ----------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
