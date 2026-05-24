using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Piledriver (Onslaught / many reprints,
/// Creature — Goblin Warrior {1}{R}).
///
/// Oracle text:
///   "Protection from blue.
///    Whenever Goblin Piledriver attacks, it gets +2/+0 until end of turn
///    for each other attacking Goblin."
///
/// ## Implemented (v1)
/// - 1/2 Creature — Goblin Warrior, mana cost {1}{R}, owner/controller wired.
/// - <b>Protection from blue</b> wired as a <see cref="ProtectionAbility"/>
///   (CR 702.16 — DEBT-A: damage prevention, enchant/equip restriction,
///   block restriction, target restriction). Same shape as Sword of Fire
///   and Ice's two protection riders.
/// - <b>Attack triggered ability (CR 508.1f)</b> wired via
///   <see cref="Triggers.OnAttackSelf"/> against
///   <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>.
///   When the trigger resolves, the factory snapshots the current
///   attacking-creatures set via the supplied
///   <c>attackingCreaturesSource</c> closure, counts every other attacking
///   Goblin (excluding Piledriver itself), and registers a
///   <see cref="PumpUntilEndOfTurnEffect"/> for +2X/+0 EOT on the
///   ContinuousEffectsService attached to Piledriver via
///   <see cref="Creature.ActiveEffects"/>. The +2X pump is observable
///   through <see cref="Creature.GetPower"/>.
///
/// ## Source closure injection
/// The trigger effect needs a live read of the current attackers list at
/// resolution time (per the oracle's "each other attacking Goblin"
/// snapshot). The engine doesn't yet expose a global "attacking creatures"
/// view from inside the effect closure, so the factory accepts a
/// <c>Func&lt;IReadOnlyList&lt;Creature&gt;&gt;</c> closure that callers
/// (Game / tests) populate with the live attacker list. When null, the
/// pump body is a no-op — suitable for shape / dispatcher tests where
/// only the card identity + ability presence is asserted. Same shape as
/// Primeval Titan's <c>selector</c> + Plague Engineer's <c>typeChooser</c>
/// — agent-prompt integration is deferred.
///
/// ## Deferred (v1 gaps)
/// - <b>Live combat-attackers provider</b>: the engine doesn't yet expose
///   <c>Game.CurrentCombat.Attackers</c> through the effect-closure
///   surface; production callers must wire the closure manually. Once
///   <c>ICurrentCombatProvider</c> ships, this factory will read attackers
///   off the live provider directly.
/// - <b>Trigger-on-stack timing</b>: the pump is registered immediately
///   when the trigger effect runs (same as Kraul Harpooner's Undergrowth
///   pump). Real MTG semantics put the trigger on the stack and resolve
///   it before blockers are declared; v1 collapses this to the trigger-
///   resolves-now shape (observationally equivalent for the +2X/+0 read
///   at damage step).
/// </summary>
[CardName("Goblin Piledriver")]
public static class GoblinPiledriverFactory
{
    public const string CardName = "Goblin Piledriver";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 1;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Goblin Piledriver with no live TriggerManager wiring and
    /// no attackers-source. Suitable for card-shape / dispatcher tests —
    /// the attack trigger is attached to the card shape but the pump body
    /// is a no-op (no source of attackers means zero pump). Protection
    /// from blue is always wired.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, attackingCreaturesSource: null);

    /// <summary>
    /// Construct Goblin Piledriver with optional runtime services.
    /// <paramref name="triggers"/> registers the attack trigger with a
    /// live manager. <paramref name="attackingCreaturesSource"/> supplies
    /// the live attacker snapshot at trigger-resolution time so the
    /// "each other attacking Goblin" count can be computed.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager to register the attack
    /// trigger against. May be null — trigger is still attached to the
    /// card shape so <see cref="ICard.Abilities"/> includes it.</param>
    /// <param name="attackingCreaturesSource">Closure returning the
    /// current attacker creature list. Called at trigger resolution. May
    /// be null — pump body is a no-op.</param>
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
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.16 — Protection from blue. Quality stored normalised; the
        // Rules.Protection / TargetLegality / CombatAbilities helpers
        // interpret it (DEBT-A). Same wiring shape as Sword of Fire and
        // Ice.
        card.AddAbility(new ProtectionAbility("blue"));

        // CR 508.1f — "Whenever Goblin Piledriver attacks, it gets +2/+0
        // until end of turn for each other attacking Goblin." Triggers
        // when Piledriver is declared as an attacker
        // (CreatureAttacksEvent matching this card via
        // Triggers.OnAttackSelf).
        var pumpEffect = new Effect(
            "Goblin Piledriver: +2/+0 EOT for each other attacking Goblin",
            () =>
            {
                if (attackingCreaturesSource == null) return;
                if (card.ActiveEffects == null) return;

                var attackers = attackingCreaturesSource() ?? Array.Empty<Creature>();
                int otherAttackingGoblins = 0;
                foreach (var atk in attackers)
                {
                    if (atk == null) continue;
                    if (ReferenceEquals(atk, card)) continue;
                    if (!atk.HasSubtype(CardSubtype.Goblin)) continue;
                    otherAttackingGoblins++;
                }

                if (otherAttackingGoblins == 0) return;
                card.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(card, otherAttackingGoblins * 2, 0));
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
