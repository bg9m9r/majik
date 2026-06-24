using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pugnacious Hammerskull (The Lost Caverns of Ixalan,
/// {2}{G}).
///
/// Creature — Dinosaur 6/6. Oracle text (verified against Scryfall 2026-06-24):
///   "Whenever this creature attacks while you don't control another Dinosaur,
///    put a stun counter on it. (If a permanent with a stun counter would
///    become untapped, remove one from it instead.)"
///
/// The base shape (name, Creature — Dinosaur, {2}{G}, 6/6) is materialised from
/// the embedded JSON definition (<c>pugnacious-hammerskull.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The attack-trigger is layered on
/// here — the JSON <c>AbilityDefinition</c> schema doesn't express it (same
/// posture as <see cref="HiredClawFactory"/>).
///
/// ## Implemented (v1)
/// - <b>6/6 Creature — Dinosaur at {2}{G}</b>, owner/controller wired.
/// - <b>Attack trigger (CR 603.1 / CR 508.1f / CR 603.4)</b>: an
///   <see cref="EventTriggerCondition{TEvent}"/> over
///   <see cref="AttackersDeclaredEvent"/> that fires when THIS creature is among
///   the declared attackers and its controller is the attacking player (same
///   "this creature attacks" detection as <see cref="GlorybringerFactory"/>).
///   The "while you don't control another Dinosaur" rider is an INTERVENING-IF
///   (CR 603.4) — re-checked as the ability would trigger AND again on
///   resolution — that scans the controller's battlefield for another Dinosaur
///   (same "control another X" scan shape as <see cref="DominatorDroneFactory"/>).
///   On resolution it places a single <see cref="CounterType.Stun"/> counter on
///   itself (CR 122.1g). Stun counters are honoured by the untap-step
///   replacement in <c>TurnDriver.UntapStep</c> — the same source of truth
///   <see cref="SleepCursedFaerieFactory"/> relies on. Stun counters are NOT
///   +1/+1 counters, so the placement does not route through
///   <see cref="CountersService"/> / replacements (Hardened Scales / Doubling
///   Season — CR 614 — do not apply to non-+1/+1 counters); it uses
///   <see cref="CounterCollection.Add"/> directly, exactly as Sleep-Cursed
///   Faerie places its stun counters.
/// </summary>
[CardName("Pugnacious Hammerskull")]
public static class PugnaciousHammerskullFactory
{
    public const string CardName = "Pugnacious Hammerskull";
    public const string Slug = "pugnacious-hammerskull";

    /// <summary>CR 122.1g — stun counters placed per attack trigger.</summary>
    public const int StunCounterAmount = 1;

    /// <summary>
    /// Construct Pugnacious Hammerskull with no live runtime services. This is
    /// the overload <see cref="NamedCardFactory"/> dispatches to on the
    /// production routed build. The attack trigger is attached to the card
    /// shape; without a <see cref="TriggerManager"/> the bus won't surface it
    /// as pending (suitable for dispatcher / structural tests).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Pugnacious Hammerskull with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Reserved for future lifecycle subscribers; not
    /// used directly today (the trigger condition + intervening-if read live
    /// state, no event-bus subscription needed).</param>
    /// <param name="triggers">TriggerManager the attack trigger registers with
    /// so it surfaces as pending. May be null — the trigger is still attached to
    /// the card shape.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature —
        // Dinosaur, {2}{G}, 6/6). The JSON carries no abilities — the attack
        // trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        BuildAttackTrigger(card, owner, triggers);

        return card;
    }

    // --- Attack trigger: stun-counter on no-other-Dinosaur attack ----------

    private static void BuildAttackTrigger(
        Creature card,
        Player owner,
        TriggerManager? triggers)
    {
        // CR 603.1 / CR 508.1f — "Whenever this creature attacks ... put a stun
        // counter on it." Fires on AttackersDeclaredEvent where the attacking
        // player is this card's controller AND this creature is among the
        // declared attackers (same self-attack detection as Glorybringer).
        var condition = new EventTriggerCondition<AttackersDeclaredEvent>(
            (e, _) => IsThisCreatureAttacking(e, card, owner));

        // CR 603.4 — "while you don't control another Dinosaur" is an
        // intervening-if condition: checked as the ability would trigger AND
        // again on resolution. Scan the controller's battlefield for ANOTHER
        // Dinosaur (a Dinosaur other than Hammerskull itself).
        bool DoesNotControlAnotherDinosaur()
        {
            var controller = card.Controller ?? owner;
            return !controller.Zones.Battlefield.GetCards().Any(c =>
                !ReferenceEquals(c, card)                 // "another"
                && c.HasSubtype(CardSubtype.Dinosaur));   // Dinosaur (CR 205.3)
        }

        // CR 122.1g — place one stun counter on itself. Stun counters are not
        // +1/+1 counters, so this does not route through CountersService /
        // replacements (Hardened Scales / Doubling Season do not double stun
        // counters); use the counter collection directly, as Sleep-Cursed
        // Faerie does. The untap-step replacement in TurnDriver.UntapStep
        // honours the counter.
        var stunEffect = new Effect(
            $"{CardName}: put a stun counter on itself (whenever it attacks while you control no other Dinosaur)",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                card.Counters.Add(CounterType.Stun, StunCounterAmount);
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { stunEffect },
            interveningIf: DoesNotControlAnotherDinosaur,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);
    }

    private static bool IsThisCreatureAttacking(
        AttackersDeclaredEvent e, Creature card, Player owner)
    {
        var controller = card.Controller ?? owner;
        if (!ReferenceEquals(e.Combat.AttackingPlayer, controller)) return false;
        return e.Combat.Attackers.Any(a => ReferenceEquals(a?.Creature, card));
    }
}
