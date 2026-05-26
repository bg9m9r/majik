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
/// Named-card factory for Goblin Guide (Zendikar, {R}).
///
/// Creature — Goblin Scout 2/2. Oracle text:
///   "Haste.
///    Whenever Goblin Guide attacks, defending player reveals the top card
///    of their library. If it's a land card, that player puts it into
///    their hand."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Goblin Scout, mana cost {R}, owner/controller wired.
/// - <b>Haste (CR 702.10)</b>: <see cref="KeywordAbility"/> marker —
///   <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/> reads it.
///   Same wiring shape as <see cref="GoblinChieftainFactory"/>.
/// - <b>Attack trigger (CR 508.1f / CR 603.6c)</b>: triggered ability
///   over <see cref="CreatureAttacksEvent"/> filtered via
///   <see cref="Triggers.OnAttackSelf"/>. On resolution the captured
///   <see cref="CreatureAttacksEvent.DefendingPlayerOrPlaneswalker"/>
///   (cast to <see cref="Player"/> — only attacks against players reveal;
///   attacks against planeswalkers no-op since planeswalkers don't have
///   libraries) reveals the top card of their library via a
///   <see cref="CardRevealedEvent"/> published on the supplied
///   <see cref="IEventBus"/>. CR 701.16 — reveal makes the card public
///   without changing its zone. If the revealed card has
///   <see cref="CardType.Land"/>, the same card is moved
///   Library → Hand via <see cref="ZoneService.MoveCard"/> (so
///   <see cref="CardMovedEvent"/> publishes for hand-zone-change
///   subscribers); otherwise it stays on top of the library — the reveal
///   is sticky for the duration of the effect but the card object
///   doesn't move.
/// - Empty-library halts the reveal cleanly — no SBA trigger here
///   (CR 704.5b loss only fires on a draw attempt, not on a reveal).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Both Haste keyword and
///   attack trigger attached; trigger not registered with any
///   <see cref="TriggerManager"/>; reveal/move uses raw zone manipulation
///   (no <see cref="CardRevealedEvent"/> publish). Suitable for
///   dispatcher / structural tests.
/// - <see cref="Create(Player, ZoneService?, IEventBus?, TriggerManager?)"/>
///   — fully wired. The attack trigger registers with
///   <paramref name="triggers"/>; reveals publish on
///   <paramref name="eventBus"/>; land moves route through
///   <paramref name="zoneService"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal sticky-public window</b>: <see cref="CardRevealedEvent"/>
///   fires once at trigger resolution. The "reveal" remains in effect
///   until the printed effect stops applying (CR 701.16); there's no
///   live tracker for the public window — clients infer it from the
///   event timestamp. Same posture as every other reveal-from-library
///   factory (Dark Confidant, Ancient Stirrings, etc.).
/// - <b>Planeswalker defender</b>: the trigger fires but resolves to a
///   no-op when the defender is a Planeswalker, because the reveal-
///   library clause is keyed on "that player" (CR 701.16). The trigger
///   stays consistent with Ulamog's parallel "defending player exiles"
///   behaviour against PW defenders.
/// </summary>
[CardName("Goblin Guide")]
public static class GoblinGuideFactory
{
    public const string CardName = "Goblin Guide";
    public const string PrintedManaCost = "{R}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Goblin Guide with no live runtime wiring. Haste marker
    /// is wired; the attack trigger is attached to the card shape but
    /// not registered with any <see cref="TriggerManager"/>; reveal /
    /// library → hand moves use raw zone manipulation and no
    /// <see cref="CardRevealedEvent"/> fires. Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Goblin Guide with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">When supplied, the land → hand move
    /// routes through <see cref="ZoneService.MoveCard"/> so
    /// <see cref="CardMovedEvent"/> publishes for zone-change
    /// subscribers.</param>
    /// <param name="eventBus">When supplied, the top-of-library reveal
    /// publishes a <see cref="CardRevealedEvent"/> with reason
    /// <c>"goblin-guide"</c>.</param>
    /// <param name="triggers">When supplied, the attack trigger registers
    /// with the bus so a <see cref="CreatureAttacksEvent"/> matching
    /// Goblin Guide as the attacker automatically queues the ability
    /// on the stack (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
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

        // CR 702.10 — printed Haste. Marker only; CombatAbilities.HasHaste
        // reads the KeywordAbility.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // Attack trigger — CR 508.1f / CR 603.6c.
        //   "Whenever Goblin Guide attacks, defending player reveals the
        //    top card of their library. If it's a land card, that player
        //    puts it into their hand."
        // Defender is captured off the live CreatureAttacksEvent (same
        // closure pattern as UlamogTheCeaselessHungerFactory + Ragavan,
        // Nimble Pilferer).
        // ----------------------------------------------------------------
        Player? capturedDefender = null;

        var attackEffect = new Effect(
            $"{CardName}: defending player reveals top of library; land → hand",
            () =>
            {
                var victim = capturedDefender;
                if (victim == null) return; // PW defender — no-op (no library).

                var top = victim.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return; // CR 704.5b — empty library is a
                                         // state-based loss the next SBA
                                         // pass handles (no draw attempt).

                // CR 701.16 — reveal makes the card public without changing
                // its zone. Publish a CardRevealedEvent so portal /
                // observers can flash the top card.
                eventBus?.Publish(new CardRevealedEvent(
                    top, victim, ZoneType.Library, "goblin-guide"));

                if (!top.HasType(CardType.Land)) return; // Non-land — stays
                                                         // on top of library.

                // Land — that player puts it into their hand.
                if (zoneService != null)
                {
                    zoneService.MoveCard(top, ZoneType.Library, ZoneType.Hand);
                }
                else
                {
                    victim.Zones.Library.RemoveCard(top);
                    victim.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CreatureAttacksEvent>(
                (e, _) =>
                {
                    if (!ReferenceEquals(e.Attacker, card)) return false;
                    // CR 506.2 — capture the defender for the resolved
                    // effect. Only Player triggers the reveal; PW defender
                    // resolves as a no-op (planeswalkers don't have
                    // libraries to reveal from).
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
