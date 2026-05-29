using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hellrider (Dark Ascension, {2}{R}{R}).
///
/// Creature — Devil 3/3. Oracle text:
///   "Haste.
///    Whenever a creature you control attacks, this creature deals 1 damage
///    to the player or planeswalker it's attacking."
///
/// ## Implemented (v1)
/// - 3/3 Creature — Devil, mana cost {2}{R}{R}, owner/controller wired.
/// - <b>Haste (CR 702.10)</b>: <see cref="KeywordAbility"/> marker —
///   <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/> reads it.
///   Same wiring shape as <see cref="GoblinGuideFactory"/>.
/// - <b>Attack trigger (CR 508.1f)</b>: triggered ability over
///   <see cref="CreatureAttacksEvent"/> filtered to attackers whose
///   controller is Hellrider's controller — i.e. "a creature you control
///   attacks" (CR 109.5 — "you"). This is NOT a self-only trigger
///   (contrast <see cref="GoblinGuideFactory"/>'s
///   <see cref="Triggers.OnAttackSelf"/>); Hellrider's own attack also
///   counts because Hellrider is a creature its controller controls.
///   On resolution Hellrider deals 1 damage to the
///   <see cref="CreatureAttacksEvent.DefendingPlayerOrPlaneswalker"/> the
///   attacking creature is attacking — routed through
///   <see cref="Fx.DealDamageAny"/> so a Player defender loses 1 life
///   (CR 119) and a Planeswalker defender loses 1 loyalty (CR 306.7 /
///   CR 120.3). Defender is captured off the live event in the condition
///   closure (same per-fire capture pattern as
///   <see cref="GoblinGuideFactory"/> + Ragavan, Nimble Pilferer).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Haste keyword + attack
///   trigger attached; trigger not registered with any
///   <see cref="TriggerManager"/>. Suitable for dispatcher / structural
///   tests. The damage resolution still runs against the captured
///   defender when the effect is executed directly.
/// - <see cref="Create(Player, TriggerManager?)"/> — registers the attack
///   trigger with the supplied <see cref="TriggerManager"/> so a
///   <see cref="CreatureAttacksEvent"/> for any of the controller's
///   attackers queues the ability on the stack (CR 603.2).
///
/// ## Deferred (v1 gaps)
/// - <b>Per-attacker defender capture</b>: the defender is captured into
///   shared closure state on a single <see cref="TriggeredAbility"/>
///   instance (the same posture as <see cref="GoblinGuideFactory"/>). The
///   engine fires one ability instance per matching attacker and resolves
///   each on the stack before the next, so the captured value is correct
///   at the moment each instance resolves. A future per-fire bound-context
///   on triggered abilities would make this immune to any reordering.
/// </summary>
[CardName("Hellrider")]
public static class HellriderFactory
{
    public const string CardName = "Hellrider";
    public const string PrintedManaCost = "{2}{R}{R}";
    public const int Power = 3;
    public const int Toughness = 3;
    public const int DamageAmount = 1;

    /// <summary>
    /// Construct Hellrider with no live runtime wiring. Haste marker is
    /// wired; the attack trigger is attached to the card shape but not
    /// registered with any <see cref="TriggerManager"/>. Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Hellrider with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the attack trigger registers
    /// with the manager so a <see cref="CreatureAttacksEvent"/> matching
    /// any creature the controller controls automatically queues the
    /// ability on the stack (CR 603.2).</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Devil });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.10 — printed Haste. Marker only; CombatAbilities.HasHaste
        // reads the KeywordAbility.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // Attack trigger — CR 508.1f.
        //   "Whenever a creature you control attacks, this creature deals
        //    1 damage to the player or planeswalker it's attacking."
        // The defender (player or planeswalker the attacker is attacking)
        // is captured off the live CreatureAttacksEvent in the condition
        // closure, then read by the resolution effect (same per-fire
        // capture pattern as GoblinGuideFactory).
        // ----------------------------------------------------------------
        object? capturedDefender = null;

        var attackEffect = Fx.Inline(
            $"{CardName}: deal {DamageAmount} damage to the attacked player or planeswalker",
            () =>
            {
                var victim = capturedDefender;
                if (victim is null) return;

                // CR 119 / CR 306.7 — route Player → life loss,
                // Planeswalker → loyalty removal.
                Fx.DealDamageAny(victim, DamageAmount);
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CreatureAttacksEvent>(
                (e, _) =>
                {
                    // CR 109.5 — "a creature YOU control". Match any
                    // attacker controlled by Hellrider's controller
                    // (includes Hellrider itself).
                    if (!ReferenceEquals(e.Attacker.Controller, card.Controller ?? owner))
                    {
                        return false;
                    }

                    // CR 506.2 — capture the player/planeswalker this
                    // attacker is attacking for the resolved effect.
                    capturedDefender = e.DefendingPlayerOrPlaneswalker;
                    return true;
                }),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }
}
