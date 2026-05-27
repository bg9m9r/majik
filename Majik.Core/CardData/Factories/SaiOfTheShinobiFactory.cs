using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sai of the Shinobi (Saviors of Kamigawa, {1}).
///
/// Artifact — Equipment. Oracle text:
///   "Equipped creature has 'Whenever this creature deals damage, you
///    may untap target permanent.'"
///   "Equip {1}."
///
/// Ninjutsu / aggro-tempo equipment. The granted damage trigger fires
/// on any damage type (combat, spell, ability) — printed wording is
/// the broad "deals damage" not Sword-of-Fire-and-Ice's "combat damage
/// to a player". Free untap fuels things like
/// <see cref="UmezawasJitteFactory"/>'s tap-cost charge-counter
/// removals, mana lands, or Aether Vial.
///
/// ## Implementation
///
/// Same engine-equivalent shape as the Sword cycle (see
/// <see cref="SwordOfFireAndIceFactory"/> /
/// <see cref="SwordOfHearthAndHomeFactory"/>): the printed "equipped
/// creature has '...'" granted ability is modelled as a
/// <see cref="TriggeredAbility"/> attached to the EQUIPMENT itself with
/// the predicate keyed off
/// <see cref="Permanent.AttachedTo"/> — observationally indistinguishable
/// for the broad "deals damage" trigger (the granted ability's
/// controller is the equipped-creature's controller, which is the same
/// as the equipment's controller in v1 — neither layer 6
/// ability-granting nor Mind-Control-style split controllers are
/// modelled here).
///
/// - <b>Identity</b>: Artifact + Equipment subtype, mana cost {1}.
/// - <b>Damage-dealt trigger (CR 603.1 / 119)</b>: a single
///   <see cref="TriggeredAbility"/> over <see cref="DamageDealtEvent"/>
///   (NOT the more specific <see cref="CombatDamageDealtEvent"/>) gated
///   on (a) Sai is on the battlefield AND attached, and (b) the damage
///   <c>SourceCard</c> is the currently equipped creature. The trigger
///   declares a 1..1 "target permanent" <see cref="TargetRequest"/>
///   (Intent: <see cref="BotIntent.Untap"/> — same shape as
///   <see cref="PestermiteFactory"/>'s ETB tap-or-untap). On
///   resolution: the chosen permanent is untapped if it is currently
///   tapped (printed "may" is auto-accepted in v1 — same simplification
///   as Puresteel Paladin's ETB-draw "may"; declining to untap is
///   never strategically interesting for the controller).
/// - <b>Equip {1}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive, with the
///   <see cref="PuresteelPaladinFactory.ZeroEquipCostProvider"/> hook so
///   metalcraft / Sigarda's Aid synergy reads through.
///
/// ## Lifecycle
///
/// - <see cref="Create(Player)"/> — shape only. The damage trigger is
///   attached for inspection but not registered with a
///   <see cref="TriggerManager"/>.
/// - <see cref="Create(Player, TriggerManager?)"/> — wires the damage
///   trigger so a <see cref="DamageDealtEvent"/> from the equipped
///   creature auto-queues the ability.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Layer-6 ability grant</b>: printed text grants the trigger to
///   the equipped creature. The engine models the trigger on the
///   equipment itself — observationally identical for the printed body
///   (same source-controller, same fire condition), but Mind-Control /
///   Donate / control-swap shenanigans on the equipped creature would
///   diverge from paper (the granted ability should follow the
///   equipped creature's controller, not the equipment's).
///   <see cref="GrantAbilityEffect"/> exists for the layer-6 path; a
///   future revision can route through it once granted-trigger
///   registration with <see cref="TriggerManager"/> at grant time is
///   wired up (today <c>GrantAbilityEffect</c> only adds the ability
///   to <see cref="Card.Abilities"/>; the trigger manager has no
///   layer-6 binding hook).
/// - <b>"You may" prompt</b>: the untap is unconditional in v1 (auto-
///   accept). Same simplification as
///   <see cref="PuresteelPaladinFactory"/>'s ETB-draw "may".
/// - <b>Tap-an-untapped target</b>: the printed text is "untap target
///   permanent" — only untap, never tap. A no-op resolution against a
///   target that is ALREADY untapped is correct (CR 701.20 — "untap"
///   has no effect on an already-untapped permanent).
/// </summary>
[CardName("Sai of the Shinobi")]
public static class SaiOfTheShinobiFactory
{
    public const string CardName = "Sai of the Shinobi";
    public const string PrintedManaCost = "{1}";
    public const string EquipCost = "{1}";

    /// <summary>
    /// Construct Sai of the Shinobi with no live trigger-manager wiring
    /// (the shape / dispatcher path). The damage trigger is attached to
    /// the card but not registered.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, triggers: null);

    /// <summary>
    /// Construct Sai of the Shinobi. When <paramref name="triggers"/>
    /// is supplied, the damage-dealt trigger is registered for
    /// bus-driven firing so any <see cref="DamageDealtEvent"/> from the
    /// equipped creature queues the untap-target-permanent ability.
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: PrintedManaCost,
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Damage-dealt trigger — CR 603.1 / CR 119.
        //   "Equipped creature has 'Whenever this creature deals damage,
        //    you may untap target permanent.'"
        // The grant is modelled as a TriggeredAbility on the equipment
        // itself gated by AttachedTo (see class xmldoc — observationally
        // identical to a Layer 6 grant for the printed body). Predicate
        // matches any DamageDealtEvent whose SourceCard is the
        // currently-equipped creature.
        // ----------------------------------------------------------------
        TriggeredAbility? damageTrigger = null;
        var damageEffect = new Effect(
            $"{CardName}: may untap target permanent",
            () =>
            {
                if (damageTrigger == null) return;
                var slots = damageTrigger.ChosenTargets;
                if (slots.Count == 0 || slots[0].Count == 0) return; // printed "may" — no target
                if (slots[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-on-resolution. Target must still
                // be on the battlefield.
                if (target.Zone != ZoneType.Battlefield) return;

                // Printed "untap target permanent" — only untap, never
                // tap. Already-untapped target → no-op (CR 701.20).
                if (target.IsTapped) target.Untap();
            });

        damageTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<DamageDealtEvent>((e, _) =>
            {
                if (card.Zone != ZoneType.Battlefield) return false;
                var equipped = card.AttachedTo;
                if (equipped == null) return false;
                if (e.SourceCard == null) return false;
                return ReferenceEquals(e.SourceCard, equipped);
            }),
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target permanent",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(damageTrigger);
        triggers?.RegisterTriggeredAbility(damageTrigger);

        // ----------------------------------------------------------------
        // Equip {1} — activated ability (CR 702.6) via the shared
        // EquipActivatedAbility primitive. Threads the Puresteel
        // zero-cost provider hook.
        // ----------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
