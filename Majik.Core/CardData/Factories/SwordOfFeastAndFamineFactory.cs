using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sword of Feast and Famine (Mirrodin Besieged, {3}).
///
/// Artifact — Equipment. Oracle text:
///   "Equipped creature gets +2/+2 and has protection from black and from green."
///   "Whenever equipped creature deals combat damage to a player, that
///    player discards a card and you untap all lands you control."
///   "Equip {2}."
///
/// ## Implementation
///
/// - <b>Static "equipped creature gets +2/+2"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (P/T modification,
///   CR 613 Layer 7c). The effect reads the source's
///   <see cref="Permanent.AttachedTo"/> dynamically, so re-equipping the
///   Sword transfers the boost without re-registration — same pattern as
///   <see cref="ColossusHammerFactory"/> / <see cref="UmezawasJitteFactory"/>.
/// - <b>Static "has protection from black and from green"</b> — granted
///   to the equipped creature via two
///   <see cref="AttachedAuraAbilityGrantStaticEffect"/> lifecycles (one
///   per colour). Each adds a <see cref="ProtectionAbility"/> to the
///   bearer's <see cref="Card.Abilities"/> collection while the Sword is
///   attached and on the battlefield, and revokes the grant on detach /
///   LTB. <see cref="Majik.Core.Rules.Protection.HasProtectionFromColor"/>
///   scans <c>Abilities</c> for <see cref="ProtectionAbility"/> entries,
///   so the granted abilities feed the same downstream gameplay rules
///   (CR 702.16) used for printed protection. Lifecycle subscribes to the
///   shared <see cref="IEventBus"/> when one is supplied; without a bus
///   the lifecycle still grants synchronously via
///   <see cref="AttachedAuraAbilityGrantStaticEffect.Sync"/>.
/// - <b>Combat-damage-to-a-player trigger</b> — fires on a
///   <see cref="CombatDamageDealtEvent"/> whose
///   <see cref="CombatDamageDealtEvent.Source"/> matches the source's
///   current <see cref="Permanent.AttachedTo"/> AND whose
///   <see cref="DamageDealtEvent.TargetPlayer"/> is non-null (CR 510 /
///   CR 603.1). On resolution:
///     1. the damaged player discards a card — v1 deterministically picks
///        the first card in hand (same v1 policy as
///        <see cref="LilianaOfTheVeilFactory"/>'s +1 each-player-discards
///        and <see cref="FaithlessLootingFactory"/>'s last-2-in-hand;
///        agent prompt deferred);
///     2. every <see cref="Land"/> the Sword's controller controls on the
///        battlefield is untapped (CR 701.20 — untap a permanent). Each
///        <see cref="Permanent.Untap"/> call is guarded by an
///        <see cref="Permanent.IsTapped"/> check because Untap throws on
///        an already-untapped permanent.
/// - <b>Equip {2}</b> — activated ability (CR 702.6a / 702.6d). Cost is
///   <c>{2}</c>. v1 picker: deterministic first creature on the
///   controller's battlefield. Same shape as
///   <see cref="ColossusHammerFactory"/> / <see cref="UmezawasJitteFactory"/>.
///
/// ## Lifecycle
///
/// When the runtime overload is used, the +2/+2 boost is registered into
/// the supplied <see cref="ContinuousEffectsService"/>, and the two
/// <see cref="AttachedAuraAbilityGrantStaticEffect"/> lifecycles
/// (black + green) are attached against the optional
/// <see cref="IEventBus"/>. The combat-damage trigger is registered into
/// the supplied <see cref="TriggerManager"/> when present. The single-arg
/// overload omits service wiring and produces the correct card shape only
/// — suitable for factory-shape / dispatch tests.
///
/// ## Deferred
///
/// - <b>Sorcery-speed restriction</b> on Equip activation (CR 702.6a) —
///   same gap as the rest of the equipment cycle; enforcement belongs in
///   an action-validator gate, not on the ability itself.
/// - <b>Attach-target prompt</b> for "target creature you control"
///   (CR 702.6b) — v1 deterministic first creature.
/// - <b>Discard prompt</b> — v1 picks the first card in the damaged
///   player's hand. Agent-driven "you choose which card you discard" (CR
///   701.16a — damaged player chooses) is deferred behind the same prompt
///   queue as Liliana of the Veil + Faithless Looting.
/// </summary>
public static class SwordOfFeastAndFamineFactory
{
    public const string CardName = "Sword of Feast and Famine";
    public const string Cost = "{3}";
    public const string EquipCost = "{2}";

    /// <summary>
    /// Constructs Sword of Feast and Famine with no live runtime wiring
    /// (shape / dispatcher path). The +2/+2 boost and protection grants
    /// are not registered against any continuous-effects service; the
    /// combat-damage trigger is attached for shape but not registered
    /// with a <see cref="TriggerManager"/>.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, eventBus: null, triggers: null);

    /// <summary>
    /// Constructs Sword of Feast and Famine with optional runtime
    /// services. When <paramref name="continuousEffects"/> is supplied
    /// the static +2/+2 boost is registered. The protection-from-black
    /// and protection-from-green ability grants are attached
    /// unconditionally (their lifecycles re-sync against the supplied
    /// <paramref name="eventBus"/> on aura-source moves, and call
    /// <c>Sync()</c> once at construction so an immediate <c>AttachTo</c>
    /// is picked up without bus traffic). When
    /// <paramref name="triggers"/> is supplied the combat-damage trigger
    /// is registered so a <see cref="CombatDamageDealtEvent"/> from the
    /// equipped creature automatically queues the ability.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: Cost,
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Static continuous effect — "Equipped creature gets +2/+2."
        // CR 613 Layer 7c. The boost gates on the source being on the
        // battlefield AND attached (see AttachedBoostEffect.IsActive).
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 2, toughness: 2));
        }

        // ----------------------------------------------------------------
        // Static ability grants — "has protection from black and from
        // green" (CR 702.16). Implemented as two
        // AttachedAuraAbilityGrantStaticEffect lifecycles, one per colour.
        // Each grant adds a ProtectionAbility(colour) to the equipped
        // creature's Abilities collection while the Sword is attached +
        // on the battlefield. Majik.Core.Rules.Protection scans the
        // bearer's Abilities for ProtectionAbility, so the grants feed
        // the same downstream targeting / damage / blocking gates used
        // for printed protection.
        //
        // Attach()'ing the lifecycle calls Sync() once immediately, so
        // an AttachTo() executed before Attach() also gets picked up.
        // Without a bus the lifecycle still works for synchronous
        // attach/detach (callers can call Sync() manually after a
        // re-equip).
        // ----------------------------------------------------------------
        var protBlack = new AttachedAuraAbilityGrantStaticEffect(
            auraSource: card,
            eventBus: eventBus,
            abilityFactory: _ => new ProtectionAbility("black"));
        var protGreen = new AttachedAuraAbilityGrantStaticEffect(
            auraSource: card,
            eventBus: eventBus,
            abilityFactory: _ => new ProtectionAbility("green"));

        protBlack.Attach();
        protGreen.Attach();

        // Expose the lifecycles via per-card registry so tests / lifecycle-
        // aware callers can Sync() after manual AttachTo() outside the bus
        // path. Mirrors SplinterTwinFactory's pattern.
        ProtectionGrants.SetLifecycles(card, protBlack, protGreen);

        // ----------------------------------------------------------------
        // Combat-damage-to-a-player trigger — CR 510, CR 603.1.
        //   "Whenever equipped creature deals combat damage to a player,
        //    that player discards a card and you untap all lands you
        //    control."
        // The predicate captures the damaged player off the event so the
        // resolved effect targets the correct hand at fire time. The
        // capture lives in a closure shared with the effect — CR 603.3
        // evaluates the trigger condition before the ability hits the
        // stack, so the captured player is fresh by the time the effect
        // resolves (same pattern as RagavanNimblePilfererFactory).
        // ----------------------------------------------------------------
        Player? capturedDamaged = null;

        var combatEffect = new Effect(
            $"{CardName}: damaged player discards 1 + untap all your lands",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;

                // 1) Damaged player discards a card (CR 701.16a). v1
                //    deterministic first-card-in-hand pick; the printed
                //    "that player discards a card" leaves the chooser
                //    as the discarding player, but no agent prompt is
                //    wired yet.
                var victim = capturedDamaged;
                if (victim != null)
                {
                    var pick = victim.Zones.Hand.GetCards().FirstOrDefault();
                    if (pick != null)
                    {
                        victim.Zones.Hand.RemoveCard(pick);
                        victim.Zones.Graveyard.AddCard(pick);
                        pick.SetZone(ZoneType.Graveyard);
                    }
                }

                // 2) Untap all lands the Sword's controller controls
                //    (CR 701.20). Permanent.Untap() throws on an
                //    already-untapped permanent, so each call is gated.
                var controller = card.Controller ?? owner;
                foreach (var land in controller.Zones.Battlefield.GetCards().OfType<Land>())
                {
                    if (land.IsTapped) land.Untap();
                }
            });

        var combatTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                var equipped = card.AttachedTo;
                if (equipped == null) return false;
                if (!ReferenceEquals(e.Source, equipped)) return false;
                if (e.TargetPlayer == null) return false; // damage to a player only
                capturedDamaged = e.TargetPlayer;
                return true;
            }),
            effects: new IEffect[] { combatEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(combatTrigger);
        triggers?.RegisterTriggeredAbility(combatTrigger);

        // ----------------------------------------------------------------
        // Equip {2} — activated ability (CR 702.6).
        //   "{2}: Attach to target creature you control. Activate only
        //    as a sorcery."
        // v1 picker: deterministic first controller-side creature.
        // Sorcery-speed restriction deferred (see class xmldoc). The
        // attach-resolution path also calls Sync() on the protection
        // lifecycles so test/non-bus paths see the grant immediately
        // after a re-equip.
        // ----------------------------------------------------------------
        var equipEffect = new Effect(
            $"{CardName}: equip — attach to a creature you control",
            () =>
            {
                var bearer = owner.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .FirstOrDefault(c => ReferenceEquals(c.Controller, owner));
                if (bearer == null) return;
                card.AttachTo(bearer);
                protBlack.Sync();
                protGreen.Sync();
            });

        var equipAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(EquipCost) },
            effects: new IEffect[] { equipEffect });

        card.AddAbility(equipAbility);

        return card;
    }

    /// <summary>
    /// Per-card registry for the two protection-grant lifecycles. Lets
    /// tests / lifecycle-aware callers call <see cref="Sync"/> after
    /// re-equipping outside the bus path. Mirrors
    /// <see cref="SplinterTwinFactory"/>'s lifecycle-stash pattern.
    /// </summary>
    public static class ProtectionGrants
    {
        private static readonly System.Collections.Generic.Dictionary<
            Artifact,
            (AttachedAuraAbilityGrantStaticEffect Black, AttachedAuraAbilityGrantStaticEffect Green)>
            _lifecycles = new();

        public static void SetLifecycles(
            Artifact sword,
            AttachedAuraAbilityGrantStaticEffect black,
            AttachedAuraAbilityGrantStaticEffect green)
        {
            _lifecycles[sword] = (black, green);
        }

        public static (AttachedAuraAbilityGrantStaticEffect Black, AttachedAuraAbilityGrantStaticEffect Green)?
            GetLifecycles(Artifact sword)
            => _lifecycles.TryGetValue(sword, out var l) ? l : null;

        /// <summary>
        /// Re-sync both protection grants for the given sword. Call after
        /// a manual <see cref="Permanent.AttachTo"/> outside the bus path.
        /// </summary>
        public static void Sync(Artifact sword)
        {
            if (_lifecycles.TryGetValue(sword, out var l))
            {
                l.Black.Sync();
                l.Green.Sync();
            }
        }
    }
}
