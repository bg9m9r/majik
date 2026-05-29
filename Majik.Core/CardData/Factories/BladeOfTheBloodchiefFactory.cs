using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Blade of the Bloodchief (Zendikar, {1}).
///
/// Artifact — Equipment. Oracle text (verified against Scryfall 2026-05-29):
///   "Whenever a creature dies, put a +1/+1 counter on equipped creature.
///    If equipped creature is a Vampire, put two +1/+1 counters on it
///    instead."
///   "Equip {1}."
///
/// Blade combines two analogue shapes already in the engine:
/// - <b>Equipment + Equip {1}</b> — line-for-line
///   <see cref="BonesplitterFactory"/> (Equipment subtype + the
///   <see cref="EquipActivatedAbility"/> primitive, CR 702.6).
/// - <b>"Whenever a creature dies" trigger</b> — same
///   <see cref="CardMovedEvent"/> Battlefield→Graveyard + creature-type
///   shape as <see cref="FalkenrathNobleFactory"/> (CR 603.1 + CR 700.4).
///
/// The base card shape (name / Artifact type / Equipment subtype / {1}
/// cost) is materialised from the embedded JSON definition
/// (<c>blade-of-the-bloodchief.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the Equip ability and the
/// death-trigger are layered on here because the JSON
/// <c>AbilityDefinition</c> schema expresses neither the equip activated
/// ability nor a generic "creature dies → counter on equipped creature"
/// trigger yet (same posture as <see cref="ArdentPleaFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Artifact — Equipment</b> at printed cost {1}.
/// - <b>Equip {1}</b> — activated ability via the
///   <see cref="EquipActivatedAbility"/> primitive (CR 702.6), with the
///   Puresteel-Paladin zero-equip cost-provider hook, identical to
///   Bonesplitter.
/// - <b>Death trigger (CR 603.1 + CR 700.4)</b>: fires on
///   <see cref="CardMovedEvent"/> with FromZone = Battlefield + ToZone =
///   Graveyard when the moved card has <see cref="CardType.Creature"/>.
///   "Whenever a creature dies" is controller-agnostic (any creature,
///   any side). On resolution it reads the equipped creature via
///   <see cref="Permanent.AttachedTo"/> (last-known at resolution) and
///   places +1/+1 counters on it: two if that creature is a Vampire
///   (CR 614 replacements honoured via <see cref="CountersService.Add"/>
///   when a <see cref="ReplacementBus"/> is supplied), one otherwise.
///   When Blade is unequipped (AttachedTo is null), resolution is a safe
///   no-op.
///
/// ## Notes
/// - <b>Vampire check at resolution</b>: the printed "If equipped creature
///   is a Vampire" rider is evaluated when the ability resolves, reading
///   the equipped creature's current subtypes (CR 608.2 — characteristics
///   are checked as the ability resolves).
///
/// ## Deferred (v1 gaps)
/// - <b>Attach-target prompt</b> for "creature you control" (CR 702.6b)
///   — v1 picks the first controller-side creature deterministically
///   (inherited from <see cref="EquipActivatedAbility"/>).
/// - The single-arg <see cref="Create(Player)"/> dispatcher path attaches
///   the death-trigger structurally with no <see cref="TriggerManager"/>
///   wiring and no <see cref="ReplacementBus"/> — suitable for shape /
///   dispatcher tests; counter placement falls through to a direct add.
/// </summary>
[CardName("Blade of the Bloodchief")]
public static class BladeOfTheBloodchiefFactory
{
    public const string CardName = "Blade of the Bloodchief";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "blade-of-the-bloodchief";

    public const string EquipCost = "{1}";

    /// <summary>Counters placed on a non-Vampire equipped creature.</summary>
    public const int NonVampireCounters = 1;

    /// <summary>Counters placed on a Vampire equipped creature ("instead").</summary>
    public const int VampireCounters = 2;

    /// <summary>
    /// Single-arg dispatcher path (used by <see cref="NamedCardFactory"/>).
    /// Attaches the Equip ability and the death-trigger structurally so the
    /// card shape is correct; no TriggerManager / ReplacementBus wiring.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Fully-wired construction.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager to register the death-trigger
    /// against. May be null — the trigger is still attached to the card
    /// shape.</param>
    /// <param name="replacements">CR 614 replacement bus routed through
    /// <see cref="CountersService.Add"/> so Hardened Scales / Doubling
    /// Season can rewrite the +1/+1 placement. May be null — counter
    /// placement falls through to a direct add.</param>
    public static Artifact Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name / Artifact / Equipment / {1}) from embedded JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Artifact card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Artifact but got "
                + $"'{built.GetType().Name}'.");
        }

        // --------------------------------------------------------------
        // Equip {1} — activated ability (CR 702.6) via the
        // EquipActivatedAbility primitive. Sorcery-speed gate, target-
        // gathering, attach resolution, and the Puresteel zero-equip
        // cost-provider hook are all encapsulated. Identical to Bonesplitter.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        // --------------------------------------------------------------
        // Death trigger — CR 603.1 + CR 700.4.
        //   "Whenever a creature dies, put a +1/+1 counter on equipped
        //    creature. If equipped creature is a Vampire, put two +1/+1
        //    counters on it instead."
        // Same Battlefield→Graveyard + creature-type shape as Falkenrath
        // Noble; controller-agnostic ("a creature", no "you control").
        // --------------------------------------------------------------
        var diesCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            return e.Card.HasType(CardType.Creature);
        });

        var counterEffect = new Effect(
            $"{CardName}: put +1/+1 counter(s) on equipped creature",
            () =>
            {
                // CR 608.2 — read the equipped creature and its
                // characteristics as the ability resolves. No equipped
                // creature ⇒ nothing happens (safe no-op while unequipped).
                if (card.AttachedTo is not Creature equipped) return;

                // "If equipped creature is a Vampire, put two instead."
                var amount = equipped.HasSubtype(CardSubtype.Vampire)
                    ? VampireCounters
                    : NonVampireCounters;

                // CR 614 — route through the replacement bus when supplied
                // so Hardened Scales / Doubling Season can rewrite the
                // placement; falls through to a direct add otherwise.
                CountersService.Add(
                    equipped, CounterType.PlusOnePlusOne, amount, replacements);
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: diesCondition,
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }
}
