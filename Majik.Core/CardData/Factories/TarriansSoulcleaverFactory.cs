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
/// Named-card factory for Tarrian's Soulcleaver ({1}).
///
/// Legendary Artifact — Equipment. Oracle text (verified against Scryfall
/// 2026-06-02):
///   "Equipped creature has vigilance."
///   "Whenever another artifact or creature is put into a graveyard from
///    the battlefield, put a +1/+1 counter on equipped creature."
///   "Equip {2}"
///
/// Tarrian's Soulcleaver combines three analogue shapes already in the
/// engine:
/// - <b>Equipment + Equip {2}</b> — line-for-line
///   <see cref="BonesplitterFactory"/> (Equipment subtype + the
///   <see cref="EquipActivatedAbility"/> primitive, CR 702.6).
/// - <b>"Equipped creature has vigilance"</b> — a Layer-6 ability-adding
///   grant (CR 613.1f) via <see cref="AttachedBoostEffect"/> with
///   <c>grantedKeywords: ["Vigilance"]</c> and no P/T change, identical in
///   shape to <see cref="SwiftfootBootsFactory"/>'s "has hexproof and
///   haste". Vigilance is read back from
///   <see cref="CreatureCharacteristics.Keywords"/> by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasVigilance"/>.
/// - <b>Dies trigger</b> — the same <see cref="CardMovedEvent"/>
///   Battlefield→Graveyard counter-accrual shape as
///   <see cref="BladeOfTheBloodchiefFactory"/> (CR 603.6e + CR 700.4),
///   differing in the qualifying card (an artifact OR creature, and
///   <em>another</em> permanent — never the Soulcleaver itself) and the
///   fixed +1/+1 amount.
///
/// The base card shape (name / Legendary supertype / Artifact type /
/// Equipment subtype / {1} cost) is materialised from the embedded JSON
/// definition (<c>tarrians-soulcleaver.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the vigilance grant, the
/// Equip ability, and the dies-trigger are layered on here because the JSON
/// <c>AbilityDefinition</c> schema expresses none of them yet (same posture
/// as <see cref="BladeOfTheBloodchiefFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Legendary Artifact — Equipment</b> at printed cost {1}. The legend
///   rule (CR 704.5j) is enforced by SBAs from the supertype on the shape.
/// - <b>"Equipped creature has vigilance."</b> — Layer-6 keyword grant
///   (CR 613.1f) via <see cref="AttachedBoostEffect"/>; gates on the
///   Soulcleaver being on the battlefield AND attached to a battlefield
///   permanent (<see cref="AttachedBoostEffect.IsActive"/>), so re-equipping
///   transfers it and detach / LTB revokes it.
/// - <b>Equip {2}</b> — activated ability via the
///   <see cref="EquipActivatedAbility"/> primitive (CR 702.6), with the
///   Puresteel-Paladin zero-equip cost-provider hook, identical to
///   Bonesplitter.
/// - <b>Dies trigger (CR 603.6e + CR 700.4)</b>: fires on
///   <see cref="CardMovedEvent"/> with FromZone = Battlefield + ToZone =
///   Graveyard when the moved card is an artifact OR a creature AND is not
///   the Soulcleaver itself ("another"). Controller-agnostic — any side's
///   permanent qualifies. On resolution it reads the equipped creature via
///   <see cref="Permanent.AttachedTo"/> (last-known at resolution) and puts
///   one +1/+1 counter on it. When the Soulcleaver is unequipped
///   (AttachedTo is null), resolution is a safe no-op.
///
/// ## Notes
/// - <b>"another"</b> (CR 109.5 / object-self-reference): the moved card is
///   compared by reference to the Soulcleaver; the Soulcleaver dying does
///   not trigger its own ability.
///
/// ## Deferred (v1 gaps)
/// - <b>Attach-target prompt</b> for "creature you control" (CR 702.6b)
///   — v1 picks the first controller-side creature deterministically
///   (inherited from <see cref="EquipActivatedAbility"/>).
/// - The single-arg <see cref="Create(Player)"/> dispatcher path attaches
///   the dies-trigger structurally with no <see cref="TriggerManager"/>
///   wiring and no <see cref="ReplacementBus"/> — suitable for shape /
///   dispatcher tests; counter placement falls through to a direct add.
/// </summary>
[CardName("Tarrian's Soulcleaver")]
public static class TarriansSoulcleaverFactory
{
    public const string CardName = "Tarrian's Soulcleaver";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "tarrians-soulcleaver";

    public const string EquipCost = "{2}";

    /// <summary>+1/+1 counters placed on the equipped creature per trigger.</summary>
    public const int CountersPerTrigger = 1;

    /// <summary>
    /// Single-arg dispatcher path (used by <see cref="NamedCardFactory"/>).
    /// Attaches the vigilance grant, the Equip ability, and the dies-trigger
    /// structurally so the card shape is correct; no
    /// ContinuousEffectsService / TriggerManager / ReplacementBus wiring.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, continuousEffects: null, triggers: null, replacements: null);

    /// <summary>
    /// Fully-wired construction.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">ContinuousEffectsService to register
    /// the Layer-6 vigilance grant against. May be null — the grant is then
    /// skipped (shape-only path).</param>
    /// <param name="triggers">TriggerManager to register the dies-trigger
    /// against. May be null — the trigger is still attached to the card
    /// shape.</param>
    /// <param name="replacements">CR 614 replacement bus routed through
    /// <see cref="CountersService.Add"/> so Hardened Scales / Doubling
    /// Season can rewrite the +1/+1 placement. May be null — counter
    /// placement falls through to a direct add.</param>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name / Legendary / Artifact / Equipment / {1}) from
        // the embedded JSON definition.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Artifact card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Artifact but got "
                + $"'{built.GetType().Name}'.");
        }

        // --------------------------------------------------------------
        // "Equipped creature has vigilance." Layer-6 ability-adding grant
        // (CR 613.1f) via an AttachedBoostEffect with no P/T change (0/0)
        // carrying the "Vigilance" marker. Same shape as Swiftfoot Boots'
        // "has hexproof and haste". Gates on the Soulcleaver being on the
        // battlefield AND attached (AttachedBoostEffect.IsActive). Vigilance
        // is read back via CombatAbilities.HasVigilance.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(
                    source: card,
                    power: 0,
                    toughness: 0,
                    grantedKeywords: new[] { "Vigilance" },
                    layer: Layer.Abilities));
        }

        // --------------------------------------------------------------
        // Equip {2} — activated ability (CR 702.6) via the
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
        // Dies trigger — CR 603.6e + CR 700.4.
        //   "Whenever another artifact or creature is put into a graveyard
        //    from the battlefield, put a +1/+1 counter on equipped creature."
        // Same Battlefield→Graveyard CardMovedEvent shape as Blade of the
        // Bloodchief, but the qualifying card is an artifact OR a creature
        // AND must be *another* permanent — the Soulcleaver itself dying
        // never triggers its own ability (CR 109.5 "another"). Controller-
        // agnostic: any side's permanent qualifies.
        // --------------------------------------------------------------
        var diesCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            // "another" — exclude the Soulcleaver itself (CR 109.5).
            if (ReferenceEquals(e.Card, card)) return false;
            return e.Card.HasType(CardType.Artifact)
                || e.Card.HasType(CardType.Creature);
        });

        var counterEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on equipped creature",
            () =>
            {
                // CR 608.2 — read the equipped creature as the ability
                // resolves. No equipped creature ⇒ nothing happens (safe
                // no-op while unequipped).
                if (card.AttachedTo is not Creature equipped) return;

                // CR 614 — route through the replacement bus when supplied
                // so Hardened Scales / Doubling Season can rewrite the
                // placement; falls through to a direct add otherwise.
                CountersService.Add(
                    equipped,
                    CounterType.PlusOnePlusOne,
                    CountersPerTrigger,
                    replacements);
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
