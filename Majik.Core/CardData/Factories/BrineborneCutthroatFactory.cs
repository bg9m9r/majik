using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Brineborn Cutthroat (Throne of Eldraine, {1}{U}).
///
/// Creature — Merfolk Pirate 2/1. Oracle text (verified against Scryfall):
///   "Flash (You may cast this spell any time you could cast an instant.)
///    Whenever you cast a spell during an opponent's turn, put a +1/+1
///    counter on this creature."
///
/// ## Shape source
/// Card identity (name, {1}{U}, 2/1, Creature — Merfolk Pirate) is loaded
/// from <c>Majik.Core/CardData/Cards/brineborn-cutthroat.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="FaerieSeerFactory"/> / <see cref="BorderlandRangerFactory"/>.
/// The Flash keyword and the cast-during-opponent's-turn trigger are
/// attached in code below: the JSON ability schema does not yet express
/// keyword markers or cast-triggered counter accumulators.
///
/// ## Implemented (v1)
/// - 2/1 Merfolk Pirate (CR 205.3m) at {1}{U}.
/// - <b>Flash</b> (CR 702.8): <see cref="KeywordAbility"/> marker the
///   cast-flow consults for instant-speed casting. Same shape as
///   <see cref="MerfolkTricksterFactory"/> / Spell Queller / Snapcaster Mage.
/// - <b>Cast-during-opponent's-turn trigger (CR 603.1 / 122.1)</b> — fires
///   on a <see cref="SpellCastEvent"/> when BOTH:
///   <list type="number">
///     <item>the spell's controller is Brineborn Cutthroat's controller —
///     "you cast a spell" (CR 603.1, controller-scoped); AND</item>
///     <item>the active player is NOT Brineborn Cutthroat's controller —
///     "during an opponent's turn" (CR 109.5 / CR 500.1), detected via the
///     optional <see cref="TurnManager"/> supplied at construction
///     (<see cref="TurnManager.ActivePlayer"/>). This is the mirror image
///     of <see cref="VoiceOfResurgenceFactory"/>'s "opponent casts during
///     YOUR turn" gate — there the active player must equal the controller;
///     here it must differ.</item>
///   </list>
///   On resolution the effect drops a single
///   <see cref="CounterType.PlusOnePlusOne"/> counter on Brineborn Cutthroat
///   (CR 122.1c — counters placed directly, no SBA gating). Persistent
///   accumulator across turns (no per-turn cap), identical effect body to
///   <see cref="SpriteDragonFactory"/>'s cast-noncreature counter.
///
///   NOTE the predicate fires on ANY spell the controller casts during the
///   opponent's turn — including casting Brineborn Cutthroat itself with
///   Flash on the opponent's turn. That is correct: the card's own cast
///   does NOT trigger this (its <see cref="SpellCastEvent"/> fires while the
///   trigger's <see cref="TriggeredAbility.ActiveZones"/> is still Stack /
///   not yet Battlefield), so the trigger is inert until the creature has
///   resolved onto the battlefield (CR 603.6a — same posture as Sprite
///   Dragon / Voice of Resurgence).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The cast trigger is attached
///   to the card for shape inspection; not registered with a
///   <see cref="TriggerManager"/>, and the active-player gate is loose (no
///   <see cref="TurnManager"/>). Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, TriggerManager?, TurnManager?, ReplacementBus?)"/>
///   — fully wired. When <paramref name="triggers"/> is supplied the cast
///   trigger registers so a matching <see cref="SpellCastEvent"/> queues the
///   ability. When <paramref name="turnManager"/> is supplied the
///   "during an opponent's turn" half is enforced precisely; when null it is
///   loose (any controller-cast spell satisfies the gate — shape posture
///   matching <see cref="VoiceOfResurgenceFactory"/>'s deferred-services
///   default). When <paramref name="replacements"/> is supplied the counter
///   placement routes through <see cref="CountersService.Add"/> so Hardened
///   Scales / Doubling Season can rewrite the count (CR 614).
///
/// ## Deferred (v1 gaps)
/// - <b>Continuous P/T recomputation</b> — effective P/T is derived from
///   base 2/1 plus +1/+1 counters via the standard
///   <see cref="CounterCollection"/> path (CR 613.4 layer 7d), inherited
///   from every other +1/+1-counter user. No card-specific layer wiring.
/// </summary>
[CardName("Brineborn Cutthroat")]
public static class BrineborneCutthroatFactory
{
    public const string CardName = "Brineborn Cutthroat";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("brineborn-cutthroat");

    /// <summary>
    /// Construct Brineborn Cutthroat with no live wiring. The cast trigger
    /// is attached to the card for shape inspection; not registered with any
    /// <see cref="TriggerManager"/>, and the active-player gate is loose
    /// (no <see cref="TurnManager"/>). Suitable for dispatcher / structural
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, turnManager: null, replacements: null);

    /// <summary>
    /// Construct Brineborn Cutthroat with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the cast trigger is registered
    /// so a matching <see cref="SpellCastEvent"/> (controller casts a spell
    /// during an opponent's turn) automatically queues the ability. When
    /// <paramref name="turnManager"/> is supplied the "during an opponent's
    /// turn" gate is enforced via <see cref="TurnManager.ActivePlayer"/>;
    /// when null the gate is loose. When <paramref name="replacements"/> is
    /// supplied the +1/+1 counter placement routes through
    /// <see cref="CountersService.Add"/> for replacement rewrites (CR 614).
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        TurnManager? turnManager,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.8 — Flash. KeywordAbility marker; the cast-flow consults
        // it for instant-speed casting. Same shape as Merfolk Trickster /
        // Spell Queller / Snapcaster Mage.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // ----------------------------------------------------------------
        // Cast-during-opponent's-turn trigger — CR 603.1 / 122.1.
        //   "Whenever you cast a spell during an opponent's turn, put a
        //    +1/+1 counter on this creature."
        //
        // Predicate:
        //   (1) spell controller == Brineborn Cutthroat's controller
        //       ("you cast a spell" — CR 603.1, controller-scoped); AND
        //   (2) active player != controller ("during an opponent's turn" —
        //       CR 109.5 / CR 500.1), via TurnManager.ActivePlayer. Loose
        //       when turnManager is null (the active-turn half collapses to
        //       "matched"), matching Voice of Resurgence's deferred-service
        //       posture. This is the mirror of Voice's gate (there active
        //       player must EQUAL the controller).
        //
        // Effect: one +1/+1 counter (CR 122.1c), identical body to Sprite
        // Dragon's cast-noncreature accumulator.
        // ----------------------------------------------------------------
        var counterEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on it (cast a spell during an opponent's turn)",
            () => CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements));

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<SpellCastEvent>((e, _) =>
            {
                var controller = card.Controller ?? owner;

                // (1) "you cast a spell" — controller-scoped (CR 603.1).
                if (!ReferenceEquals(e.Spell.Controller, controller)) return false;

                // (2) "during an opponent's turn" — active player is anyone
                //     other than the controller (CR 109.5 / CR 500.1). Loose
                //     when no TurnManager is wired.
                var duringOpponentsTurn =
                    turnManager == null
                    || (turnManager.ActivePlayer != null
                        && !ReferenceEquals(turnManager.ActivePlayer, controller));

                return duringOpponentsTurn;
            }),
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
