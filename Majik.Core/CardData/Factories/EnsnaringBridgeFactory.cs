using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ensnaring Bridge (Stronghold, {3}).
///
/// Artifact. Oracle text:
///   "Creatures with power greater than the number of cards in your hand
///    can't attack."
///
/// ## Implementation
///
/// CR 508.1c — a creature can't attack if a restriction applies to it.
/// The bridge installs a single
/// <see cref="CombatRestrictionEffect"/> in predicate mode on the supplied
/// game-level <see cref="ContinuousEffectsService"/>:
///
/// - <b>Restriction</b>: <see cref="CombatRestriction.CannotAttack"/>.
/// - <b>Predicate</b>: <c>c =&gt; c.Power &gt; bridge.Controller.Zones.Hand.Count</c>.
///   Evaluated on every attack-validation pass
///   (<see cref="Majik.Core.Combat.CombatValidator.CanAttack"/> consults
///   <see cref="ContinuousEffectsService.HasRestriction"/>), so the
///   threshold tracks the controller's live hand size and the queried
///   creature's live power. Multiple Bridges register independent
///   restrictions — first to trip the predicate wins, all are
///   idempotent. The Bridge is colour-blind / controller-blind: per the
///   printed text, any creature with sufficient power can't attack,
///   including ones controlled by the Bridge's own player (this is the
///   classic Lantern Control / 8-Rack drawback).
/// - <b>IsActive gate</b>:
///   <c>() =&gt; bridge.Zone == ZoneType.Battlefield</c>. Off-battlefield
///   the restriction is suppressed (CR 603.6e — static abilities function
///   only while their source is on the battlefield) and dropped by the
///   service's prune sweep so a stale Bridge can't lock combat forever
///   after being destroyed.
///
/// The <see cref="StaticAbility"/> marker carries the printed description
/// for shape tests / UI surface; the working enforcement is the
/// predicate-mode <see cref="CombatRestrictionEffect"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Layer 7c power-modifying effects vs. timing</b>: the predicate
///   reads <see cref="Creature.Power"/>, which routes through the layer
///   system if the creature has an <see cref="Creature.ActiveEffects"/>
///   service wired. Edge cases where a pump effect arrives mid-attack
///   declaration use the value the layer system reports at query time
///   — same posture as every other live-stat read in
///   <see cref="Majik.Core.Combat.CombatValidator"/>.
/// - <b>Bot agent surface</b>: the heuristic bot's attack planner does
///   not yet read <see cref="Majik.Core.Combat.CombatRestriction.CannotAttack"/>
///   restrictions when proposing attackers; the engine will reject any
///   declared attacker the predicate catches. Same posture as Leyline
///   Binding's restriction on first ship.
/// </summary>
[CardName("Ensnaring Bridge")]
public static class EnsnaringBridgeFactory
{
    public const string CardName = "Ensnaring Bridge";
    public const string PrintedManaCost = "{3}";

    /// <summary>Printed static-ability description surfaced on the card.</summary>
    public const string StaticDescription =
        "Creatures with power greater than the number of cards in your hand can't attack.";

    /// <summary>
    /// Construct Ensnaring Bridge with no continuous-effects service. The
    /// static-ability marker is attached for card-text inspection but the
    /// working can't-attack predicate is NOT registered. Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Ensnaring Bridge. When <paramref name="effects"/> is
    /// supplied, a predicate-mode <see cref="CombatRestrictionEffect"/>
    /// is registered: any creature whose current power exceeds the
    /// Bridge controller's hand size can't attack while the Bridge is on
    /// the battlefield (CR 508.1c).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Game-level continuous-effects service. May
    /// be null — the can't-attack restriction is then skipped (the
    /// static-ability marker is still attached for inspection).</param>
    public static Artifact Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Static-ability marker — CR 113.3d / 603.6e. Functions while on
        // the battlefield. Description matches the printed text for shape
        // / UI surfacing. The working enforcement is the predicate-mode
        // CombatRestrictionEffect below.
        // ----------------------------------------------------------------
        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: StaticDescription,
            isActiveCheck: () => card.Zone == ZoneType.Battlefield));

        // ----------------------------------------------------------------
        // Working restriction. CR 508.1c. Predicate evaluated against the
        // queried creature at every attack-validation pass — both the
        // creature's power (current value via the layer system) and the
        // controller's hand size are live reads, so the threshold tracks
        // hand-emptying / hand-refilling and pump effects without any
        // re-registration.
        //
        // "your hand" — the Bridge controller's hand, not the queried
        // creature's controller's hand. CR 109.5 — "your" in a static
        // ability refers to the ability's controller.
        //
        // Gate: only active while Bridge is on the battlefield (CR 603.6e
        // analogue for static restrictions). Off-battlefield, IsActive
        // returns false and the service's prune sweep drops the effect.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            effects.Register(new CombatRestrictionEffect(
                restriction: CombatRestriction.CannotAttack,
                predicate: c =>
                {
                    var ctrl = card.Controller;
                    if (ctrl == null) return false;
                    return c.Power > ctrl.Zones.Hand.Count;
                },
                isActiveGate: () => card.Zone == ZoneType.Battlefield,
                expiresAtEndOfTurn: false));
        }

        return card;
    }
}
