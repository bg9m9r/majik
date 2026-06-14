using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fear of Falling (Duskmourn, {3}{U}{U}).
///
/// Enchantment Creature — Nightmare 4/4. Oracle text (verified against
/// Scryfall):
///   "Flying
///    Whenever this creature attacks, target creature defending player
///    controls gets -2/-0 and loses flying until your next turn."
///
/// ## Implementation
///
/// - 4/4 blue Nightmare Enchantment Creature, mana cost {3}{U}{U}, via
///   <see cref="PermanentBuilders.EnchantmentCreature"/> (CR 205.2a — dual
///   Creature + Enchantment type, same posture as Fear of Missing Out and the
///   other enchantment creatures).
/// - <b>Flying</b> (CR 702.9) — a <see cref="KeywordAbility"/> marker, same
///   shape as <see cref="ColossalSkyturtleFactory"/> / Air Elemental.
/// - <b>Attacks-trigger debuff (CR 508.1f + CR 603.2 + CR 613)</b>: a
///   <see cref="TriggeredAbility"/> on <see cref="Triggers.OnAttackSelf"/>.
///   It targets a creature the <i>defending player</i> controls (the player
///   being attacked, read from
///   <see cref="CreatureAttacksEvent.DefendingPlayerOrPlaneswalker"/>). On
///   resolution the chosen creature:
///     * gets -2/-0 (CR 613 Layer 7c) via
///       <see cref="UntilControllerNextTurnPumpEffect"/>, and
///     * loses flying (CR 613 Layer 6) via
///       <see cref="UntilControllerNextTurnLoseKeywordEffect"/>,
///   both lasting "until your next turn" (CR 514 does NOT expire these — they
///   are gated by a shared <c>active</c> flag flipped off on the controller's
///   NEXT turn-start, the same "until your next turn" wiring as
///   <see cref="ReflectorMageFactory"/>: the trigger resolves on the
///   controller's CURRENT turn, so the first matching
///   <see cref="TurnStartedEvent"/> the handler sees is the controller's next
///   turn — CR 702 reads "your next turn" as the controller's).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only (the
///   <see cref="NamedCardFactory"/> dispatch target). Flying + the attacks
///   trigger are attached; without a <see cref="TriggerManager"/> the bus
///   won't fire the trigger.
/// - <see cref="Create(Player, TriggerManager?, IEventBus?)"/> — fully wired.
/// </summary>
[CardName("Fear of Falling")]
public static class FearOfFallingFactory
{
    public const string CardName = "Fear of Falling";
    public const string PrintedManaCost = "{3}{U}{U}";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Fear of Falling with no live runtime wiring (the dispatch
    /// target). Flying + the attacks trigger are attached to the card shape.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Fear of Falling with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the attacks trigger is
    /// registered.</param>
    /// <param name="eventBus">When supplied, the debuff's "until your next
    /// turn" expiry is scheduled on the controller's next
    /// <see cref="TurnStartedEvent"/>.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // CR 205.2a — Enchantment Creature: Creature + Enchantment card types.
        var card = PermanentBuilders.EnchantmentCreature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Nightmare });

        card.SetOwner(owner);
        card.SetController(owner);

        // Flying (CR 702.9) — combat block-restriction marker.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Attacks trigger — "Whenever this creature attacks, target creature
        // defending player controls gets -2/-0 and loses flying until your
        // next turn." (CR 508.1f / 603.2 / 613.)
        // ----------------------------------------------------------------
        TriggeredAbility? attackTrigger = null;

        // The defending player snapshotted from the attack event; the
        // target-candidate gatherer scopes to creatures THAT player controls
        // (CR 509.1a — "defending player" = the player being attacked).
        Player? defendingPlayer = null;

        var attackCondition = new EventTriggerCondition<CreatureAttacksEvent>((e, _) =>
        {
            if (!ReferenceEquals(e.Attacker, card)) return false;
            // CR 509.1a — capture the defending player for target scoping.
            defendingPlayer = e.DefendingPlayerOrPlaneswalker as Player;
            return true;
        });

        var debuffEffect = new Effect(
            $"{CardName}: target creature defending player controls gets -2/-0 and loses flying until your next turn",
            () =>
            {
                var chosen = attackTrigger?.ChosenTargets;
                if (chosen is not { Count: > 0 } || chosen[0].Count == 0) return;
                if (chosen[0][0] is not Creature target) return;

                // CR 608.2b — if the target is no longer on the battlefield at
                // resolution, the ability does nothing.
                if (target.Zone != ZoneType.Battlefield) return;

                // Shape-only guard: without a live ContinuousEffectsService the
                // continuous-effect registration is a no-op (matches Cower in
                // Fear / Disfigure). The flag/cleanup still wire up so the
                // until-your-next-turn semantics are exercised by tests.
                if (target.ActiveEffects == null) return;

                // CR 514 does NOT expire these (duration is "until your next
                // turn", not "until end of turn"). A shared boxed flag controls
                // both effects' IsActive(); the controller's next turn-start
                // flips it off (CR 702 — "your next turn" = the controller's).
                var active = new bool[] { true };

                var pump = new UntilControllerNextTurnPumpEffect(target, -2, 0, () => active[0]);
                var loseFlying = new UntilControllerNextTurnLoseKeywordEffect(
                    target, "Flying", () => active[0]);
                target.ActiveEffects.Register(pump);
                target.ActiveEffects.Register(loseFlying);

                // "Until your next turn" — schedule expiry on the controller's
                // NEXT turn-start (same wiring as Reflector Mage). The trigger
                // resolves during the controller's CURRENT turn, after that
                // turn's TurnStartedEvent has already fired, so the first event
                // this handler receives is the controller's next turn.
                if (eventBus != null)
                {
                    var benController = card.Controller ?? owner;
                    Action<TurnStartedEvent>? handler = null;
                    handler = ev =>
                    {
                        if (!ReferenceEquals(ev.Player, benController)) return;
                        active[0] = false;
                        // Drop the now-inactive effects from the registry.
                        target.ActiveEffects?.Prune();
                        if (handler != null) eventBus.Unsubscribe<TurnStartedEvent>(handler);
                    };
                    eventBus.Subscribe<TurnStartedEvent>(handler);
                }
            });

        attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: attackCondition,
            effects: new IEffect[] { debuffEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                // "target creature defending player controls" (CR 509.1a).
                new TargetRequest(
                    Description: "target creature defending player controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    // No "debuff/shrink" BotIntent exists; a -2/-0 +
                    // lose-flying on an opposing creature is a weakening /
                    // removal-style effect, so flag it Removal for the ranker.
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx =>
                    {
                        // Prefer the captured defending player; fall back to
                        // every opponent of the controller when the event
                        // wasn't observed (direct test/bot probe path).
                        var controller = card.Controller ?? owner;
                        var defenders = defendingPlayer != null
                            ? new[] { defendingPlayer }
                            : ctx.AllPlayers
                                .Where(p => !ReferenceEquals(p, controller))
                                .ToArray();
                        return defenders
                            .SelectMany(p => p.Zones.Battlefield.GetCards())
                            .OfType<Creature>()
                            .Cast<object>()
                            .ToList();
                    }),
            });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }
}
