using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nettlecyst (New Phyrexia, {3}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified 2026-05-29):
///   "Living weapon (When this Equipment enters, create a 0/0 black
///    Phyrexian Germ creature token, then attach this to it.)"
///   "Equipped creature gets +1/+1 for each artifact and/or enchantment
///    you control."
///   "Equip {2}"
///
/// ## Why a hand-rolled C# factory (not the JSON CardDefinition path)
///
/// The data-driven <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>
/// only supports the effect/ability shapes enumerated in its
/// <c>BuildEffect</c> / <c>BuildAbility</c> dispatch (counters, draw,
/// scry/surveil, stub damage, …). It has NO living-weapon trigger, NO
/// equip ability, and NO dynamic attached-boost effect — a JSON def would
/// produce only a vanilla Artifact shell (cf. <c>blade-of-the-bloodchief.json</c>,
/// which carries zero abilities). The functioning analogues
/// (<see cref="BatterskullFactory"/>, <see cref="CranialPlatingFactory"/>)
/// are themselves hand-rolled C# factories for exactly this reason, so
/// Nettlecyst follows that established pattern.
///
/// ## Implementation
///
/// - <b>Living weapon (CR 702.91 / CR 603.6a)</b>: an ETB-self trigger
///   that creates a 0/0 black Phyrexian Germ creature token under the
///   equipment's controller and immediately attaches Nettlecyst to it.
///   Wired as a <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>; the spawned token
///   routes through <see cref="TokenFactory.CreateOnBattlefield"/> so
///   <see cref="CardMovedEvent"/> fires for downstream ETB listeners when
///   a <see cref="ZoneService"/> is wired. The Germ carries both the
///   <see cref="CardSubtype.Phyrexian"/> and <see cref="CardSubtype.Germ"/>
///   subtypes and is black via <see cref="TokenFactory.TokenSpec.Colors"/>.
///   It enters 0/0 — without the boost it would die to SBAs (CR 704.5f);
///   once attached, the Layer-7c boost below brings it to at least +1/+1
///   (Nettlecyst itself is an artifact you control), keeping it alive.
/// - <b>Static "+1/+1 for each artifact and/or enchantment you control"</b>
///   — the dynamic-N <see cref="AttachedBoostEffect"/> overload (Layer 7c,
///   CR 613 Layer 7c). Both the power and toughness closures sample the
///   SAME count (artifacts + enchantments under the controller), so the
///   bonus is symmetric +N/+N, unlike Cranial Plating's +N/+0. The boost
///   reads <see cref="Permanent.AttachedTo"/> dynamically, so re-equipping
///   transfers it without re-registration; <see cref="AttachedBoostEffect.IsActive"/>
///   gates on being on the battlefield AND attached.
/// - <b>Equip {2}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive, threading the
///   Puresteel-Paladin zero-equip cost-provider hook for parity with the
///   rest of the equipment cycle.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only (factory-shape /
/// dispatch tests). The living-weapon ETB trigger is attached for shape
/// but not registered with a <see cref="TriggerManager"/>; the dynamic
/// boost is not registered against any <see cref="ContinuousEffectsService"/>.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically (same gap as the rest of
///   the equipment cycle).
/// - <b>Artifact/enchantment-count nuances</b> — the boost closure scans
///   the controller's battlefield top-level for any permanent with
///   <c>CardType.Artifact</c> or <c>CardType.Enchantment</c>. Phased-out
///   permanents (CR 702.26) and face-down morphs would currently miscount;
///   same gap as Cranial Plating's artifact count.
/// </summary>
[CardName("Nettlecyst")]
public static class NettlecystFactory
{
    public const string CardName = "Nettlecyst";
    public const string Cost = "{3}";
    public const string EquipCost = "{2}";

    /// <summary>
    /// Constructs Nettlecyst with no live runtime wiring (the shape /
    /// dispatcher path). The living-weapon ETB trigger is attached but not
    /// registered with a <see cref="TriggerManager"/>; the dynamic boost
    /// is not registered against any <see cref="ContinuousEffectsService"/>.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null, zoneService: null);

    /// <summary>
    /// Constructs Nettlecyst with optional continuous-effects wiring (the
    /// dynamic +N/+N boost). Convenience overload used by boost-focused
    /// tests; ETB triggers and token-routing services are left unwired.
    /// </summary>
    public static Artifact Create(Player owner, ContinuousEffectsService? continuousEffects)
        => Create(owner, continuousEffects, triggers: null, zoneService: null);

    /// <summary>
    /// Constructs Nettlecyst with optional runtime services. When
    /// <paramref name="continuousEffects"/> is supplied the +N/+N boost
    /// (Layer 7c) is registered against it. When <paramref name="triggers"/>
    /// is supplied the living-weapon ETB trigger is registered for
    /// bus-driven firing. When <paramref name="zoneService"/> is supplied
    /// the spawned Germ token routes through the service so
    /// <see cref="CardMovedEvent"/> fires for downstream ETB listeners.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: Cost,
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // Static "Equipped creature gets +1/+1 for each artifact and/or
        // enchantment you control." Dynamic-N AttachedBoostEffect samples
        // the controller's artifact+enchantment count at each layer pass
        // (CR 613 Layer 7c). Both stats read the SAME count → symmetric
        // +N/+N (contrast Cranial Plating's +N/+0).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                powerFn: () => CountArtifactsAndEnchantments(card),
                toughnessFn: () => CountArtifactsAndEnchantments(card)));
        }

        // --------------------------------------------------------------
        // Living weapon — CR 702.91 / CR 603.6a.
        //   "When Nettlecyst enters, create a 0/0 black Phyrexian Germ
        //    creature token, then attach Nettlecyst to it."
        // ETB-self trigger builds the Germ via
        // TokenFactory.CreateOnBattlefield (CardMovedEvent fires when a
        // ZoneService is wired) and immediately attaches the equipment.
        // The Germ enters 0/0; once attached the Layer-7c boost above
        // takes it to at least 1/1 (Nettlecyst itself is an artifact you
        // control), so it survives SBAs (CR 704.5f).
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
        // Equip {2} — standard equipment-cycle Equip activated ability
        // (CR 702.6) via the shared primitive. Threads the Puresteel
        // zero-cost provider hook.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }

    /// <summary>
    /// Live count of artifact + enchantment permanents on Nettlecyst's
    /// CURRENT controller's battlefield (CR 613 Layer 7c source-of-truth).
    /// "Artifact and/or enchantment" is a union — a single permanent that
    /// is both an Artifact AND an Enchantment (e.g. an artifact enchantment)
    /// is counted ONCE, not twice (CR 109.2 / the "and/or" phrasing means
    /// "qualifies if either"). Reads the controller dynamically so a
    /// controller-change effect re-targets the count. Defaults to 0 when
    /// Nettlecyst has no live controller so the boost gates cleanly via
    /// <see cref="AttachedBoostEffect.IsActive"/>.
    /// </summary>
    public static int CountArtifactsAndEnchantments(Permanent equipment)
    {
        var ctrl = equipment.Controller ?? equipment.Owner;
        if (ctrl == null) return 0;
        return ctrl.Zones.Battlefield.GetCards()
            .Count(c => c.HasType(CardType.Artifact) || c.HasType(CardType.Enchantment));
    }
}
