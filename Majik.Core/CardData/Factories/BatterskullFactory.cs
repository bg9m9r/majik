using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
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
/// Named-card factory for Batterskull (New Phyrexia, {5}).
///
/// Artifact — Equipment. Oracle text:
///   "Living weapon (When this Equipment enters, create a 0/0 black Germ
///    creature token, then attach this to it.)"
///   "Equipped creature gets +4/+4 and has vigilance and lifelink."
///   "{3}: Return Batterskull to its owner's hand."
///
/// ## Implementation
///
/// - <b>Living weapon (CR 702.91)</b>: an ETB trigger on Batterskull
///   itself that creates a 0/0 black Germ creature token under the
///   equipment's controller and immediately attaches Batterskull to it.
///   Wired as <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>; the spawned token
///   routes through <see cref="TokenFactory.CreateOnBattlefield"/> so
///   <see cref="CardMovedEvent"/> fires for downstream ETB listeners
///   (Soul Warden, etc.). The Germ is black via
///   <see cref="TokenFactory.TokenSpec.Colors"/>; subtypes include
///   <see cref="CardSubtype.Germ"/>. The token enters as a 0/0 — without
///   the boost, it would immediately die to the SBA loop (CR 704.5f);
///   the Layer-7c boost below is what keeps it alive once attached.
/// - <b>Static "equipped creature gets +4/+4"</b>: registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (CR 613 Layer 7c).
///   Mirrors <see cref="SwordOfFireAndIceFactory"/> /
///   <see cref="HammerOfNazahnFactory"/>. The boost reads
///   <see cref="Permanent.AttachedTo"/> dynamically, so re-equipping
///   transfers the +4/+4 without re-registration.
/// - <b>Vigilance + lifelink grants (CR 702.20 / 702.15, CR 613.1f)</b>:
///   when a <see cref="ContinuousEffectsService"/> is supplied, two
///   <see cref="GrantAbilityEffect"/> instances re-project
///   <see cref="KeywordAbility"/>("Vigilance") /
///   <see cref="KeywordAbility"/>("Lifelink") onto the live equipped
///   creature each layer pass. <see cref="Majik.Core.Combat.CombatAbilities"/>
///   reads the keyword markers off the bearer for both vigilance (skip
///   declare-attackers untap) and lifelink (life-gain on damage). The
///   shape-only constructor (no service) falls back to stamping the
///   keyword markers on Batterskull itself, mirroring the
///   <see cref="HammerOfNazahnFactory"/> Indestructible fallback so
///   factory-shape / dispatch tests still observe the keywords somewhere
///   on the equipment.
/// - <b>{3}: Return Batterskull to its owner's hand</b> (CR 602.1) —
///   activated ability (instant-speed). Cost is <c>{3}</c>. On
///   resolution, Batterskull moves from the battlefield to its owner's
///   hand via raw zone moves; the equipment LTBs the battlefield
///   (CR 704.5n unattaches the Germ via the existing zone-move
///   pipeline, and the now-unboosted 0/0 Germ dies to SBAs on the next
///   loop). This is the entire reason Batterskull is the card it is —
///   bounce-and-replay dodges artifact destruction and gives the
///   lifelinker a recurring presence.
/// - <b>Equip {5}</b>: wait — Batterskull's printed equip cost is
///   actually {5} per Scryfall, NOT in the task brief. The brief omits
///   the equip cost (oracle on Scryfall reads "Equip {5}"); we wire
///   <see cref="EquipActivatedAbility"/> with that cost via the standard
///   Puresteel zero-cost provider hook.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only — suitable for
/// factory-shape / dispatch tests. The living-weapon ETB trigger is
/// attached for shape but not registered with a
/// <see cref="TriggerManager"/>; the static +4/+4 boost is not
/// registered against any <see cref="ContinuousEffectsService"/>.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically.
/// - <b>ETB-event dispatch for the spawned Germ</b> — when no
///   <see cref="ZoneService"/> is wired, the token enters via a raw
///   zone insert; downstream ETB-of-creature triggers (Soul Warden,
///   etc.) won't fire on the shape-only path.
/// </summary>
[CardName("Batterskull")]
public static class BatterskullFactory
{
    public const string CardName = "Batterskull";
    public const string Cost = "{5}";
    public const string EquipCost = "{5}";
    public const string ReturnCost = "{3}";

    /// <summary>
    /// Constructs Batterskull with no live runtime wiring (the shape /
    /// dispatcher path). The living-weapon ETB trigger is attached but
    /// not registered with a <see cref="TriggerManager"/>; the static
    /// +4/+4 boost is not registered against any
    /// <see cref="ContinuousEffectsService"/>; vigilance + lifelink
    /// markers are stamped on Batterskull itself for deterministic
    /// shape-only inspection.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null, zoneService: null);

    /// <summary>
    /// Constructs Batterskull with optional runtime services. When
    /// <paramref name="continuousEffects"/> is supplied the +4/+4 boost
    /// (Layer 7c) and the vigilance + lifelink grants (Layer 6) are
    /// registered against it. When <paramref name="triggers"/> is
    /// supplied the living-weapon ETB trigger is registered for
    /// bus-driven firing. When <paramref name="zoneService"/> is
    /// supplied the spawned Germ token routes through the service so
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
        // Static continuous effect — "Equipped creature gets +4/+4."
        // CR 613 Layer 7c. The effect gates on the source being on the
        // battlefield AND attached (see AttachedBoostEffect.IsActive).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 4, toughness: 4));
        }

        // --------------------------------------------------------------
        // Vigilance + lifelink grants — "Equipped creature has vigilance
        // and lifelink." (CR 702.20 / CR 702.15, CR 613.1f).
        //
        // With a ContinuousEffectsService supplied, two Layer-6
        // ability-grant effects re-project KeywordAbility("Vigilance") /
        // KeywordAbility("Lifelink") onto the live equipped creature.
        // The selectors read card.AttachedTo at sync time, so
        // re-equipping transfers the grants without re-registration;
        // LTB / Humility revoke them via the service's grant lifecycle.
        //
        // Shape-only path (no service): both keyword markers remain on
        // Batterskull itself so CombatAbilities.HasVigilance /
        // HasLifelink return a deterministic answer for factory-shape /
        // dispatch tests when callers point them at Batterskull (same
        // posture as Hammer of Nazahn's Indestructible fallback).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: bearer => new KeywordAbility(
                    "Vigilance", bearer, bearer.Controller ?? owner)));
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: bearer => new KeywordAbility(
                    "Lifelink", bearer, bearer.Controller ?? owner)));
        }
        else
        {
            card.AddAbility(new KeywordAbility("Vigilance", card, owner));
            card.AddAbility(new KeywordAbility("Lifelink", card, owner));
        }

        // --------------------------------------------------------------
        // Living weapon — CR 702.91 / CR 603.6a.
        //   "When Batterskull enters, create a 0/0 black Germ creature
        //    token, then attach Batterskull to it."
        // Wired as an ETB-self trigger that builds the Germ token via
        // TokenFactory.CreateOnBattlefield (so CardMovedEvent fires
        // when a ZoneService is wired) and immediately attaches the
        // equipment. The Germ is 0/0; the Layer-7c boost above brings
        // it to 4/4 once attached, so it survives SBAs (CR 704.5f).
        // --------------------------------------------------------------
        var livingWeaponEffect = new Effect(
            $"{CardName}: living weapon — create 0/0 black Germ + attach",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var ctrl = card.Controller ?? owner;

                var spec = new TokenFactory.TokenSpec(
                    Name: "Germ",
                    Power: 0,
                    Toughness: 0,
                    Subtypes: new[] { CardSubtype.Germ },
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
        // Activated ability — "{3}: Return Batterskull to its owner's
        //   hand." (CR 602.1, instant speed.)
        // Bounce primitive — same shape as Aether Spellbomb's
        // BounceToOwnersHand but targeting self. Idempotent: a
        // double-execution after Batterskull has already left the
        // battlefield no-ops.
        // --------------------------------------------------------------
        var bounceEffect = new Effect(
            $"{CardName}: return to owner's hand",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var holder = card.Controller ?? owner;

                // CR 704.5n — equipment leaving the battlefield
                // unattaches via the standard zone-move pipeline.
                // Permanent.Unattach is invoked defensively here so
                // the AttachedTo edge is dropped even on the shape-only
                // path where there's no ZoneService to publish the
                // move event.
                card.Unattach();

                holder.Zones.Battlefield.RemoveCard(card);
                owner.Zones.Hand.AddCard(card);
                card.SetZone(ZoneType.Hand);
            });

        var bounceAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(ReturnCost) },
            effects: new IEffect[] { bounceEffect });

        card.AddAbility(bounceAbility);

        // --------------------------------------------------------------
        // Equip {5} — activated ability (CR 702.6) via the shared
        // EquipActivatedAbility primitive. Threads the Puresteel
        // zero-cost provider hook for parity with the rest of the
        // equipment cycle.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
