using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Signal Pest (Mirrodin Besieged, {1}).
///
/// Artifact Creature — Pest 0/1. Oracle text:
///   "Signal Pest can't be blocked except by creatures with flying or reach.
///    Signal Pest gets +1/+0 for each other attacking creature."
///
/// Aggro / Affinity / Hardened Scales / Hammer-Time shell — 1-mana evasive
/// body that scales with wide boards; the Mirrodin Besieged companion piece
/// to Memnite + Ornithopter on the 0/1-mana artifact-creature curve.
///
/// ## Implementation (v1)
///
/// - 0/1 <see cref="Creature"/> with <see cref="CardSubtype.Pest"/>.
/// - <see cref="CardType.Artifact"/> additively stamped via
///   <see cref="Card.AddCardType"/> so HasType lookups + colour identity
///   see both Artifact + Creature (mirrors <see cref="OrnithopterFactory"/>
///   / <see cref="MemniteFactory"/>).
/// - Mana cost is the literal {1} string (one generic, no coloured).
/// - <b>"Signal Pest gets +1/+0 for each other attacking creature."</b> —
///   the printed text is a STATIC continuous effect that re-reads the
///   attacker count layer-by-layer (Layer 7c). The engine doesn't yet have
///   a dynamic count-based static-pump primitive, so v1 collapses this to
///   a <see cref="Triggers.OnAttackSelf"/> attack trigger that snapshots the
///   live attacker list once and registers a <see cref="PumpUntilEndOfTurnEffect"/>
///   for +N/+0 EOT where N = count(other attackers). Self-exclusion mirrors
///   Goblin Piledriver's "+2/+0 per other attacking Goblin" rider
///   (<see cref="GoblinPiledriverFactory"/>); the difference is that Signal
///   Pest counts EVERY other attacker (no subtype filter) and the per-unit
///   bonus is +1 power not +2.
///
/// ## Deferred (v1 gaps)
///
/// - <b>"Can't be blocked except by creatures with flying or reach."</b>
///   <see cref="Majik.Core.Combat.BlockLegality.CanBlock"/> currently only
///   handles the attacker's Flying restriction; there is no general
///   "can't be blocked except by &lt;predicate&gt;" primitive (Menace is
///   a count-only restriction handled in <c>BlockLegality.MenaceSatisfied</c>,
///   not a predicate-on-blocker restriction). v1 ships Signal Pest as a
///   vanilla shell + the attack-trigger boost only — opponents may block
///   it with any creature. Once a <c>CantBeBlockedExceptBy</c> primitive
///   lands, attach it here and remove the deferred-keyword log.
/// - <b>Static-vs-trigger semantics on the boost.</b> A real MTG static
///   continuously re-reads the attacker count, so if a second attacker is
///   declared after Signal Pest's "attack trigger" resolves, the static
///   would update Signal Pest's power immediately. The trigger-based
///   approximation here is a snapshot at trigger-resolution time
///   (observationally equivalent in the common single-declare-attackers
///   path — both attackers are declared simultaneously in CR 508.1, so the
///   snapshot includes them — but diverges if an additional attacker enters
///   mid-combat via e.g. Aurelia, the Warleader's extra-combat phase). Same
///   approximation as Goblin Piledriver / Goblin Rabblemaster.
/// - <b>Live combat-attackers provider.</b> The engine doesn't yet expose
///   <c>Game.CurrentCombat.Attackers</c> through the effect-closure surface;
///   production callers must wire the <c>attackingCreaturesSource</c>
///   closure manually. Single-arg <see cref="Create(Player)"/> is a no-op
///   pump body (suitable for shape / dispatcher tests).
/// </summary>
[CardName("Signal Pest")]
public static class SignalPestFactory
{
    public const string CardName = "Signal Pest";
    public const string PrintedManaCost = "{1}";
    public const int Power = 0;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Signal Pest with no live runtime services. Suitable for
    /// card-shape / dispatcher tests — the attack-trigger pump body is a
    /// no-op (no attackers source). The "can't be blocked except by Flying
    /// or Reach" restriction is not yet wired (engine primitive missing —
    /// see class XML doc). The attack trigger is still attached to the card
    /// shape so <see cref="ICard.Abilities"/> includes it.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, attackingCreaturesSource: null);

    /// <summary>
    /// Construct Signal Pest with optional runtime services.
    /// <paramref name="triggers"/> registers the attack trigger with a live
    /// manager. <paramref name="attackingCreaturesSource"/> supplies the
    /// live attacker snapshot at trigger-resolution time so the
    /// "each other attacking creature" count can be computed.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager to register the attack trigger
    /// against. May be null — trigger is still attached to the card shape
    /// so <see cref="ICard.Abilities"/> includes it.</param>
    /// <param name="attackingCreaturesSource">Closure returning the current
    /// attacker creature list. Called at trigger resolution. May be null —
    /// pump body is a no-op.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<Creature>>? attackingCreaturesSource)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Pest });

        // CR 301.1 / 302.1 — Artifact Creature: additively flag the
        // Artifact type so HasType lookups + colour identity see both
        // types (mirrors Ornithopter / Memnite / Vault Skirge).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 508.1f — "Signal Pest gets +1/+0 for each other attacking
        // creature." Modelled as an attack trigger snapshot (deferred from
        // a true static — see class XML doc). On resolution, count every
        // attacking creature OTHER than Signal Pest itself and register a
        // PumpUntilEndOfTurnEffect for +N/+0 EOT. Same closure shape as
        // GoblinPiledriverFactory; the difference is no subtype filter
        // (any creature counts) and the per-unit bonus is +1 not +2.
        var pumpEffect = new Effect(
            $"{CardName}: +1/+0 EOT for each other attacking creature",
            () =>
            {
                if (attackingCreaturesSource == null) return;
                if (card.ActiveEffects == null) return;

                var attackers = attackingCreaturesSource() ?? Array.Empty<Creature>();
                int otherAttackers = 0;
                foreach (var atk in attackers)
                {
                    if (atk == null) continue;
                    if (ReferenceEquals(atk, card)) continue;
                    otherAttackers++;
                }

                if (otherAttackers == 0) return;
                card.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(card, otherAttackers, 0));
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { pumpEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }
}
