using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sazh's Chocobo (Final Fantasy, {G}).
///
/// Creature — Bird 0/1. Oracle text (verified against Scryfall 2026-06):
///   "Landfall — Whenever a land you control enters, put a +1/+1 counter on
///    this creature."
///
/// The green sibling of <see cref="AkoumHellhoundFactory"/> /
/// <see cref="PlatedGeopedeFactory"/>: a landfall one-drop that grows itself
/// on each land drop. The difference from those two is the resolve effect —
/// here landfall places a permanent <see cref="CounterType.PlusOnePlusOne"/>
/// counter on itself (CR 122, CR 613.7d) rather than a temporary
/// "until end of turn" pump, so the growth persists across turns. Same
/// self-affecting, no-target counter placement as
/// <see cref="BristlyBillSpineSowerFactory"/>'s landfall trigger, except the
/// counter always lands on this creature (the oracle names "this creature",
/// CR 603.6a) so there is nothing to target.
///
/// Base shape (name, Creature, Bird subtype, {G}, 0/1) is materialised from
/// the embedded JSON definition (<c>sazhs-chocobo.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the landfall trigger is layered
/// on top (the JSON <c>AbilityDefinition</c> schema does not express landfall
/// triggers — same posture as the other JSON-backed landfall cards).
///
/// ## Implemented (v1)
/// - 0/1 Creature — Bird at {G}, owner / controller wired (from JSON).
/// - <b>Landfall triggered ability</b> (CR 603.1 / 603.6a / CR 702.142) —
///   fires on a <see cref="Majik.Core.Events.CardMovedEvent"/> filtered to
///   "a land entering the battlefield under the controller's control" via the
///   shared <see cref="Triggers.OnLandEntersUnderControl"/> predicate (same
///   predicate as Akoum Hellhound / Plated Geopede / Steppe Lynx). No
///   <see cref="Majik.Core.Targeting.TargetRequest"/>: the counter always
///   lands on the Chocobo itself (CR 603.6a — the effect names "this
///   creature").
/// - <b>Resolve — +1/+1 counter on this creature</b>: places one
///   <see cref="CounterType.PlusOnePlusOne"/> counter via
///   <see cref="CounterCollection.Add"/> on the Chocobo's own
///   <see cref="Permanent.Counters"/> (CR 122.1, CR 613.7d). Unlike the
///   "until end of turn" landfall pumpers this growth is permanent — it does
///   not expire in the cleanup step. The counter contributes +1/+1 through
///   the layer system whenever <see cref="Creature.ActiveEffects"/> is wired.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only; the landfall trigger is
///   attached for inspection but not registered with a bus. This is the
///   overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager)"/> — fully wired; the trigger
///   is registered so a land entering under the controller's control queues
///   the ability automatically.
/// </summary>
[CardName("Sazh's Chocobo")]
public static class SazhsChocoboFactory
{
    public const string CardName = "Sazh's Chocobo";
    public const string Slug = "sazhs-chocobo";
    public const int Power = 0;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Sazh's Chocobo with no live <see cref="TriggerManager"/>
    /// wiring. The landfall trigger is attached for shape inspection but not
    /// registered with a bus. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Sazh's Chocobo. When <paramref name="triggers"/> is supplied
    /// the landfall trigger is registered so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> for a land entering
    /// under the controller's control automatically queues the ability.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Bird subtype, {G}, 0/1). The JSON carries no abilities — the
        // landfall trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Landfall — CR 603.1 / 603.6a / CR 702.142.
        //   "Whenever a land you control enters, put a +1/+1 counter on this
        //    creature."
        // Predicate is shared with Akoum Hellhound / Plated Geopede / Steppe
        // Lynx. No target: the counter always lands on the Chocobo itself.
        // On resolve, place one permanent +1/+1 counter (CR 122.1, CR 613.7d)
        // on the Chocobo's own Counters — unlike the "until end of turn"
        // landfall pumpers, this growth persists across turns.
        // ----------------------------------------------------------------
        var counterEffect = new Effect(
            $"{CardName}: landfall — put a +1/+1 counter on this creature",
            () => card.Counters.Add(CounterType.PlusOnePlusOne, 1));

        var landfallTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLandEntersUnderControl(owner),
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(landfallTrigger);
        triggers?.RegisterTriggeredAbility(landfallTrigger);

        return card;
    }
}
