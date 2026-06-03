using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tinybones, the Pickpocket (Modern Horizons 3, {B}).
///
/// Legendary Creature — Skeleton Rogue 1/1. Oracle text:
///   "Deathtouch
///    Whenever Tinybones deals combat damage to a player, you may cast
///    target nonland permanent card from that player's graveyard, and mana
///    of any type can be spent to cast that spell."
///
/// ## Implemented (v1)
/// - 1/1 Legendary Creature — Skeleton Rogue, mana cost {B}, Deathtouch.
/// - Combat-damage-to-a-player triggered ability (CR 510, CR 603.1) wired
///   over <see cref="CombatDamageDealtEvent"/> filtered to the source card
///   and a non-null <see cref="CombatDamageDealtEvent.TargetPlayer"/>. The
///   resolved effect stamps a runtime NON-OWNER graveyard-cast grant
///   (<see cref="Card.GrantRuntimeGraveyardNonOwnerCast"/>) on each nonland
///   permanent card in the damaged player's graveyard, permitting the
///   Tinybones controller — who does NOT own those cards — to cast one from
///   that player's graveyard (CR 601.3e). The matching alternative cost is
///   <see cref="Majik.Core.Costs.GraveyardNonOwnerCastAlternativeCost"/>, the
///   graveyard mirror of <see cref="Majik.Core.Costs.ExileCastAlternativeCost"/>
///   (Ragavan, Nimble Pilferer's non-owner exile cast).
/// - "Mana of any type can be spent to cast that spell": the grant cost is the
///   printed cost converted to an all-generic cost of equal mana value (generic
///   mana accepts mana of any type), and <c>anyTypeMana: true</c> records the
///   relaxation for any downstream payment surface.
/// - When an <see cref="IEventBus"/> is supplied, a one-shot
///   <see cref="StepStartedEvent"/> handler clears every grant on the first
///   Cleanup step (CR 514.2 — the cast window is "this turn" via the combat
///   trigger) and unsubscribes itself.
///
/// ## How the granted card is cast
/// Pass <see cref="Majik.Core.Costs.GraveyardNonOwnerCastAlternativeCost"/> built
/// from the card's <see cref="Card.RuntimeGraveyardNonOwnerCastCost"/> to
/// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>. The card stays in its
/// owner's graveyard until it goes to the stack; the
/// <see cref="Majik.Core.Game.SpellCastFlow"/> stamps
/// <c>Spell.WasCastFromGraveyard</c> from the Graveyard source zone (the
/// read-side sentinel shipped with Ash Zealot). The nonland permanent enters
/// the battlefield under the Tinybones controller (CR 110.2); when it later
/// dies it goes to its OWNER's graveyard (CR 400.3) — the engine's zones are
/// owner-keyed, so no extra plumbing is required.
///
/// ## Deferred (v1 gaps)
/// - <b>"target nonland permanent card"</b>: the grant is stamped on every
///   eligible card in the damaged player's graveyard; the actual single-target
///   choice + decision to cast belongs to the agent's priority loop (same
///   permission-layer-not-prompt boundary as Ragavan). A strict one-target
///   restriction is not enforced at the grant level.
/// </summary>
[CardName("Tinybones, the Pickpocket")]
public static class TinybonesThePickpocketFactory
{
    /// <summary>
    /// Construct Tinybones with no live event-bus / TriggerManager wiring. The
    /// combat-damage trigger is attached for shape but not registered; the
    /// runtime graveyard-cast grants remain until the test clears them manually
    /// (no EOT cleanup subscription). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Tinybones with optional runtime services. When
    /// <paramref name="eventBus"/> is supplied the runtime graveyard-cast
    /// grants are cleared on the next Cleanup step; when
    /// <paramref name="triggers"/> is supplied the combat trigger is registered
    /// so a <see cref="CombatDamageDealtEvent"/> automatically queues the
    /// ability.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Tinybones, the Pickpocket",
            manaCost: "{B}",
            power: 1,
            toughness: 1,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Skeleton, CardSubtype.Rogue });

        card.SetOwner(owner);
        card.SetController(owner);

        // Deathtouch (CR 702.2).
        card.AddAbility(new KeywordAbility("Deathtouch", card, owner));

        // ----------------------------------------------------------------
        // Combat-damage-to-a-player trigger — CR 510, CR 603.1.
        //   "Whenever Tinybones deals combat damage to a player, you may cast
        //    target nonland permanent card from that player's graveyard, and
        //    mana of any type can be spent to cast that spell."
        // The predicate captures the damaged player off the event so the
        // resolved effect reads the correct graveyard at fire time.
        // ----------------------------------------------------------------
        Player? capturedDamaged = null;

        var effect = new Effect(
            "Tinybones: grant non-owner graveyard cast of a nonland permanent card (CR 601.3e)",
            () =>
            {
                var victim = capturedDamaged;
                if (victim == null) return;

                // Stamp a non-owner graveyard-cast grant on every nonland
                // permanent card in the damaged player's graveyard. The
                // Tinybones controller (NOT the card's owner) becomes a legal
                // caster (CR 601.3e). "Mana of any type can be spent" → convert
                // the printed cost to an all-generic cost of equal mana value.
                foreach (var gyCard in victim.Zones.Graveyard.GetCards().ToList())
                {
                    if (gyCard is not Card concrete) continue;
                    if (concrete.HasType(CardType.Land)) continue;
                    if (!IsPermanentCard(concrete)) continue;

                    var anyTypeCost = AllGenericOf(concrete.ManaCostValue);
                    concrete.GrantRuntimeGraveyardNonOwnerCast(owner, anyTypeCost, anyTypeMana: true);
                }

                // EOT cleanup — CR 514.2. Schedule a one-shot handler that
                // clears the grants on the first Cleanup step and unsubscribes.
                // Skipped when no bus is wired (tests manage EOT manually).
                if (eventBus != null)
                {
                    var captured = victim;
                    Action<StepStartedEvent>? handler = null;
                    handler = (e) =>
                    {
                        if (e.StepType != PhaseStateType.Cleanup) return;
                        foreach (var gyCard in captured.Zones.Graveyard.GetCards().ToList())
                        {
                            if (gyCard is Card c) c.ClearRuntimeGraveyardNonOwnerCast();
                        }
                        if (handler != null) eventBus.Unsubscribe(handler);
                    };
                    eventBus.Subscribe(handler);
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (!ReferenceEquals(e.Source, card)) return false;
                if (e.TargetPlayer == null) return false;
                capturedDamaged = e.TargetPlayer;
                return true;
            }),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// CR 110.4 — a "permanent card" is a card with one of the permanent card
    /// types (artifact, creature, enchantment, land, planeswalker). Tinybones
    /// explicitly says "nonland", so the land check is applied at the call site;
    /// this probe covers the remaining permanent types.
    /// </summary>
    private static bool IsPermanentCard(Card card) =>
        card.HasType(CardType.Creature)
        || card.HasType(CardType.Artifact)
        || card.HasType(CardType.Enchantment)
        || card.HasType(CardType.Planeswalker);

    /// <summary>
    /// CR 601.3e — "mana of any type can be spent": an all-generic cost of the
    /// same mana value, since generic mana accepts mana of any type.
    /// </summary>
    private static ManaCost AllGenericOf(ManaCost printed) =>
        ManaCost.Parse("{" + printed.TotalValue + "}");
}
