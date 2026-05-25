using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Quirion Beastcaller (Dominaria United, {G}).
///
/// Creature — Elf Druid 1/1. Oracle text (Scryfall, verified):
///   "Quirion Beastcaller enters with a +1/+1 counter on it for each other
///    creature spell you've cast this turn.
///    When Quirion Beastcaller dies, distribute a number of +1/+1 counters
///    equal to the number of +1/+1 counters on Quirion Beastcaller among
///    any number of target creatures."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Elf Druid at {G}.
/// - <b>Dies trigger (CR 603.6c / CR 700.4)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="Events.CardMovedEvent"/>
///   filtered to <c>FromZone == Battlefield AND ToZone == Graveyard</c>
///   for this card. Active zones = {Battlefield, Graveyard} so the
///   condition still matches after <see cref="Services.ZoneService"/>
///   has already stamped the card's <see cref="Card.Zone"/> = Graveyard
///   before publishing the event (mirrors Matter Reshaper / Wurmcoil
///   Engine — CR 603.6d "looks back").
/// - On resolve: read the live +1/+1 counter count off Quirion (last-known-
///   information per CR 608.2g — the card has already moved to the
///   graveyard but counters persist on <see cref="Card.Counters"/> until
///   the cleanup step). v1 deterministic dump: place all N counters on
///   the first chosen target creature, mirroring the deterministic
///   target-list collapse used by other multi-target effects pre-prompt
///   (Slogurk's "up to three lands" return is the closest cousin).
///
/// ## Deferred (v1 gaps)
/// - <b>"Enters with N +1/+1 counters" — ETB-counter half</b>: requires a
///   per-turn "creature spells you've cast this turn" counter that
///   <see cref="Game.TurnState"/> doesn't expose today (the existing
///   <see cref="Game.TurnState.SpellsCastByPlayer"/> tally counts ALL
///   spells, not specifically creature spells). v1 ships Quirion with
///   no ETB-counter replacement — enters as a vanilla 1/1, so the dies
///   trigger distributes 0 counters by default unless external +1/+1
///   sources stacked counters on it (Hardened Scales / Conclave Mentor /
///   adapt-style adds — those paths exercise the dies-distribute half
///   end-to-end without needing the creature-spell-cast tracking).
///   Lands when a creature-spell-cast watcher is added to
///   <see cref="Game.TurnState"/> and threaded through
///   <see cref="Effects.EntersWithCountersReplacement"/> (same gap
///   Walking Ballista documents for X-thread-through).
/// - <b>"Distribute … among any number of target creatures"</b>: v1
///   collapses the distribution to a single deterministic target (first
///   creature in the resolved target list). Real agent-driven N-target
///   distribution with per-target count choices awaits the modal /
///   distribution prompt MVP — same posture as Slogurk's "up to three"
///   target collapse, Bone Shards' modal collapse, Hardened Scales'
///   amount-bump composition deferral.
/// </summary>
[CardName("Quirion Beastcaller")]
public static class QuirionBeastcallerFactory
{
    public const string CardName = "Quirion Beastcaller";
    public const string PrintedManaCost = "{G}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Quirion Beastcaller with no live <see cref="TriggerManager"/>
    /// wiring. The dies trigger is attached structurally for shape /
    /// dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Quirion Beastcaller with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied the dies
    /// trigger registers so a qualifying Battlefield → Graveyard
    /// <see cref="Events.CardMovedEvent"/> automatically queues the
    /// ability (CR 603.2).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Druid });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Dies trigger (CR 603.6c / CR 700.4):
        //   "When Quirion Beastcaller dies, distribute a number of +1/+1
        //    counters equal to the number of +1/+1 counters on Quirion
        //    Beastcaller among any number of target creatures."
        //
        // v1 reads the live counter count off the dying card (last-known-
        // information per CR 608.2g) and deterministically dumps all N
        // counters on the first chosen target creature. Multi-target
        // distribution is deferred (see class xmldoc).
        // ----------------------------------------------------------------
        TriggeredAbility? diesTrigger = null;

        var diesEffect = new Effect(
            $"{CardName}: distribute N +1/+1 counters on chosen creature (single-target collapse)",
            () =>
            {
                if (diesTrigger == null) return;
                if (diesTrigger.ChosenTargets.Count == 0
                    || diesTrigger.ChosenTargets[0].Count == 0)
                {
                    // Printed "any number of target creatures" — zero
                    // chosen is legal (CR 601.2c), and the counters
                    // simply go nowhere.
                    return;
                }

                if (diesTrigger.ChosenTargets[0][0] is not Creature target) return;

                // CR 608.2b — resolution-time legality.
                if (target.Zone != ZoneType.Battlefield) return;

                // CR 608.2g — last-known-information. Quirion has already
                // moved to the graveyard; counters persist on the card
                // object until the next cleanup step (CR 514.2).
                var n = card.Counters.Count(CounterType.PlusOnePlusOne);
                if (n <= 0) return;

                Fx.PlaceCounter(target, CounterType.PlusOnePlusOne, n);
            });

        diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { diesEffect },
            interveningIf: null,
            // ActiveZones = {Battlefield, Graveyard} — Wurmcoil / Matter
            // Reshaper posture so the trigger still matches after the
            // ZoneService stamp.
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any number of target creatures",
                    // Printed "any number of" — MinTargets = 0 honours the
                    // "may distribute zero" path (CR 601.2c).
                    MinTargets: 0,
                    MaxTargets: 1, // v1 single-target collapse — see class xmldoc.
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }
}
