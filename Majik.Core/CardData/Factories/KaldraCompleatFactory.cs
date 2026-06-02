using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kaldra Compleat (Modern Horizons 2, {7}).
///
/// Legendary Artifact — Equipment. Oracle text (Scryfall, verified 2026-06-02):
///   "Living weapon (When this Equipment enters, create a 0/0 black Phyrexian
///    Germ creature token, then attach this to it.)"
///   "Indestructible"
///   "Equipped creature gets +5/+5 and has first strike, trample,
///    indestructible, haste, and 'Whenever this creature deals combat damage
///    to a creature, exile that creature.'"
///   "Equip {7}"
///
/// ## Shape source
/// Card identity (name, {7}, Legendary Artifact — Equipment) is loaded from
/// <c>Majik.Core/CardData/Cards/kaldra-compleat.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. All behaviour is attached in code
/// below: the JSON ability schema has no living-weapon trigger, no equip
/// ability, no dynamic attached-boost effect, and no granted-trigger shape —
/// the same reason the living-weapon analogues
/// (<see cref="BatterskullFactory"/>, <see cref="NettlecystFactory"/>) hand-roll
/// their behaviour.
///
/// ## Implementation
///
/// - <b>Living weapon (CR 702.91 / CR 603.6a)</b>: an ETB-self trigger that
///   creates a 0/0 black Phyrexian Germ creature token under the equipment's
///   controller and immediately attaches Kaldra Compleat to it. Wired as a
///   <see cref="TriggeredAbility"/> over <see cref="Triggers.OnEnterBattlefieldSelf"/>;
///   the spawned token routes through <see cref="TokenFactory.CreateOnBattlefield"/>
///   so <see cref="CardMovedEvent"/> fires for downstream ETB listeners when a
///   <see cref="ZoneService"/> is wired. The Germ enters 0/0 — without the
///   boost it would die to SBAs (CR 704.5f); once attached the Layer-7c +5/+5
///   below keeps it alive, and the granted Indestructible keyword (Layer 6)
///   means it survives even lethal damage (CR 702.12).
/// - <b>Indestructible on the equipment itself (CR 702.12)</b>: Kaldra Compleat
///   the artifact has Indestructible printed on it directly, so the marker is
///   ALWAYS stamped on the card (unlike the equipped-creature keyword grants,
///   which project onto the bearer). <see cref="Majik.Core.Combat.CombatAbilities.HasIndestructible"/>
///   and the SBA / destroy pipeline read the marker off the card.
/// - <b>Static "+5/+5"</b>: <see cref="AttachedBoostEffect"/> at Layer 7c
///   (CR 613 Layer 7c). Reads <see cref="Permanent.AttachedTo"/> dynamically so
///   re-equipping transfers the boost without re-registration; gates on being
///   on the battlefield AND attached (see <see cref="AttachedBoostEffect.IsActive"/>).
/// - <b>Keyword grants — first strike, trample, indestructible, haste</b>
///   (CR 702.7 / 702.19 / 702.12 / 702.10, CR 613.1f Layer 6): when a
///   <see cref="ContinuousEffectsService"/> is supplied, four
///   <see cref="GrantAbilityEffect"/> instances re-project fresh
///   <see cref="KeywordAbility"/> markers onto the live equipped creature each
///   layer pass / re-equip. The shape-only path (no service) stamps the four
///   markers on Kaldra Compleat itself, mirroring
///   <see cref="HammerOfNazahnFactory"/>'s Indestructible fallback, so
///   factory-shape / dispatch tests observe the keywords somewhere on the
///   equipment.
/// - <b>Granted triggered ability — "Whenever this creature deals combat
///   damage to a creature, exile that creature."</b> (CR 603.1 / CR 510,
///   CR 613.1f Layer 6): when a <see cref="ContinuousEffectsService"/> AND a
///   <see cref="TriggerManager"/> are supplied, a fifth
///   <see cref="GrantAbilityEffect"/> projects a fresh
///   <see cref="TriggeredAbility"/> onto the live equipped creature. The
///   granted trigger fires on <see cref="CombatDamageDealtEvent"/> where the
///   source is the BEARER and the target is a Creature (per oracle "to a
///   creature" — player + planeswalker targets do NOT fire); the resolve body
///   exiles the damaged creature via <see cref="Fx.MoveToExile(ICard)"/>. Each
///   re-grant produces a fresh closure bound to the live bearer, and the
///   freshly-built trigger is registered with the supplied
///   <see cref="TriggerManager"/> via the grant-aware
///   <see cref="GrantAbilityEffect"/> factory closure so a re-equip rebinds the
///   trigger to the new bearer. Same combat-trigger shape as
///   <see cref="StinkweedImpFactory"/>, but the destroy is an exile (CR 701.10)
///   and it is GRANTED to the equipped creature rather than printed on a body.
/// - <b>Equip {7}</b>: standard equipment-cycle <see cref="EquipActivatedAbility"/>
///   wiring with the Puresteel-Paladin zero-cost provider hook (CR 702.6).
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only (factory-shape / dispatch
/// tests): the living-weapon ETB trigger is attached but not registered; the
/// boost and keyword/trigger grants are not registered against any service; the
/// four equipped-creature keywords and the Indestructible-on-self marker are
/// stamped on the card for deterministic inspection.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first controller-side
///   creature deterministically (same gap as the rest of the equipment cycle).
/// </summary>
[CardName("Kaldra Compleat")]
public static class KaldraCompleatFactory
{
    public const string CardName = "Kaldra Compleat";
    public const string Cost = "{7}";
    public const string EquipCost = "{7}";
    public const int Boost = 5;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("kaldra-compleat");

    /// <summary>
    /// Constructs Kaldra Compleat with no live runtime wiring (the shape /
    /// dispatcher path). The living-weapon ETB trigger is attached but not
    /// registered with a <see cref="TriggerManager"/>; the +5/+5 boost and the
    /// equipped-creature keyword / trigger grants are not registered against any
    /// <see cref="ContinuousEffectsService"/>. The four equipped-creature
    /// keywords are stamped on Kaldra Compleat itself for deterministic
    /// shape-only inspection (alongside the always-on Indestructible-on-self).
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null, zoneService: null);

    /// <summary>
    /// Constructs Kaldra Compleat with optional runtime services. When
    /// <paramref name="continuousEffects"/> is supplied the +5/+5 boost
    /// (Layer 7c) and the first strike / trample / indestructible / haste grants
    /// (Layer 6) are registered against it. When BOTH
    /// <paramref name="continuousEffects"/> and <paramref name="triggers"/> are
    /// supplied the granted combat-damage exile trigger is projected onto the
    /// equipped creature and registered for bus-driven firing. When
    /// <paramref name="zoneService"/> is supplied the spawned Germ token routes
    /// through the service so <see cref="CardMovedEvent"/> fires for downstream
    /// ETB listeners.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // Indestructible on the equipment itself — CR 702.12. Printed on
        // Kaldra Compleat directly (NOT a grant), so the marker is always
        // on the card. CombatAbilities.HasIndestructible + the destroy /
        // SBA pipeline read it here.
        // --------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Indestructible", card, owner));

        // --------------------------------------------------------------
        // Static "+5/+5" — CR 613 Layer 7c. Gates on attached (see
        // AttachedBoostEffect.IsActive).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: Boost, toughness: Boost));
        }

        // --------------------------------------------------------------
        // "Equipped creature has first strike, trample, indestructible,
        // and haste." (CR 702.7 / 702.19 / 702.12 / 702.10, CR 613.1f
        // Layer 6). With a service wired, GrantAbilityEffect re-projects a
        // fresh KeywordAbility per keyword onto the live equipped creature
        // each layer pass / re-equip. Shape-only path stamps the four
        // keywords on Kaldra Compleat itself (Hammer of Nazahn posture) so
        // factory-shape tests observe them somewhere on the equipment.
        // --------------------------------------------------------------
        string[] grantedKeywords = { "First strike", "Trample", "Indestructible", "Haste" };
        if (continuousEffects != null)
        {
            foreach (var kw in grantedKeywords)
            {
                var keyword = kw; // capture
                continuousEffects.Register(new GrantAbilityEffect(
                    source: card,
                    targetSelector: () => card.AttachedTo,
                    abilityFactory: bearer => new KeywordAbility(
                        keyword, bearer, bearer.Controller ?? owner)));
            }
        }
        else
        {
            foreach (var kw in grantedKeywords)
            {
                card.AddAbility(new KeywordAbility(kw, card, owner));
            }
        }

        // --------------------------------------------------------------
        // Granted triggered ability — "Whenever this creature deals combat
        // damage to a creature, exile that creature." (CR 603.1 / CR 510,
        // CR 613.1f Layer 6). Projected onto the live equipped creature via
        // GrantAbilityEffect: each (re-)grant builds a fresh combat trigger
        // bound to the live bearer and registers it with the TriggerManager
        // (revoked + rebuilt on re-equip). The trigger gates on the bearer
        // dealing combat damage to a Creature target (oracle "to a creature"
        // — players / planeswalkers do NOT fire); the resolve body exiles
        // the damaged creature (CR 701.10). Same combat-trigger shape as
        // Stinkweed Imp, but exile (not destroy) and granted, not printed.
        // --------------------------------------------------------------
        if (continuousEffects != null && triggers != null)
        {
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: bearer =>
                    BuildExileTrigger(card, bearer, owner, triggers)));
        }

        // --------------------------------------------------------------
        // Living weapon — CR 702.91 / CR 603.6a.
        //   "When Kaldra Compleat enters, create a 0/0 black Phyrexian Germ
        //    creature token, then attach Kaldra Compleat to it."
        // ETB-self trigger builds the Germ via
        // TokenFactory.CreateOnBattlefield (CardMovedEvent fires when a
        // ZoneService is wired) and immediately attaches the equipment. The
        // Germ enters 0/0; once attached the Layer-7c +5/+5 takes it to 5/5
        // (and granted Indestructible keeps it alive past lethal), so it
        // survives SBAs (CR 704.5f).
        // --------------------------------------------------------------
        var livingWeaponEffect = new Effect(
            $"{CardName}: living weapon — create 0/0 black Phyrexian Germ + attach",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var ctrl = card.Controller ?? owner;

                var spec = new TokenFactory.TokenSpec(
                    Name: "Germ",
                    Power: 0,
                    Toughness: 0,
                    Subtypes: new[] { CardSubtype.Phyrexian, CardSubtype.Germ },
                    Colors: new[] { ManaColor.Black });

                var germ = TokenFactory.CreateOnBattlefield(spec, ctrl, zoneService);
                card.AttachTo(germ);
            });

        var livingWeaponTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { livingWeaponEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(livingWeaponTrigger);
        triggers?.RegisterTriggeredAbility(livingWeaponTrigger);

        // --------------------------------------------------------------
        // Equip {7} — activated ability (CR 702.6) via the shared
        // EquipActivatedAbility primitive. Threads the Puresteel zero-cost
        // provider hook for parity with the rest of the equipment cycle.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }

    /// <summary>
    /// Builds the granted "Whenever this creature deals combat damage to a
    /// creature, exile that creature" trigger bound to <paramref name="bearer"/>
    /// and registers it with <paramref name="triggers"/>. CR 603.1 / CR 510;
    /// the exile is CR 701.10. The captured-victim closure mirrors
    /// <see cref="StinkweedImpFactory"/>: the predicate stamps the damaged
    /// creature off <see cref="CombatDamageDealtEvent.Target"/>, the resolve
    /// body exiles it (with a CR 608.2b still-on-battlefield guard).
    /// </summary>
    private static TriggeredAbility BuildExileTrigger(
        Permanent source,
        Permanent bearer,
        Player owner,
        TriggerManager triggers)
    {
        Creature? capturedVictim = null;

        var exileEffect = new Effect(
            $"{CardName}: exile creature that took combat damage",
            () =>
            {
                var victim = capturedVictim;
                if (victim == null) return;

                // CR 608.2b — the damaged creature must still be on the
                // battlefield. If it already left (bounced, died, exiled) the
                // exile is a clean no-op.
                if (victim.Zone != ZoneType.Battlefield) return;

                // CR 701.10 — exile the damaged creature.
                Fx.MoveToExile(victim);
            });

        var trigger = new TriggeredAbility(
            source: source,
            controller: bearer.Controller ?? owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (!ReferenceEquals(e.Source, bearer)) return false;
                if (e.Target is not Creature victim) return false;
                capturedVictim = victim;
                return true;
            }),
            effects: new IEffect[] { exileEffect },
            activeZones: new[] { ZoneType.Battlefield });

        triggers.RegisterTriggeredAbility(trigger);
        return trigger;
    }
}
