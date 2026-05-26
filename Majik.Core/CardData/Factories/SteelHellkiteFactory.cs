using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Steel Hellkite (Scars of Mirrodin, {6}).
///
/// Artifact Creature — Dragon 5/5. Oracle text:
///   "Flying.
///    {2}: Steel Hellkite gets +1/+0 until end of turn.
///    {X}: Destroy each nontoken permanent with mana value X whose
///    controller was dealt combat damage by Steel Hellkite this turn.
///    Activate only during your turn."
///
/// ## Implemented (v1)
/// - 5/5 Artifact Creature — Dragon at {6} (multi-type via
///   <see cref="Card.AddCardType"/>, mirroring Esika's Chariot / Walking
///   Ballista).
/// - <b>Flying</b> (CR 702.9) wired as a <see cref="KeywordAbility"/>
///   marker; combat code reads it directly.
/// - <b>{2}: +1/+0 EOT</b>: vanilla <see cref="ActivatedAbility"/> with a
///   <see cref="ManaCostCost"/> of <c>{2}</c>; effect registers a
///   <see cref="PumpUntilEndOfTurnEffect"/> on Steel Hellkite for +1/+0
///   via <see cref="Creature.ActiveEffects"/>. No sorcery-speed gate
///   (printed as instant-speed pump).
/// - <b>{X}: Destroy each nontoken permanent with mv = X whose
///   controller was dealt combat damage by Steel Hellkite this turn.
///   Activate only during your turn.</b>
///   <list type="bullet">
///     <item>Mana cost <c>{X}</c> via <see cref="ManaCostCost"/>; X-value
///     resolution mirrors <see cref="BlastZoneFactory"/>'s
///     <c>chargeXValueProvider</c> closure (engine has no per-activation
///     X ledger). Default = 0 in the shape-only path.</item>
///     <item><b>Combat-damage-victim tracking</b>: an event-bus
///     subscriber on <see cref="CombatDamageDealtEvent"/> accumulates the
///     <see cref="Player"/> controllers of every entity Steel Hellkite
///     dealt combat damage to this turn. Damage to a creature /
///     planeswalker contributes its controller; damage to a player
///     contributes that player. The set is reset on
///     <see cref="TurnStartedEvent"/> (CR 700.5 — "this turn" memory
///     ends at the cleanup step of the same turn; resetting on the next
///     turn-start is observationally identical for the "during your
///     turn" activation gate and discards stale state across turn
///     boundaries).</item>
///     <item><b>Sorcery-speed-like gate</b> ("Activate only during your
///     turn") via <see cref="ActivatedAbility"/>'s
///     <c>sorcerySpeed</c> flag — true here, so
///     <see cref="Rules.ActionValidator"/> rejects activations on
///     opponents' turns (CR 117.1a / 307.5). v1 caveat: this flag also
///     gates the activation to main-phase + empty-stack; the printed
///     "during your turn" rider is broader (any step of your turn). The
///     stricter v1 gate is observationally safer (no false-positive
///     activations) and matches the same posture Steel Hellkite-style
///     "during your turn" cards take in this repo until a dedicated
///     "any-phase your-turn-only" rider lands.</item>
///     <item>Sweep iterates every battlefield supplied by the
///     <paramref name="allPlayersResolver"/> (falls back to controller-
///     only when null) and destroys every <b>nontoken</b> permanent
///     (CR 111.1 — token detection via <see cref="Permanent.IsToken"/>)
///     whose <see cref="Card.ManaCostValue"/>'s total equals X AND
///     whose controller is in the tracked victim set.</item>
///   </list>
///
/// ## Source-closure injection
/// - <c>xValueProvider</c>: sampled at activation resolution to determine
///   the destruction mv target. Same shape as
///   <see cref="EngineeredExplosivesFactory"/> / <see cref="BlastZoneFactory"/>.
/// - <c>allPlayersResolver</c>: drives the sweep scope. Same shape as
///   Engineered Explosives / Pernicious Deed / Blast Zone.
/// - <c>eventBus</c>: when supplied, the combat-damage-victim tracker
///   subscribes to <see cref="CombatDamageDealtEvent"/> (accumulate) and
///   <see cref="TurnStartedEvent"/> (clear).
///
/// ## Deferred (v1 gaps)
/// - <b>Sorcery-speed vs "your-turn-only"</b>: see ability-2 comments.
///   The stricter gate is acceptable until the engine grows a separate
///   <c>YourTurnOnly</c> activation flag.
/// - <b>Pre-bus combat damage</b>: if the event bus is wired AFTER Steel
///   Hellkite has already dealt combat damage this turn, that prior
///   damage is not tracked. Production callers wire the bus at ETB time
///   (mirroring the <see cref="UmezawasJitteFactory"/> /
///   <see cref="BorosReckonerFactory"/> "subscribe-at-construction"
///   posture).
/// - <b>Multi-controller damage in one packet</b>: combat damage is
///   dealt per-(source, target) pair (CR 510.1c), so a single packet
///   only ever credits one controller. The tracking set is correct by
///   construction; no merge logic needed.
/// - <b>Lifetime of subscriptions</b>: when Steel Hellkite leaves the
///   battlefield (or zone-changes), the subscriptions remain attached
///   to the event bus. The handlers gate on the activation availability
///   check (off-battlefield → no activations fire), so stale handlers
///   are harmless but accumulate. Same posture as the per-instance
///   subscriptions in <see cref="OmnathLocusOfCreationFactory"/>.
/// </summary>
[CardName("Steel Hellkite")]
public static class SteelHellkiteFactory
{
    public const string CardName = "Steel Hellkite";
    public const string PrintedManaCost = "{6}";
    public const int Power = 5;
    public const int Toughness = 5;

    /// <summary>
    /// Construct Steel Hellkite with no live runtime wiring. The pump
    /// activation is wired structurally; the destruction activation
    /// resolves with X = 0 and an empty victim set (so it destroys
    /// nothing). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner,
            xValueProvider: null,
            allPlayersResolver: null,
            eventBus: null);

    /// <summary>
    /// Construct Steel Hellkite. When <paramref name="xValueProvider"/>
    /// is supplied, the {X} activation samples it at resolution. When
    /// <paramref name="allPlayersResolver"/> is supplied, the sweep
    /// scans every player's battlefield; otherwise only the controller's.
    /// When <paramref name="eventBus"/> is supplied, the combat-damage-
    /// victim tracker subscribes to <see cref="CombatDamageDealtEvent"/>
    /// (accumulate) and <see cref="TurnStartedEvent"/> (clear).
    /// </summary>
    public static Creature Create(
        Player owner,
        Func<int>? xValueProvider,
        Func<IReadOnlyList<Player>>? allPlayersResolver,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Dragon });

        // CR 301.1 / 302.1 — Artifact Creature: additively flag Artifact
        // so HasType lookups + colour identity see both types.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.9 Flying — keyword marker.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // {2}: Steel Hellkite gets +1/+0 until end of turn.
        // CR 602.1 — plain activated ability, instant speed.
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: +1/+0 EOT for {{2}}",
            () =>
            {
                if (card.ActiveEffects == null) return;
                card.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(card, 1, 0));
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{2}") },
            effects: new IEffect[] { pumpEffect }));

        // ----------------------------------------------------------------
        // Combat-damage-victim tracker. Accumulates the controllers of
        // every entity Steel Hellkite dealt combat damage to THIS turn.
        // Reset on TurnStartedEvent (CR 700.5 — "this turn" memory).
        // ----------------------------------------------------------------
        var combatVictims = new HashSet<Player>();

        if (eventBus != null)
        {
            eventBus.Subscribe<CombatDamageDealtEvent>(e =>
            {
                if (!ReferenceEquals(e.Source, card)) return;
                if (e.Amount <= 0) return;

                // Damage to a creature / planeswalker → its controller.
                // CombatDamageDealtEvent.Target is ICard? (null when the
                // target is a player — see the dual-ctor on the event).
                if (e.Target is ICard targetCard)
                {
                    var c = targetCard.Controller;
                    if (c != null) combatVictims.Add(c);
                    return;
                }

                // Damage to a player → read TargetPlayer off the base
                // DamageDealtEvent (set by the Player-target ctor).
                if (e.TargetPlayer is { } victimPlayer)
                {
                    combatVictims.Add(victimPlayer);
                }
            });

            eventBus.Subscribe<TurnStartedEvent>(_ => combatVictims.Clear());
        }

        // ----------------------------------------------------------------
        // {X}: Destroy each nontoken permanent with mv = X whose
        // controller was dealt combat damage by Steel Hellkite this turn.
        // CR 602 + CR 117.1a / 307.5 — sorcery-speed-equivalent gate via
        // ActivatedAbility.sorcerySpeed (see class xmldoc for the v1
        // caveat on "during your turn").
        // ----------------------------------------------------------------
        var sweepEffect = new Effect(
            $"{CardName}: destroy each nontoken permanent with mv = X whose controller took combat damage from this card this turn",
            () =>
            {
                var x = xValueProvider?.Invoke() ?? 0;
                if (combatVictims.Count == 0) return;

                var players = allPlayersResolver?.Invoke()
                    ?? (IReadOnlyList<Player>)new[] { owner };

                foreach (var p in players)
                {
                    if (p == null) continue;

                    // Snapshot — we mutate the battlefield list inside
                    // the loop. Mirror Engineered Explosives / Blast Zone
                    // pattern.
                    var victims = p.Zones.Battlefield.GetCards()
                        .OfType<Permanent>()
                        .Where(c => !c.IsToken)
                        .Where(c => c.Controller != null && combatVictims.Contains(c.Controller))
                        .Where(c => c.ManaCostValue.TotalValue == x)
                        .ToList();

                    foreach (var v in victims)
                    {
                        // CR 701.7b — destroyed permanents go to their
                        // owner's graveyard. Fall back to the iterated
                        // player when Owner is null so shape-only tests
                        // still surface the destruction.
                        var victimOwner = v.Owner ?? p;
                        p.Zones.Battlefield.RemoveCard(v);
                        victimOwner.Zones.Graveyard.AddCard(v);
                        v.SetZone(ZoneType.Graveyard);
                    }
                }
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{X}") },
            effects: new IEffect[] { sweepEffect },
            sorcerySpeed: true));

        return card;
    }
}
