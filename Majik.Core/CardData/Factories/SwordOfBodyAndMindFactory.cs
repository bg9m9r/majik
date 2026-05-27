using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sword of Body and Mind (Scars of Mirrodin, {3}).
///
/// Artifact — Equipment. Oracle text:
///   "Equipped creature gets +2/+2 and has protection from green and from
///    blue."
///   "Whenever equipped creature deals combat damage to a player, you
///    create a 2/2 green Wolf creature token and that player puts the top
///    ten cards of their library into their graveyard."
///   "Equip {2}."
///
/// ## Implementation
///
/// - <b>Static "equipped creature gets +2/+2"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (CR 613 Layer 7c).
///   Mirrors <see cref="SwordOfFireAndIceFactory"/>.
/// - <b>"Protection from green and from blue"</b> — CR 702.16. With a
///   <see cref="ContinuousEffectsService"/> wired, two
///   <see cref="GrantAbilityEffect"/> instances (CR 613.1f, Layer 6)
///   re-project <see cref="ProtectionAbility"/>("green") /
///   <see cref="ProtectionAbility"/>("blue") onto the live equipped
///   creature. Shape-only path leaves both markers on the equipment card.
/// - <b>Combat-damage-to-a-player trigger (CR 510, CR 603.1)</b> — gated
///   on equipped creature + non-null
///   <see cref="DamageDealtEvent.TargetPlayer"/>. On resolution:
///     1. controller creates a 2/2 green Wolf creature token via
///        <see cref="TokenFactory.CreateOnBattlefield"/>;
///     2. damaged player mills the top ten cards of their library via
///        <see cref="MillAction.Apply"/> (CR 701.13). Mill of an empty
///        library is a no-op, not a loss condition (CR 701.13b / 704.5b).
/// - <b>Equip {2}</b> — activated ability (CR 702.6) via
///   <see cref="EquipActivatedAbility"/>.
///
/// ## Lifecycle
///
/// The single-arg overload omits service wiring (shape-only path). The
/// runtime overload registers the +2/+2 boost + protection grants against
/// the supplied <see cref="ContinuousEffectsService"/>; the combat-damage
/// trigger is registered against the supplied <see cref="TriggerManager"/>;
/// token creation routes through the optional <see cref="ZoneService"/>
/// so <see cref="Majik.Core.Events.CardMovedEvent"/> fires for ETB
/// listeners (Soul Warden etc.).
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically.
/// </summary>
[CardName("Sword of Body and Mind")]
public static class SwordOfBodyAndMindFactory
{
    public const string CardName = "Sword of Body and Mind";
    public const string Cost = "{3}";
    public const string EquipCost = "{2}";
    public const int MillCount = 10;
    public const int WolfPower = 2;
    public const int WolfToughness = 2;

    /// <summary>
    /// Constructs Sword of Body and Mind with no live runtime wiring
    /// (shape / dispatcher path).
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null, zones: null);

    /// <summary>
    /// Constructs Sword of Body and Mind. When
    /// <paramref name="continuousEffects"/> is supplied the +2/+2 boost
    /// (Layer 7c) is registered and two Layer-6 protection grants
    /// re-project ProtectionAbility("green") / ProtectionAbility("blue")
    /// onto the live equipped creature. When <paramref name="triggers"/>
    /// is supplied the combat-damage trigger is registered. The Wolf
    /// token routes through the supplied <paramref name="zones"/> so
    /// CardMovedEvent fires for downstream ETB listeners.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers,
        ZoneService? zones)
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
        // CR 613 Layer 7c.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 2, toughness: 2));
        }

        // --------------------------------------------------------------
        // Protection grants — "Equipped creature has protection from
        // green and from blue." (CR 702.16, CR 613.1f).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: _ => new ProtectionAbility("green")));
            continuousEffects.Register(new GrantAbilityEffect(
                source: card,
                targetSelector: () => card.AttachedTo,
                abilityFactory: _ => new ProtectionAbility("blue")));
        }
        else
        {
            card.AddAbility(new ProtectionAbility("green"));
            card.AddAbility(new ProtectionAbility("blue"));
        }

        // --------------------------------------------------------------
        // Combat-damage-to-a-player trigger — CR 510 / CR 603.1.
        //   "Whenever equipped creature deals combat damage to a player,
        //    you create a 2/2 green Wolf creature token and that player
        //    puts the top ten cards of their library into their
        //    graveyard."
        //
        // Captures the damaged player off the event so the resolved
        // effect mills the correct library at fire time (same pattern
        // as SwordOfFeastAndFamineFactory).
        // --------------------------------------------------------------
        Player? capturedDamaged = null;

        var combatEffect = new Effect(
            $"{CardName}: create a 2/2 green Wolf and damaged player mills 10",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;

                var controller = card.Controller ?? owner;

                // 1) Create a 2/2 green Wolf creature token (CR 111 /
                //    TokenFactory). Routes through ZoneService when
                //    supplied so CardMovedEvent fires.
                TokenFactory.CreateOnBattlefield(
                    new TokenFactory.TokenSpec(
                        Name: "Wolf",
                        Power: WolfPower,
                        Toughness: WolfToughness,
                        Subtypes: new[] { CardSubtype.Wolf },
                        Colors: new[] { ManaColor.Green }),
                    controller,
                    zones);

                // 2) Damaged player mills top 10 (CR 701.13). Empty /
                //    short library is a clean no-op (CR 701.13b /
                //    704.5b — mill is not a draw, so no loss flag).
                var victim = capturedDamaged;
                if (victim != null)
                {
                    MillAction.Apply(victim, MillCount);
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

        // --------------------------------------------------------------
        // Equip {2} — activated ability (CR 702.6).
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
