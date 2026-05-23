using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boros Reckoner (Gatecrash, {R/W}{R/W}{R/W}).
///
/// Creature — Minotaur Wizard 3/3. Oracle text:
///   "First strike. Whenever Boros Reckoner is dealt damage, it deals that
///    much damage to any target."
///
/// ## Implemented (v1)
/// - 3/3 Creature — Minotaur Wizard, mana cost {R/W}{R/W}{R/W} (CR 107.4e
///   hybrid pips — <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/>
///   accepts each pip and decomposes into a single <c>HybridPip</c>).
/// - First strike keyword wired via <see cref="KeywordAbility"/>; combat
///   code reads the marker through <see cref="Majik.Core.Combat.CombatAbilities.HasFirstStrike"/>.
/// - <b>Damage-received trigger</b> (CR 603.1) wired over
///   <see cref="DamageDealtEvent"/> filtered to <c>TargetCard == this</c>.
///   The amount of damage is captured off the event and forwarded by the
///   resolved effect to the configured redirect target via
///   <see cref="OracleSpellBinder.DealDamage"/> (any-target: player /
///   creature / planeswalker). When an <see cref="IEventBus"/> is supplied
///   the effect republishes the redirect as a non-combat
///   <see cref="DamageDealtEvent"/> with <c>DamageType.Ability</c>
///   (CR 119.2c) so the portal can animate the ping.
///
/// ## v1 simplification — trigger vs. replacement effect
/// Printed Boros Reckoner is a *replacement* effect ("If a source would
/// deal damage to Boros Reckoner, instead it deals that damage to any
/// target") — CR 614. The engine has no source-damage redirect primitive
/// yet (the existing <see cref="Majik.Core.Replacements.PreventNextDamageFromChosenSourceShield"/>
/// shape covers prevention, not redirect). To keep this PR small the
/// ability is modelled as a triggered effect "whenever Boros Reckoner is
/// dealt damage, it deals that much to any target" — which differs from
/// the printed card in three observable ways:
///   1. The damage still resolves on Boros Reckoner (marked damage / SBAs
///      can kill it) before the redirect fires; the replacement version
///      prevents the damage entirely.
///   2. The redirected damage is from Boros Reckoner, on the stack,
///      rather than the replacement-shifted instance.
///   3. The redirect is now subject to the priority loop (the trigger
///      goes on the stack, opponents can respond).
/// A future PR can convert this to a proper replacement effect once the
/// source-damage redirect primitive lands.
///
/// ## Redirect target
/// Set <see cref="BorosReckonerTrigger.RedirectTarget"/> before the trigger
/// resolves to choose the any-target (Player / Creature / Planeswalker).
/// When null at resolution the effect is a no-op (no implicit choice — the
/// caller is expected to wire a real prompt or default in production
/// code, mirroring Goblin Bombardment's <c>DamageTarget</c> pattern).
/// </summary>
public static class BorosReckonerFactory
{
    /// <summary>
    /// Construct Boros Reckoner with no live event-bus / TriggerManager
    /// wiring. The damage-received trigger is attached for shape but not
    /// registered, and the redirect publishes no <see cref="DamageDealtEvent"/>.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Boros Reckoner with optional runtime services. When
    /// <paramref name="eventBus"/> is supplied the redirect republishes a
    /// non-combat <see cref="DamageDealtEvent"/> (CR 119.2c); when
    /// <paramref name="triggers"/> is supplied the damage-received trigger
    /// is registered so a <see cref="DamageDealtEvent"/> automatically
    /// queues the ability.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Boros Reckoner",
            manaCost: "{R/W}{R/W}{R/W}",
            power: 3,
            toughness: 3,
            supertypes: null,
            subtypes: new[] { CardSubtype.Minotaur, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // First strike keyword marker (CR 702.7). Read by CombatFlow's
        // two-step damage sequencer via CombatAbilities.HasFirstStrike.
        card.AddAbility(new KeywordAbility("First strike", source: card, controller: owner));

        // ----------------------------------------------------------------
        // Damage-received trigger — CR 603.1.
        //   "Whenever Boros Reckoner is dealt damage, it deals that much
        //    damage to any target."
        // Matches DamageDealtEvent (and its CombatDamageDealtEvent
        // subclass) where TargetCard is this Boros Reckoner. The amount
        // is captured in a closure shared with the resolved effect.
        // ----------------------------------------------------------------
        int capturedAmount = 0;

        var effect = new Effect(
            "Boros Reckoner: deal captured amount of damage to redirect target",
            () =>
            {
                // RedirectTarget is set on the ability instance below.
                // No-op when unset or when the captured amount is 0.
                if (capturedAmount <= 0) return;

                var trig = card.Abilities.OfType<BorosReckonerTrigger>().FirstOrDefault();
                var target = trig?.RedirectTarget;
                if (target == null) return;

                // CR 119.2c — non-combat damage from a triggered ability.
                Player? targetPlayer = target as Player;
                ICard? targetCard = target as ICard;
                eventBus?.Publish(new DamageDealtEvent(
                    sourceCard: card,
                    sourcePlayer: null,
                    targetCard: targetCard,
                    targetPlayer: targetPlayer,
                    amount: capturedAmount,
                    damageType: DamageType.Ability));

                // Mutate state through the shared damage primitive.
                OracleSpellBinder.DealDamage(target, capturedAmount);

                // Clear captured amount so a future fire doesn't reuse it.
                capturedAmount = 0;
            });

        var trigger = new BorosReckonerTrigger(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<DamageDealtEvent>((e, _) =>
            {
                if (e.TargetCard is not Creature recv) return false;
                if (!ReferenceEquals(recv, card)) return false;
                if (e.Amount <= 0) return false;
                capturedAmount = e.Amount;
                return true;
            }),
            effects: new IEffect[] { effect });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}

/// <summary>
/// Boros Reckoner's damage-received triggered ability. Subclasses
/// <see cref="TriggeredAbility"/> so the chosen any-target travels with
/// the ability instance (test / bot setter), mirroring
/// <see cref="GoblinBombardmentAbility"/>'s pattern for activated
/// abilities.
/// </summary>
public sealed class BorosReckonerTrigger : TriggeredAbility
{
    /// <summary>
    /// The "any target" for the redirected damage. Accepts a
    /// <see cref="Player"/>, <see cref="Creature"/>, or
    /// <see cref="Majik.Core.Cards.Planeswalker"/>. When null at
    /// resolution time the effect is a no-op.
    /// </summary>
    public object? RedirectTarget { get; set; }

    public BorosReckonerTrigger(
        ICard source,
        Player controller,
        ITriggerCondition condition,
        IEffect[] effects)
        : base(
            source: source,
            controller: controller,
            condition: condition,
            effects: effects,
            activeZones: new[] { ZoneType.Battlefield })
    {
    }
}
