using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Noble Hierarch (Conflux / Modern Horizons 2).
///
/// Creature — Human Druid {G} 0/1.
/// Oracle text:
///   "Exalted (Whenever a creature you control attacks alone, that creature
///    gets +1/+1 until end of turn.)
///    {T}: Add {G}, {W}, or {U}."
///
/// ## Implemented (v1)
/// - 0/1 Creature — Human Druid, mana cost {G}, owner/controller wired.
/// - <b>Three mana abilities (CR 605.1)</b>: {T}: Add {G}, {T}: Add {W},
///   {T}: Add {U}. Each is a <see cref="ManaAbility"/> with a
///   <c>canActivateCheck = !IsTapped</c> gate, mirroring the Delighted
///   Halfling pattern.
/// - <b>Exalted keyword marker</b> (CR 702.90) wired as a
///   <see cref="KeywordAbility"/> so data-side tools see it.
/// - <b>Exalted trigger (CR 702.90b)</b>: fires on every
///   <see cref="CreatureAttacksEvent"/> while Noble Hierarch is on the
///   battlefield. At trigger-resolution time the factory reads the live
///   attackers via the injected <c>attackingCreaturesSource</c> closure.
///   If exactly one attacker whose controller is Noble Hierarch's controller
///   is found, a <see cref="PumpUntilEndOfTurnEffect"/> +1/+1 EOT is
///   registered on that attacker's <see cref="Creature.ActiveEffects"/>.
///   If there are two or more attackers the trigger no-ops (CR 702.90b —
///   "attacks alone" means no other attackers from the controller's side).
///
/// ## Source-closure injection
/// Same pattern as Goblin Piledriver's <c>attackingCreaturesSource</c>.
/// The engine doesn't yet expose a global "attacking creatures" view from
/// inside the effect closure, so the factory accepts a
/// <c>Func&lt;IReadOnlyList&lt;Creature&gt;&gt;</c> that callers (Game /
/// tests) populate. When null the pump body is a no-op — suitable for
/// shape / dispatcher tests where only card identity + ability presence is
/// asserted. The trigger condition still fires on any
/// <see cref="CreatureAttacksEvent"/> whose attacker is controlled by Noble
/// Hierarch's controller (so the trigger is correctly attached to the card
/// shape even in the single-arg path).
///
/// ## Deferred (v1 gaps)
/// - <b>Live combat-attackers provider</b>: same gap as Goblin Piledriver.
///   Once <c>ICurrentCombatProvider</c> ships the factory will read
///   attackers off the live provider directly.
/// - <b>Trigger-on-stack timing</b>: pump is registered immediately at
///   trigger-resolution (same collapse as Goblin Piledriver / Kraul
///   Harpooner). Observationally equivalent for the +1/+1 read at damage
///   step in a single-combat-step game.
/// - <b>Opponent exalted stacking</b>: multiple Exalted sources from the
///   same controller each add +1/+1 — a correct v1 observation, since each
///   factory instance registers its own trigger independently.
/// </summary>
[CardName("Noble Hierarch")]
public static class NobleHierarchFactory
{
    public const string CardName = "Noble Hierarch";
    public const string PrintedManaCost = "{G}";
    public const int Power = 0;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Noble Hierarch with no live TriggerManager wiring and no
    /// attackers-source. Suitable for card-shape / dispatcher tests — the
    /// exalted trigger is attached to the card shape but the pump body is a
    /// no-op. All three mana abilities are always wired.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, attackingCreaturesSource: null);

    /// <summary>
    /// Construct Noble Hierarch with optional runtime services.
    /// <paramref name="triggers"/> registers the exalted trigger with a live
    /// manager. <paramref name="attackingCreaturesSource"/> supplies the live
    /// attacker snapshot at trigger-resolution time so the "attacks alone"
    /// check can be made.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager to register the exalted trigger
    /// against. May be null — trigger is still attached to the card shape so
    /// <see cref="ICard.Abilities"/> includes it.</param>
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
            subtypes: new[] { CardSubtype.Human, CardSubtype.Druid });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.90 — Exalted keyword marker so data-side tools see it.
        card.AddAbility(new KeywordAbility("Exalted", card, owner));

        // CR 605.1 — Three mana abilities (no stack). Each taps Noble
        // Hierarch via the Permanent.Tap() path inside ManaAbility.Activate();
        // the canActivateCheck gates on !IsTapped so duplicate activations
        // are prevented. Mirrors the Delighted Halfling multi-colour pattern.

        // {T}: Add {G}
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse("{G}"),
            canActivateCheck: () => !card.IsTapped));

        // {T}: Add {W}
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse("{W}"),
            canActivateCheck: () => !card.IsTapped));

        // {T}: Add {U}
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse("{U}"),
            canActivateCheck: () => !card.IsTapped));

        // CR 702.90b — Exalted. "Whenever a creature you control attacks
        // alone, that creature gets +1/+1 until end of turn."
        // The trigger fires on every CreatureAttacksEvent whose attacker is
        // controlled by Noble Hierarch's controller. At resolution time the
        // factory reads the live attackers list via attackingCreaturesSource;
        // if exactly 1 controller-side attacker is found it is pumped +1/+1.
        var exaltedEffect = new Effect(
            "Noble Hierarch Exalted: +1/+1 EOT when a creature attacks alone",
            () =>
            {
                if (attackingCreaturesSource == null) return;

                var attackers = attackingCreaturesSource() ?? Array.Empty<Creature>();

                // Count only creatures controlled by Noble Hierarch's current
                // controller (CR 702.90b — "a creature you control attacks
                // alone" means no other controlled creatures are attacking).
                var controlledAttackers = new List<Creature>();
                foreach (var atk in attackers)
                {
                    if (atk == null) continue;
                    if (!ReferenceEquals(atk.Controller, card.Controller)) continue;
                    controlledAttackers.Add(atk);
                }

                // "attacks alone" — exactly 1 controlled attacker.
                if (controlledAttackers.Count != 1) return;

                var soloAttacker = controlledAttackers[0];
                if (soloAttacker.ActiveEffects == null) return;

                soloAttacker.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(soloAttacker, 1, 1));
            });

        // The trigger condition fires for any CreatureAttacksEvent where the
        // attacker is controlled by Noble Hierarch's controller — the pump
        // body itself gates on the "exactly 1 attacker" invariant so we don't
        // need a finer predicate here.
        var exaltedTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CreatureAttacksEvent>(
                (e, _) => ReferenceEquals(e.Attacker.Controller, card.Controller)),
            effects: new IEffect[] { exaltedEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(exaltedTrigger);
        triggers?.RegisterTriggeredAbility(exaltedTrigger);

        return card;
    }
}
