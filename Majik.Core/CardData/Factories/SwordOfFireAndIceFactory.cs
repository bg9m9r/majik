using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sword of Fire and Ice (Darksteel, {3}).
///
/// Artifact — Equipment. Oracle text:
///   "Equipped creature gets +2/+2 and has protection from red and from
///    blue."
///   "Whenever equipped creature deals combat damage to a player, Sword
///    of Fire and Ice deals 2 damage to any target and you draw a card."
///   "Equip {2}."
///
/// ## Implementation
///
/// - <b>Static "equipped creature gets +2/+2"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (CR 613 Layer 7c). The
///   effect reads <see cref="Permanent.AttachedTo"/> dynamically, so
///   re-equipping transfers the boost without re-registration. Mirrors
///   <see cref="ColossusHammerFactory"/> / <see cref="SkullclampFactory"/>.
/// - <b>"Protection from red and from blue"</b> — CR 702.16. With a
///   <see cref="ContinuousEffectsService"/> wired, two
///   <see cref="GrantAbilityEffect"/> instances (CR 613.1f, Layer 6)
///   re-project <see cref="ProtectionAbility"/>("red") /
///   <see cref="ProtectionAbility"/>("blue") onto the live equipped
///   creature. The grant selectors read <see cref="Permanent.AttachedTo"/>
///   at sync time, so re-equipping transfers the protection automatically.
///   <see cref="Majik.Core.Rules.Protection.HasProtectionFromColor"/>
///   reads the markers off the bearer, so CR 702.16e (DEBT-A — damage /
///   enchanting / equipping / blocking / targeting) is correctly scoped
///   to the equipped creature instead of the equipment card. The
///   shape-only constructor (no service) falls back to leaving the two
///   markers on the equipment card so factory-shape / dispatch tests
///   still get a deterministic answer.
/// - <b>Combat-damage-to-a-player trigger (CR 510, CR 603.1)</b> — wired
///   over <see cref="CombatDamageDealtEvent"/> filtered to the equipped
///   creature (<see cref="Permanent.AttachedTo"/> at trigger-evaluation
///   time) and a non-null <see cref="CombatDamageDealtEvent.TargetPlayer"/>.
///   Same shape as <see cref="UmezawasJitteFactory"/>'s combat trigger,
///   but additionally gates on <c>TargetPlayer != null</c> (printed text
///   is "to a player", not Jitte's broader "deals combat damage"). On
///   resolution:
///     1. deals 2 damage to a chosen "any target" via
///        <see cref="OracleSpellBinder.DealDamage"/>; and
///     2. draws one card for the equipment controller.
///   A 1..1 "any target" <see cref="TargetRequest"/> is attached for
///   shape parity with Jitte's modal damage mode — agents populate
///   <see cref="TriggeredAbility.ChosenTargets"/> before resolution.
///   When no target is supplied (shape-only path) the damage is a
///   no-op but the draw still resolves (paired effects — CR 608.2b
///   "do as much as possible").
/// - <b>Equip {2}</b> — activated ability (CR 702.6). Cost is <c>{2}</c>.
///   v1 picker is deterministic: the first creature on the controller's
///   battlefield. Same shape as <see cref="UmezawasJitteFactory"/>.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only — suitable for
/// factory-shape / dispatch tests. The combat-damage trigger is attached
/// for shape but not registered with a <see cref="TriggerManager"/>; the
/// static +2/+2 boost is not registered against any
/// <see cref="ContinuousEffectsService"/>. Use the overload to wire
/// runtime services.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically.
/// - <b>Real "any target" prompt</b> for the combat trigger — v1
///   honours pre-supplied targets via
///   <see cref="TriggeredAbility.SetChosenTargets"/>; absent a chosen
///   target the damage half no-ops while the draw half still resolves.
/// </summary>
[CardName("Sword of Fire and Ice")]
public static class SwordOfFireAndIceFactory
{
    public const string CardName = "Sword of Fire and Ice";
    public const string Cost = "{3}";
    public const string EquipCost = "{2}";

    /// <summary>
    /// Constructs Sword of Fire and Ice with no live runtime wiring (the
    /// shape / dispatcher path). The +2/+2 boost is not registered against
    /// any service; the combat-damage trigger is attached to the card but
    /// not registered with a <see cref="TriggerManager"/>. Protection
    /// markers are present.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null);

    /// <summary>
    /// Constructs Sword of Fire and Ice. When
    /// <paramref name="continuousEffects"/> is supplied the +2/+2
    /// boost (Layer 7c) is registered against it; the effect gates on
    /// the Sword being on the battlefield AND attached to a battlefield
    /// permanent. When <paramref name="triggers"/> is supplied the
    /// combat-damage-to-a-player trigger is registered so a
    /// <see cref="CombatDamageDealtEvent"/> from the equipped creature
    /// (targeting a player) automatically queues the ability.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: Cost,
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // Static continuous effect — "Equipped creature gets +2/+2."
        // CR 613 Layer 7c. The effect gates on the source being on the
        // battlefield AND attached (see AttachedBoostEffect.IsActive).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 2, toughness: 2));
        }

        // --------------------------------------------------------------
        // Protection grants — "Equipped creature has protection from red
        // and from blue." (CR 702.16, CR 613.1f).
        //
        // When a ContinuousEffectsService is supplied, two Layer-6
        // ability-grant effects re-project ProtectionAbility("red") /
        // ProtectionAbility("blue") onto the live equipped creature. The
        // selectors read card.AttachedTo at sync time, so re-equipping
        // transfers the grants without re-registration; LTB / Humility
        // revoke them via the service's grant lifecycle.
        //
        // Shape-only path (no service): both ProtectionAbility markers
        // remain on the equipment card so HasProtectionFromColor still
        // returns a deterministic answer for factory-shape / dispatch
        // tests. With the service wired the markers live on the equipped
        // creature itself, which is what CR 702.16e (DEBT-A) reads.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: _ => new ProtectionAbility("red")));
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: _ => new ProtectionAbility("blue")));
        }
        else
        {
            card.AddAbility(new ProtectionAbility("red"));
            card.AddAbility(new ProtectionAbility("blue"));
        }

        // --------------------------------------------------------------
        // Combat-damage-to-a-player trigger — CR 510 / CR 603.1.
        //   "Whenever equipped creature deals combat damage to a
        //    player, Sword of Fire and Ice deals 2 damage to any target
        //    and you draw a card."
        // Matches any CombatDamageDealtEvent whose Source is the
        // currently-equipped creature AND TargetPlayer != null.
        // --------------------------------------------------------------
        TriggeredAbility? combatTrigger = null;
        var combatEffect = new Effect(
            $"{CardName}: deal 2 damage to any target and draw a card",
            () =>
            {
                // 1) Deal 2 damage to the chosen "any target". No-op when
                //    no target was supplied (CR 608.2b — do as much as
                //    possible; the paired draw still resolves).
                if (combatTrigger != null
                    && combatTrigger.ChosenTargets.Count > 0
                    && combatTrigger.ChosenTargets[0].Count > 0)
                {
                    var target = combatTrigger.ChosenTargets[0][0];
                    OracleSpellBinder.DealDamage(target, 2);
                }

                // 2) Draw a card. Empty-library flags the loss-condition
                //    SBA stamp (CR 704.5b / 120.3).
                DrawOne(owner);
            });

        combatTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (e.TargetPlayer == null) return false;
                var equipped = card.AttachedTo;
                if (equipped == null) return false;
                return ReferenceEquals(e.Source, equipped);
            }),
            effects: new IEffect[] { combatEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(combatTrigger);
        triggers?.RegisterTriggeredAbility(combatTrigger);

        // --------------------------------------------------------------
        // Equip {2} — activated ability (CR 702.6).
        //   "{2}: Attach to target creature you control. Activate only
        //    as a sorcery."
        // v1 picker: deterministic first controller-side creature.
        // CR 117.1a / 307.5 — sorcery-speed restriction now enforced via
        // ActionValidator.
        // --------------------------------------------------------------
        var equipEffect = new Effect(
            $"{CardName}: equip — attach to a creature you control",
            () =>
            {
                var bearer = owner.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .FirstOrDefault(c => ReferenceEquals(c.Controller, owner));
                if (bearer == null) return; // No legal target → no-op.
                card.AttachTo(bearer);
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
    /// Draw a single card for <paramref name="player"/> via raw library →
    /// hand zone moves. Empty-library halts the draw and stamps
    /// <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> so the SBA
    /// loop notes the loss condition (CR 704.5b / 120.3). Mirrors the
    /// simple-draw shape used by other shape-only factory paths
    /// (see <see cref="SkullclampFactory"/>).
    /// </summary>
    private static void DrawOne(Player player)
    {
        var top = player.Zones.Library.GetCards().FirstOrDefault();
        if (top == null)
        {
            player.MarkTriedToDrawFromEmptyLibrary();
            return;
        }
        player.Zones.Library.RemoveCard(top);
        player.Zones.Hand.AddCard(top);
        top.SetZone(ZoneType.Hand);
    }
}
