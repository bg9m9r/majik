using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Zones;

namespace Majik.Core.Players;

/// <summary>
/// CR 701.54 — "The Ring tempts you" subsystem. Models the per-player
/// emblem named <b>The Ring</b> together with the tempt counter and the
/// Ring-bearer designation. Reusable infrastructure shared by every
/// Lord-of-the-Rings card that tempts (Boromir, Frodo, Call of the Ring,
/// the One Ring promo, etc.) — a card tempts its controller via
/// <see cref="Player.TheRingTemptsYou(Cards.Permanent)"/>, which delegates
/// here.
///
/// ## What the Ring tracks (CR 701.54)
/// - <b>Tempt count</b> (<see cref="TemptCount"/>) — how many times this
///   player has been tempted. Drives which staged abilities are live.
/// - <b>Ring-bearer designation</b> (<see cref="RingBearer"/>, CR 701.54b/e)
///   — a non-copiable designation on one permanent the player controls.
///   "is your Ring-bearer" is true iff that permanent is on the
///   battlefield under this player's control with the designation
///   (<see cref="IsRingBearer(Cards.Permanent)"/>).
///
/// ## Staged abilities (CR 701.54c)
/// The emblem always has the always-on block restriction; the three
/// triggered abilities turn on as the tempt count crosses 2 / 3 / 4. Each
/// triggered ability is registered with the <see cref="TriggerManager"/>
/// once at Ring creation and guards on <see cref="TemptCount"/> at fire
/// time (CR 701.54c thresholds are "tempted 2/3/4 or more times"), so the
/// abilities self-activate as the player is tempted again without
/// re-registration.
///
/// - <b>(always)</b> "Your Ring-bearer is legendary and can't be blocked by
///   creatures with greater power." Modelled as a
///   <see cref="CantBeBlockedExceptByEffect"/> on the current Ring-bearer
///   (predicate: blocker power ≤ Ring-bearer power, CR 509.1b). The
///   effect follows the designation — it is moved when the bearer changes
///   (<see cref="DesignateRingBearer"/>). The "is legendary" half is a
///   designation property surfaced via <see cref="RingBearerIsLegendary"/>
///   (the legend rule consults Ring-bearer designation, CR 704.5j); the
///   engine does not yet layer-mutate the supertype list, matching the
///   shape-only legendary posture used elsewhere.
/// - <b>(2+)</b> "Whenever your Ring-bearer attacks, draw a card, then
///   discard a card."
/// - <b>(3+)</b> "Whenever your Ring-bearer becomes blocked by a creature,
///   the blocking creature's controller sacrifices it at end of combat."
/// - <b>(4+)</b> "Whenever your Ring-bearer deals combat damage to a player,
///   each opponent loses 3 life."
/// </summary>
public sealed class RingState
{
    private readonly Player _owner;
    private readonly IEventBus? _eventBus;
    private readonly TriggerManager? _triggers;
    private readonly Func<IReadOnlyList<Player>>? _allPlayersResolver;

    private Permanent? _ringBearer;
    private CantBeBlockedExceptByEffect? _bearerBlockRestriction;

    /// <summary>Pending end-of-combat sacrifices queued by the 3+ ability —
    /// blockers of the Ring-bearer whose controllers must sacrifice them at
    /// end of combat (CR 701.54c). Keyed by combat instance so a fresh
    /// combat doesn't inherit a prior combat's queue.</summary>
    private readonly List<(Player Controller, Permanent Blocker)> _pendingEndOfCombatSacrifices = new();

    /// <summary>
    /// CR 701.54 — the Ring's owner is the tempted player; the emblem named
    /// The Ring lives in their command zone. The optional services let the
    /// staged triggered abilities drive themselves off the live event bus;
    /// when null the Ring is structural only (tempt counting + designation
    /// still work, abilities don't auto-fire).
    /// </summary>
    public RingState(
        Player owner,
        IEventBus? eventBus = null,
        TriggerManager? triggers = null,
        Func<IReadOnlyList<Player>>? allPlayersResolver = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _eventBus = eventBus;
        _triggers = triggers;
        _allPlayersResolver = allPlayersResolver;

        RegisterStagedTriggers();
    }

    /// <summary>CR 701.54 — how many times this player has been tempted.</summary>
    public int TemptCount { get; private set; }

    /// <summary>CR 701.54b — the permanent currently carrying the Ring-bearer
    /// designation, or null if the player has no Ring-bearer. Not a copiable
    /// value.</summary>
    public Permanent? RingBearer => _ringBearer;

    /// <summary>CR 701.54c — the always-on emblem clause makes the Ring-bearer
    /// legendary. Surfaced as a designation property (the engine does not
    /// layer-mutate the supertype list in v1).</summary>
    public bool RingBearerIsLegendary => _ringBearer != null;

    /// <summary>CR 701.54e — true iff <paramref name="permanent"/> is on the
    /// battlefield under this player's control AND currently carries the
    /// Ring-bearer designation.</summary>
    public bool IsRingBearer(Permanent permanent)
    {
        if (permanent == null) return false;
        if (!ReferenceEquals(_ringBearer, permanent)) return false;
        return permanent.Zone == ZoneType.Battlefield
               && ReferenceEquals(permanent.Controller, _owner);
    }

    /// <summary>
    /// CR 701.54a/d — perform one "the Ring tempts you" instruction.
    /// Increments the tempt count and (if a creature is offered) re-designates
    /// the Ring-bearer. Per CR 701.54a the player chooses a creature they
    /// control to become the Ring-bearer; <paramref name="chosenBearer"/> is
    /// that choice. Passing null leaves the existing Ring-bearer in place
    /// (legal when the player controls no creature — the tempt still counts,
    /// CR 701.54d).
    /// </summary>
    public void Tempt(Permanent? chosenBearer)
    {
        TemptCount++;
        if (chosenBearer != null)
        {
            DesignateRingBearer(chosenBearer);
        }
    }

    /// <summary>
    /// CR 701.54a/b — move the Ring-bearer designation onto
    /// <paramref name="bearer"/>. The designation is unique: assigning it to a
    /// new creature removes it from the previous one. The always-on
    /// block restriction (CR 509.1b) follows the designation.
    /// </summary>
    public void DesignateRingBearer(Permanent bearer)
    {
        ArgumentNullException.ThrowIfNull(bearer);

        // Move the "can't be blocked by greater power" restriction off the
        // old bearer and onto the new one.
        if (_ringBearer is Creature oldBearer && _bearerBlockRestriction != null)
        {
            oldBearer.ActiveEffects?.Unregister(_bearerBlockRestriction);
        }
        _bearerBlockRestriction = null;

        _ringBearer = bearer;

        if (bearer is Creature newBearer)
        {
            newBearer.ActiveEffects ??= new ContinuousEffectsService();
            // CR 701.54c — "can't be blocked by creatures with greater power".
            // A would-be blocker is legal iff its power ≤ the Ring-bearer's.
            _bearerBlockRestriction = new CantBeBlockedExceptByEffect(
                newBearer,
                blocker => blocker is Creature c && c.Power <= newBearer.Power);
            newBearer.ActiveEffects.Register(_bearerBlockRestriction);
        }
    }

    // ------------------------------------------------------------------
    // Staged triggered abilities (CR 701.54c). Registered once; each guards
    // on the live TemptCount threshold at fire time.
    // ------------------------------------------------------------------

    private void RegisterStagedTriggers()
    {
        if (_triggers == null) return;

        // 2+ — "Whenever your Ring-bearer attacks, draw a card, then discard
        //       a card." (CR 508.1f)
        Creature? attackingBearer = null;
        var attackTrigger = new TriggeredAbility(
            source: _owner,
            controller: _owner,
            condition: new EventTriggerCondition<CreatureAttacksEvent>((e, _) =>
            {
                if (TemptCount < 2) return false;
                if (!IsRingBearer(e.Attacker)) return false;
                attackingBearer = e.Attacker;
                return true;
            }),
            effects: new IEffect[]
            {
                new Effect("The Ring (2+): draw a card, then discard a card", () =>
                {
                    attackingBearer = null;
                    var drawn = Majik.Core.Primitives.Fx.DrawCards(_owner, 1);
                    if (drawn.Count == 0) return;
                    Majik.Core.Primitives.Fx.Discard(_owner, 1);
                }),
            },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Command });
        _triggers.RegisterTriggeredAbility(attackTrigger);

        // 3+ — "Whenever your Ring-bearer becomes blocked by a creature, the
        //       blocking creature's controller sacrifices it at end of
        //       combat." (CR 509.1g / CR 701.54c)
        var blockedTrigger = new TriggeredAbility(
            source: _owner,
            controller: _owner,
            condition: new EventTriggerCondition<BlockersDeclaredEvent>((e, _) =>
            {
                if (TemptCount < 3) return false;
                if (_ringBearer == null) return false;
                // Did the Ring-bearer become blocked? Find the attacker entry
                // whose creature is our Ring-bearer and that has ≥1 blocker.
                var attacker = e.Combat.Attackers
                    .FirstOrDefault(a => ReferenceEquals(a.Creature, _ringBearer));
                return attacker != null && attacker.Blockers.Count > 0;
            }),
            effects: new IEffect[]
            {
                new Effect("The Ring (3+): queue blocker sacrifice at end of combat", () =>
                {
                    // The condition already validated the Ring-bearer is
                    // blocked; enqueue each blocker for end-of-combat sacrifice.
                    if (_ringBearer == null) return;
                    var atk = _lastBlockedCombat?.Attackers
                        .FirstOrDefault(a => ReferenceEquals(a.Creature, _ringBearer));
                    if (atk == null) return;
                    foreach (var blocker in atk.Blockers)
                    {
                        var ctrl = blocker.Creature.Controller;
                        if (ctrl == null) continue;
                        _pendingEndOfCombatSacrifices.Add((ctrl, blocker.Creature));
                    }
                }),
            },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Command });
        _triggers.RegisterTriggeredAbility(blockedTrigger);

        // The 3+ trigger needs the Combat handle at resolution time; capture
        // it from the BlockersDeclaredEvent via a lightweight bus listener so
        // the effect closure can read the blockers without re-deriving them.
        _eventBus?.Subscribe<BlockersDeclaredEvent>(e => _lastBlockedCombat = e.Combat);

        // End-of-combat: each queued blocker's controller sacrifices it
        // (CR 701.54c / CR 701.16). Drains the queue.
        _eventBus?.Subscribe<CombatEndedEvent>(_ =>
        {
            foreach (var (controller, blocker) in _pendingEndOfCombatSacrifices.ToList())
            {
                if (blocker.Zone != ZoneType.Battlefield) continue;
                if (!ReferenceEquals(blocker.Controller, controller)) continue;
                var owner = blocker.Owner ?? controller;
                controller.Zones.Battlefield.RemoveCard(blocker);
                owner.Zones.Graveyard.AddCard(blocker);
            }
            _pendingEndOfCombatSacrifices.Clear();
            _lastBlockedCombat = null;
        });

        // 4+ — "Whenever your Ring-bearer deals combat damage to a player,
        //       each opponent loses 3 life." (CR 510 / CR 701.54c)
        var combatDamageTrigger = new TriggeredAbility(
            source: _owner,
            controller: _owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (TemptCount < 4) return false;
                if (_ringBearer == null) return false;
                if (!ReferenceEquals(e.Source, _ringBearer)) return false;
                // "to a player" — TargetPlayer is non-null for player damage.
                return e.TargetPlayer != null;
            }),
            effects: new IEffect[]
            {
                new Effect("The Ring (4+): each opponent loses 3 life", () =>
                {
                    var everyone = _allPlayersResolver?.Invoke()
                        ?? (IReadOnlyList<Player>)new[] { _owner };
                    foreach (var p in everyone)
                    {
                        if (ReferenceEquals(p, _owner)) continue; // opponents only
                        if (p.HasLost) continue;
                        p.LoseLife(3);
                    }
                }),
            },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Command });
        _triggers.RegisterTriggeredAbility(combatDamageTrigger);
    }

    private Majik.Core.Combat.Combat? _lastBlockedCombat;
}
