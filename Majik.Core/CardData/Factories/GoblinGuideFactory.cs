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
/// Named-card factory for Goblin Guide (Zendikar / Modern Horizons 3).
///
/// Creature — Goblin Scout {R}, 2/2. Oracle text:
///   "Haste. Whenever Goblin Guide attacks, defending player reveals the
///    top card of their library. If it's a land card, that player puts it
///    into their hand."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Goblin Scout, mana cost {R}, owner/controller wired.
/// - <b>Haste</b> (CR 702.10) — wired as a <see cref="KeywordAbility"/> marker;
///   <see cref="CombatAbilities.HasHaste"/> reads this when evaluating
///   summoning sickness.
/// - <b>Attack triggered ability (CR 508.1f)</b> — fires on
///   <see cref="CreatureAttacksEvent"/> matching this card via
///   <see cref="Triggers.OnAttackSelf"/>. The defending player is
///   captured from <see cref="CreatureAttacksEvent.DefendingPlayerOrPlaneswalker"/>
///   at trigger evaluation time (same closure pattern as
///   <see cref="RagavanNimblePilfererFactory"/>). On resolution:
///     1. Peek at the defending player's top library card via
///        <c>player.Zones.Library.GetCards().FirstOrDefault()</c>.
///     2. If the library is empty, no-op.
///     3. Emit a <see cref="CardRevealedEvent"/> for the top card (when
///        <paramref name="eventBus"/> is non-null) — CR 701.16 reveal.
///     4. If the revealed card is a land (CR 305 — <c>HasType(CardType.Land)</c>),
///        move it from Library → Hand. Zone move routes through
///        <see cref="ZoneService.MoveCard"/> when supplied so ETB
///        triggers and zone-change events fire (CR 603.6a); falls back to
///        raw zone manipulation for the shape-only path.
///     5. If the card is not a land, it stays on top of the library
///        (no zone move needed — CR 701.16 reveals don't rearrange the
///        library unless the card is moved by a separate instruction).
///
/// ## "Defending player" handling
/// <see cref="CreatureAttacksEvent.DefendingPlayerOrPlaneswalker"/> is
/// typed as <c>object</c> to accommodate planeswalker-attacked states.
/// v1 casts it to <see cref="Player"/>; if the cast fails (e.g. defending
/// a planeswalker), the effect is a no-op. Planeswalker-defend semantics
/// (the attacking player's opponent still reveals) is deferred.
///
/// ## Single-arg overload
/// The parameterless <see cref="Create(Player)"/> overload attaches the
/// trigger to the card shape but does not register it with a
/// <see cref="TriggerManager"/>. Suitable for card-shape / dispatcher tests.
/// The <c>(owner, eventBus, triggers)</c> overload registers the trigger so
/// a <see cref="CreatureAttacksEvent"/> automatically queues the ability.
///
/// ## Deferred (v1 gaps)
/// - <b>CardRevealedEvent for non-land</b>: the reveal event is emitted for
///   both land and non-land branches, but only the land branch moves the card.
///   Some UI clients may want the reveal to persist for a moment before the
///   card stays on top — CR 701.16 is satisfied by the event emission.
/// - <b>Planeswalker defending</b>: if Goblin Guide attacks into a
///   planeswalker, the event carries the planeswalker object. The v1 cast to
///   <see cref="Player"/> will be null, and the effect no-ops. The correct
///   behaviour is to still use the defending player's library (CR 508.1f
///   reads "defending player", which is always a player, not a planeswalker —
///   the planeswalker is just the attack target, CR 506.4).
/// </summary>
public static class GoblinGuideFactory
{
    public const string CardName = "Goblin Guide";
    public const string PrintedManaCost = "{R}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Goblin Guide with card shape only. The Haste keyword marker
    /// and the attack trigger ability are attached to the card, but the
    /// trigger is not registered with a <see cref="TriggerManager"/>.
    /// Suitable for dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, eventBus: null, triggers: null, zoneService: null);

    /// <summary>
    /// Construct a fully-wired Goblin Guide.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Optional event bus. When supplied,
    /// <see cref="CardRevealedEvent"/> is published at trigger resolution
    /// (CR 701.16). May be null — reveal is silent for the shape-only
    /// path.</param>
    /// <param name="triggers">Optional <see cref="TriggerManager"/>. When
    /// supplied the attack trigger is registered so a
    /// <see cref="CreatureAttacksEvent"/> automatically queues the
    /// ability. May be null — trigger still appears on
    /// <see cref="ICard.Abilities"/>.</param>
    /// <param name="zoneService">Optional <see cref="ZoneService"/>. When
    /// supplied and the top card is a land, the Library → Hand move routes
    /// through <see cref="ZoneService.MoveCard"/> so zone-change events
    /// fire (CR 603.6a). May be null — raw zone manipulation is used
    /// instead.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Scout });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.10 — Haste. KeywordAbility marker; CombatAbilities.HasHaste
        // reads this to evaluate summoning sickness (CR 302.6).
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // CR 508.1f — attack trigger.
        //   "Whenever Goblin Guide attacks, defending player reveals the
        //    top card of their library. If it's a land card, that player
        //    puts it into their hand."
        //
        // The defending player is captured in a closure shared with the
        // effect — same pattern as RagavanNimblePilfererFactory. The
        // condition predicate runs at trigger evaluation time (before the
        // ability hits the stack, CR 603.3) so the captured reference is
        // current when the effect resolves.
        // ----------------------------------------------------------------
        Player? capturedDefender = null;

        var attackEffect = new Effect(
            $"{CardName}: defending player reveals top of library; if land, put it in hand",
            () =>
            {
                var defender = capturedDefender;
                if (defender == null) return;

                // CR 701.16 — peek at the top card of the defending
                // player's library. FirstOrDefault matches all other
                // factory usages — index 0 is the top card of the
                // library zone list.
                var top = defender.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return; // empty library — no-op.

                // Emit reveal event so UI subscribers can show the card.
                // CR 701.16 — "reveal" is visible to all players.
                eventBus?.Publish(new CardRevealedEvent(
                    top, defender, ZoneType.Library, CardName));

                // CR 305 — check if the top card has the Land card type.
                if (!top.HasType(CardType.Land)) return;

                // It's a land: the defending player puts it into their hand.
                if (zoneService != null)
                {
                    zoneService.MoveCard(top, ZoneType.Library, ZoneType.Hand, defender);
                }
                else
                {
                    defender.Zones.Library.RemoveCard(top);
                    defender.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CreatureAttacksEvent>((e, _) =>
            {
                if (!ReferenceEquals(e.Attacker, card)) return false;
                // Capture the defending player. Cast to Player — v1 no-op
                // if defending a planeswalker (see deferred notes above).
                capturedDefender = e.DefendingPlayerOrPlaneswalker as Player;
                return true;
            }),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }
}
