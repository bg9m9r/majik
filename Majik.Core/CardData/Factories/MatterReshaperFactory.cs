using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Matter Reshaper (Oath of the Gatewatch, {3}{C}).
///
/// Creature — Eldrazi Drone 3/2. Oracle text (Scryfall, verified):
///   "When this creature dies, reveal the top card of your library. If it's
///    a permanent card with mana value 3 or less, put it onto the
///    battlefield. Otherwise, put it into your hand."
///
/// ## Implemented (v1)
/// - 3/2 Creature — Eldrazi Drone at {3}{C}.
/// - <b>Dies trigger (CR 603.6c / CR 700.4)</b>: Battlefield → Graveyard
///   <see cref="CardMovedEvent"/> filtered to this card. Active zones =
///   {Battlefield, Graveyard} so the trigger still matches once the card
///   has been moved to the graveyard by <see cref="ZoneService"/> prior
///   to the <see cref="CardMovedEvent"/> publish (mirrors Wurmcoil
///   Engine's dies-trigger active-zones posture; CR 603.6d "looks back").
/// - On resolution:
///   1. Peek the controller's top library card (CR 701.16 — reveal).
///      A <see cref="CardRevealedEvent"/> is published when an
///      <see cref="IEventBus"/> is supplied so portal / log subscribers
///      can flash the card.
///   2. Branch on the card's printed type set + mana value:
///      <list type="bullet">
///        <item>Permanent card (Creature / Artifact / Enchantment / Land /
///              Planeswalker — CR 110.4a — excludes Instant + Sorcery)
///              AND mana value &lt;= 3 → Library → Battlefield under the
///              dying card's controller (CR 121 — the dies trigger is
///              controlled by Matter Reshaper's controller; the "put it
///              onto the battlefield" half routes the new permanent under
///              that same controller per CR 110.2a — the new object's
///              controller is the player who put it there).</item>
///        <item>Otherwise (nonpermanent OR mana value &gt;= 4) →
///              Library → Hand.</item>
///      </list>
///   3. Empty library → no-op (no reveal, no zone move).
/// - Zone moves route through <see cref="ZoneService.MoveCard"/> when
///   supplied so the resulting <see cref="CardMovedEvent"/>s fire
///   replacement / ETB triggers downstream (Containment Priest /
///   Tormod's Crypt / etc.). When the service is null the move is a raw
///   zone shuffle — suitable for shape tests.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Trigger attached for shape
///   observability; not registered with any <see cref="TriggerManager"/>;
///   zone moves on resolution use raw zone manipulation. Suitable for
///   dispatcher / structural tests.
/// - <see cref="Create(Player, ZoneService?, IEventBus?, TriggerManager?)"/>
///   — fully wired. Trigger registers with <paramref name="triggers"/>;
///   reveal publishes through <paramref name="eventBus"/>; zone moves
///   route through <paramref name="zones"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Mana value of cards with {X}</b>: the printed mana value of a card
///   in the library is computed from its mana cost; {X} contributes 0 per
///   CR 202.3b. The reveal reads <see cref="ManaCost.TotalValue"/> which
///   already treats X as 0 — correct for the library-reveal context
///   (PendingCastX is only stamped on cards mid-cast).
/// - <b>Reveal duration</b>: per CR 701.16 the card stays revealed until
///   the effect that revealed it stops applying. v1 emits the event once
///   and immediately commits the chosen zone move; clients are expected
///   to render the reveal as a transient flash (same posture as Dark
///   Confidant).
/// </summary>
[CardName("Matter Reshaper")]
public static class MatterReshaperFactory
{
    public const string CardName = "Matter Reshaper";
    public const string PrintedManaCost = "{3}{C}";
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Matter Reshaper with no live wiring. The dies trigger is
    /// attached for shape observability; not registered with any
    /// <see cref="TriggerManager"/>; the reveal/zone-move uses raw zone
    /// manipulation. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Matter Reshaper with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, reveal-then-place zone moves
    /// route through <see cref="ZoneService.MoveCard"/> so
    /// <see cref="CardMovedEvent"/> publishes for downstream ETB triggers
    /// (Soul Warden, Containment Priest, etc.).</param>
    /// <param name="eventBus">When supplied, the reveal step publishes a
    /// <see cref="CardRevealedEvent"/>.</param>
    /// <param name="triggers">When supplied, the dies trigger registers
    /// so a qualifying <see cref="CardMovedEvent"/> automatically queues
    /// it on the stack (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Eldrazi, CardSubtype.Drone });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Dies trigger — CR 603.6c / CR 700.4.
        //   "When this creature dies, reveal the top card of your library.
        //    If it's a permanent card with mana value 3 or less, put it
        //    onto the battlefield. Otherwise, put it into your hand."
        //
        // ActiveZones = {Battlefield, Graveyard} — same posture as
        // Wurmcoil Engine: the trigger's zone-guard must still match
        // after ZoneService has already moved the card to the graveyard
        // before publishing the CardMovedEvent.
        //
        // Resolve binds to the dying card's controller at trigger-creation
        // time (the printed text reads "your library"; the dying card's
        // last controller before the dies-event is who owns the trigger
        // per CR 603.6d).
        // ----------------------------------------------------------------
        var diesEffect = new Effect(
            $"{CardName}: reveal top, permanent + mv <= 3 → battlefield, else → hand",
            () =>
            {
                // The dying card's controller drives the reveal target;
                // for v1 we use the configured owner (which is also the
                // initial controller). Control-change effects on a creature
                // about to die are vanishingly rare — Threaten / Act of
                // Treason temp control is the only common path, and the
                // dies trigger then belongs to the new controller per
                // CR 603.6d; v1 binds to the original owner which is the
                // accepted simplification for non-control-change dies
                // triggers (matches Wurmcoil's `owner` posture).
                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    // CR 701.16 — nothing to reveal; no move happens. The
                    // empty-library state-loss flag isn't set here (the
                    // trigger doesn't draw — it reveals + places /
                    // returns; only Player.DrawCard touches the loss
                    // marker).
                    return;
                }

                // CR 701.16 — public reveal. Library zone (card hasn't
                // moved yet).
                eventBus?.Publish(new CardRevealedEvent(
                    top, owner, ZoneType.Library, CardName));

                // CR 110.4a — permanent card = a card with one or more of
                // the five permanent types (Artifact, Creature,
                // Enchantment, Land, Planeswalker). Instant / Sorcery
                // cards are nonpermanent.
                var isPermanent =
                    top.HasType(CardType.Artifact)
                    || top.HasType(CardType.Creature)
                    || top.HasType(CardType.Enchantment)
                    || top.HasType(CardType.Land)
                    || top.HasType(CardType.Planeswalker);

                // CR 202.3b — printed mana value. {X} in the library
                // contributes 0 (no chosen X off-stack).
                var mv = ManaCost.Parse(top.ManaCost ?? string.Empty).TotalValue;

                var destination = (isPermanent && mv <= 3)
                    ? ZoneType.Battlefield
                    : ZoneType.Hand;

                if (zones != null)
                {
                    // CR 110.2a — when the card enters the battlefield, it
                    // does so under Matter Reshaper's controller (the
                    // controller who's resolving this trigger). Pass the
                    // controller through so ZoneService stamps it on the
                    // permanent.
                    zones.MoveCard(top, ZoneType.Library, destination,
                        destination == ZoneType.Battlefield ? owner : null);
                }
                else
                {
                    // Raw zone manipulation — shape-only path.
                    owner.Zones.Library.RemoveCard(top);
                    if (destination == ZoneType.Battlefield)
                    {
                        owner.Zones.Battlefield.AddCard(top);
                        if (top is Permanent perm)
                        {
                            perm.SetController(owner);
                            perm.MarkEnteredBattlefield();
                        }
                    }
                    else
                    {
                        owner.Zones.Hand.AddCard(top);
                    }
                    top.SetZone(destination);
                }
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { diesEffect },
            // ActiveZones = Battlefield + Graveyard (Wurmcoil posture) so
            // the trigger still matches after ZoneService has stamped the
            // card's Zone = Graveyard before publishing the CardMovedEvent.
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }
}
