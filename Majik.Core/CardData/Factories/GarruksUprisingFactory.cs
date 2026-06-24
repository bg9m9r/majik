using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Garruk's Uprising (Lorwyn Eclipsed Commander / many
/// reprints — Enchantment {2}{G}).
///
/// Oracle text (verified against Scryfall 2026-06-24):
///   "When this enchantment enters, if you control a creature with power 4 or
///    greater, draw a card.
///    Creatures you control have trample. (Each of those creatures can deal
///    excess combat damage to the player or planeswalker it's attacking.)
///    Whenever a creature you control with power 4 or greater enters, draw a
///    card."
///
/// The base shape (name, single Enchantment card type, {2}{G}, green) is
/// materialised from the embedded JSON definition (<c>garruks-uprising.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="AnthemOfChampionsFactory"/>. The static trample grant and the two
/// draw triggers are layered on here because the JSON
/// <c>AbilityDefinition</c> schema doesn't express continuous keyword-grant
/// statics or triggered abilities.
///
/// ## Implemented (v1)
/// - Card identity: Enchantment, mana cost {2}{G}, owner / controller wiring.
/// - <b>Static "Creatures you control have trample"</b> (CR 613.1f Layer 6 —
///   ability-adding keyword grant; CR 702.19 Trample): registered as a
///   <see cref="LordStaticEffect"/> via the canonical constructor with BOTH
///   membership gates null (no subtype, no keyword filter), <c>power: 0,
///   toughness: 0</c> (no P/T change — keyword grant only),
///   <c>grantedKeywords: ["Trample"]</c>, <c>includeSelf: true</c> (Garruk's
///   Uprising is an Enchantment, not a creature, so <c>includeSelf</c> is moot,
///   but the all-creatures-you-control scope is the intent),
///   <c>opponentsOnly: false</c>, <c>allPlayers: false</c> — controller-scoped
///   ("you control", CR 109.5). The Trample keyword string is consumed by
///   <see cref="Majik.Core.Combat.CombatAbilities"/> excess-damage assignment.
///   <see cref="ContinuousEffect.IsActive"/> short-circuits when Garruk's
///   Uprising isn't on the battlefield, so the grant lifts on LTB (CR 614).
/// - <b>ETB intervening-if draw</b> (CR 603.4): "When this enchantment enters,
///   if you control a creature with power 4 or greater, draw a card." Wired as
///   an ETB <see cref="TriggeredAbility"/> with condition
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> and an
///   <c>interveningIf</c> gate (<see cref="ControlsPower4Creature"/>) re-checked
///   on resolution. Effect draws one card under the controller via
///   <see cref="Majik.Core.Primitives.Fx.DrawCards"/> (replacement-bus aware).
///   Same structural shape as <see cref="DominatorDroneFactory"/>'s ETB drain.
/// - <b>Power-4-creature-enters draw trigger</b> (CR 603.6a): "Whenever a
///   creature you control with power 4 or greater enters, draw a card." Fires
///   on a <see cref="CardMovedEvent"/> → Battlefield for a creature controlled
///   by this card's controller whose current power (CR 208.3) is &gt;= 4. The
///   power gate reads the candidate's LIVE <see cref="Creature.Power"/>
///   (layer-applied — anthems / counters count), mirroring
///   <see cref="BondersEnclaveFactory"/> / <see cref="BigGameHunterFactory"/>.
///   Effect draws one card under the controller. Structurally mirrors
///   <see cref="GlaringFleshrakerFactory"/>'s another-creature-enters trigger
///   (controller match + a per-card gate), with the gate being power &gt;= 4
///   rather than colorless. Note: unlike "another", this trigger has no
///   self-exclusion — but Garruk's Uprising is an Enchantment, never a creature
///   with power 4+, so it cannot trigger itself.
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/> stays
///   on the <see cref="ContinuousEffectsService"/> across zone changes;
///   <see cref="ContinuousEffect.IsActive"/> gates it off when the source isn't
///   on the battlefield, but a future Prune pass could drop the entry. Same
///   shape as the other anthem / lord factories.
/// - <b>Control-change re-eval</b>: controller scope reads
///   <see cref="Permanent.Controller"/> live on the source, so a control change
///   is reflected lazily; same caveat posture as the other anthem factories.
/// </summary>
[CardName("Garruk's Uprising")]
public static class GarruksUprisingFactory
{
    public const string CardName = "Garruk's Uprising";
    public const string Slug = "garruks-uprising";

    /// <summary>CR 208.3 — the printed "power 4 or greater" threshold.</summary>
    private const int PowerThreshold = 4;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Garruk's Uprising with no live wiring. The static trample grant
    /// is NOT registered (no layers service), and the two draw triggers are
    /// attached structurally but NOT enrolled with a <see cref="TriggerManager"/>.
    /// Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Garruk's Uprising. When
    /// <paramref name="continuousEffects"/> is supplied, the "Creatures you
    /// control have trample" static is registered against the layers service.
    /// When <paramref name="triggers"/> is supplied, both draw triggers are
    /// enrolled so the matching battlefield-entry events surface them as pending.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the trample
    /// grant against. May be null — no live grant.</param>
    /// <param name="triggers">Trigger manager for the two draw triggers. May be
    /// null — triggers attach structurally but are not enrolled.</param>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Enchantment, {2}{G}, green) from the embedded JSON.
        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Static — "Creatures you control have trample."
        // CR 613.1f (Layer 6 — keyword-grant) / CR 702.19 (Trample).
        // Canonical LordStaticEffect with no membership gate (all creatures),
        // no P/T change (power/toughness 0), granting Trample, controller-
        // scoped ("you control", CR 109.5).
        // ----------------------------------------------------------------
        continuousEffects?.Register(new LordStaticEffect(
            source: card,
            matchingSubtype: null,
            matchingKeyword: null,
            power: 0,
            toughness: 0,
            grantedKeywords: new[] { "Trample" },
            includeSelf: true,
            opponentsOnly: false,
            allPlayers: false));

        // ----------------------------------------------------------------
        // Intervening-if gate for the ETB draw (CR 603.4 / 208.3).
        // "you control a creature with power 4 or greater" — read each
        // candidate's LIVE power (layer-applied, CR 208.3) so anthems /
        // counters count.
        // ----------------------------------------------------------------
        bool ControlsPower4Creature()
        {
            var controller = card.Controller ?? owner;
            return controller.Zones.Battlefield.GetCards()
                .OfType<Creature>()
                .Any(c => c.Power >= PowerThreshold);
        }

        // ----------------------------------------------------------------
        // Trigger 1 — ETB intervening-if draw (CR 603.4).
        //   "When this enchantment enters, if you control a creature with
        //    power 4 or greater, draw a card."
        // ----------------------------------------------------------------
        var etbDrawEffect = new Effect(
            $"{CardName}: draw a card (ETB, if you control a creature with power {PowerThreshold}+)",
            () =>
            {
                var controller = card.Controller ?? owner;
                Majik.Core.Primitives.Fx.DrawCards(controller, 1);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbDrawEffect },
            interveningIf: ControlsPower4Creature,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Trigger 2 — power-4-creature-enters draw (CR 603.6a / 208.3).
        //   "Whenever a creature you control with power 4 or greater enters,
        //    draw a card."
        // Fires on a battlefield entry of a creature THIS controller controls
        // whose live power (CR 208.3) is >= 4. No self-exclusion is needed —
        // Garruk's Uprising is an Enchantment, never a power-4 creature.
        // ----------------------------------------------------------------
        var power4EntersCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (e.Card is not Creature entering) return false;
            // CR 109.5 — "you control": match the entering creature's
            // controller to Garruk's Uprising's controller.
            if (!ReferenceEquals(entering.Controller, card.Controller ?? owner)) return false;
            // CR 208.3 — power is the left value of the P/T box; read live.
            return entering.Power >= PowerThreshold;
        });

        var entersDrawEffect = new Effect(
            $"{CardName}: draw a card (a creature you control with power {PowerThreshold}+ entered)",
            () =>
            {
                var controller = card.Controller ?? owner;
                Majik.Core.Primitives.Fx.DrawCards(controller, 1);
            });

        var entersTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: power4EntersCondition,
            effects: new IEffect[] { entersDrawEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(entersTrigger);
        triggers?.RegisterTriggeredAbility(entersTrigger);

        return card;
    }
}
