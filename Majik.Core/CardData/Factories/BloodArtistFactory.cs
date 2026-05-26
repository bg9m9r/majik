using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Blood Artist (Avacyn Restored, {1}{B}).
///
/// Creature — Vampire 0/1. Oracle text (Scryfall, verified):
///   "Whenever Blood Artist or another creature dies, target player loses
///    1 life and you gain 1 life."
///
/// Blood Artist is the original aristocrats death-drain — every creature
/// death on either side of the battlefield drains 1 from a chosen
/// player and gains 1 to the controller. Pairs with sacrifice fodder
/// (Bloodghast, tokens) and Falkenrath Noble / Zulaport Cutthroat as
/// the Death-Drain Cycle.
///
/// ## Implemented (v1)
/// - 0/1 Creature — Vampire at {1}{B}, owner/controller wired.
/// - <b>Death trigger</b> (CR 603.1 + CR 700.4): a single
///   <see cref="TriggeredAbility"/> fires on
///   <see cref="CardMovedEvent"/> with FromZone = Battlefield + ToZone =
///   Graveyard when the moved card has <see cref="CardType.Creature"/>.
///   The printed "Blood Artist or another creature" wording collapses to
///   "a creature" because the Artist is itself a creature — the union
///   reduces to one predicate (same shape as Cruel Celebrant's
///   self-or-other collapse, but here the union spans ALL creatures, not
///   just controlled ones). CR 603.10 — the dying card's snapshot is
///   read off <see cref="Permanent.Controller"/> (the engine keeps
///   controller across the zone move; identical posture to The Meathook
///   Massacre's dies-triggers).
/// - <b>Drain side</b>: on resolution drains 1 from the chosen target
///   player and gains 1 life to the controller. The target is supplied
///   by an optional <paramref name="targetResolver"/> (mirrors
///   The Meathook Massacre / Cruel Celebrant's resolver convention —
///   single-arg <c>Create(owner)</c> silently no-ops the drain side;
///   lifegain ALWAYS fires per the printed semantics — the printed text
///   reads "target player loses 1 life AND you gain 1 life" as two
///   discrete clauses, not as a lifelink-style combined event).
///
/// ## Notes
/// - <b>Self-trigger</b>: Blood Artist's own death triggers its ability
///   (CR 603.6c — a leaves-the-battlefield-style trigger that names
///   itself looks back to the last known information just before
///   leaving; the trigger is active in the graveyard for resolution).
///   v1 keeps activeZones at Battlefield + Graveyard for self-trigger
///   correctness — the IsTriggered predicate fires on the move event,
///   then the ability resolves from the graveyard. Same shape used by
///   Falkenrath Noble (a strict cousin printed earlier in Innistrad).
/// - <b>Targeting</b>: "target player" is a single-player target (CR
///   115.1). v1 surfaces this via the resolver lambda — the bot / UI
///   picks a player, the resolver returns them, the drain executes.
///   With no resolver the drain silently no-ops (shape-test path).
/// - <b>Discrete life events</b>: CR 119.3 — the lifegain and lifeloss
///   are separate life-change events. This matters for lifegain-payoff
///   triggers (Heliod, Sun-Crowned) and life-loss-matters effects
///   (Sanguine Bond / Vito) — neither side has lifelink semantics.
///
/// ## Deferred (v1 gaps)
/// - <b>Last-known-information for the dying permanent</b>: CR 603.10 —
///   the moved card's controller must be read from LKI at the moment of
///   death. The engine currently keeps <see cref="Permanent.Controller"/>
///   on the card after the zone move, so this v1 implementation reads
///   it directly. A future LKI snapshot pass would replace the
///   controller read with a captured value. Same posture as Cruel
///   Celebrant / Meathook.
/// </summary>
[CardName("Blood Artist")]
public static class BloodArtistFactory
{
    public const string CardName = "Blood Artist";
    public const string PrintedManaCost = "{1}{B}";
    public const int Power = 0;
    public const int Toughness = 1;
    public const int DrainAmount = 1;
    public const int GainAmount = 1;

    /// <summary>
    /// Construct Blood Artist with no live runtime services. The
    /// death-trigger is attached to the card shape but not registered
    /// with a <see cref="TriggerManager"/>, and no target resolver is
    /// wired (so the drain side is a no-op while the lifegain side
    /// still fires). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, targetResolver: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Blood Artist with optional runtime services.
    /// <paramref name="targetResolver"/> supplies the single target
    /// player the death-trigger drains 1 life from on resolution.
    /// <paramref name="triggers"/> registers the triggered ability so
    /// the bus drives it automatically.
    /// </summary>
    public static Creature Create(
        Player owner,
        Func<Player?>? targetResolver,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Vampire });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Death trigger — CR 603.1 + CR 700.4.
        //   "Whenever Blood Artist or another creature dies, target
        //    player loses 1 life and you gain 1 life."
        // The "or another" wording collapses to "a creature" because
        // Blood Artist is itself a creature — one predicate spanning
        // every creature regardless of controller.
        // ----------------------------------------------------------------
        var diesCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            return e.Card.HasType(CardType.Creature);
        });

        var drainEffect = new Effect(
            $"{CardName}: target player loses 1 life + controller gains 1 life",
            () =>
            {
                var target = targetResolver?.Invoke();
                target?.LoseLife(DrainAmount);
                owner.GainLife(GainAmount);
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: diesCondition,
            effects: new IEffect[] { drainEffect },
            // CR 603.6c — self-naming dies trigger must remain active in
            // the graveyard so Blood Artist's OWN death still resolves
            // the drain/gain. Same posture as Falkenrath Noble.
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }
}
