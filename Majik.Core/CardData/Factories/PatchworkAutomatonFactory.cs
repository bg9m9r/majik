using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Patchwork Automaton (Streets of New Capenna /
/// Aetherdrift reprint, {2}).
///
/// Artifact Creature — Construct 1/1. Oracle text (Modern-reprint seed):
///   "Ward {2} (Whenever this creature becomes the target of a spell or
///    ability an opponent controls, counter it unless that player pays {2}.)
///    Whenever you cast an artifact spell, put a +1/+1 counter on this
///    creature."
///
/// NOTE: the SNC printing read "{3} 0/0 with Ward {2}, enters with a +1/+1
/// counter". The current oracle (and our embedded Modern seed) is the
/// Aetherdrift / reprint shape: <c>{2}</c> base 1/1, no ETB counter. We
/// implement the seed oracle — that's what the engine's
/// <see cref="EmbeddedCardRepository"/> tracks against
/// <c>IsImplemented</c>.
///
/// ## Implemented (v1)
///
/// - 1/1 <b>Artifact Creature</b> — Construct at <c>{2}</c>. The base
///   <see cref="Creature"/> constructor only registers
///   <see cref="CardType.Creature"/>; the Artifact type is additively
///   stamped via <c>AddCardType(CardType.Artifact)</c> (mirrors
///   <see cref="SteelOverseerFactory"/> / <see cref="KappaCannoneerFactory"/>'s
///   multi-type shape). This also makes Patchwork Automaton's own cast
///   satisfy the "Whenever you cast an artifact spell" predicate (the
///   automaton is itself an artifact spell while on the stack — CR 112.1a).
///
/// - <b>Ward {2} (CR 702.21)</b>: wired as a
///   <see cref="KeywordAbility"/> marker plus a <see cref="WardEffect"/>
///   builder exposed via <see cref="BuildWardEffect"/>. Same posture as
///   <see cref="KappaCannoneerFactory"/>'s Ward — the marker is
///   structural-only until the spell-resolution path consults
///   <see cref="WardEffect"/> for every spell or ability that targets a
///   permanent.
///
/// - <b>Cast trigger (CR 603.1)</b>: wired via
///   <see cref="EventTriggerCondition{T}"/> over
///   <see cref="SpellCastEvent"/>. Predicate:
///     1. The cast spell's controller is Patchwork Automaton's current
///        controller (CR 109.5 — "you cast").
///     2. The spell's card has <see cref="CardType.Artifact"/>.
///   On resolution: place one <see cref="CounterType.PlusOnePlusOne"/>
///   counter on the automaton via <see cref="CountersService.Add"/> so
///   Hardened Scales / Doubling Season replacements can rewrite the
///   count (CR 614). Active only while on the battlefield.
///
///   Patchwork Automaton's own cast counts (CR 603.6a — the trigger
///   condition checks the cast EVENT, not the on-battlefield identity of
///   the source at the moment of casting; the source is still on the
///   stack when SpellCastEvent fires, so the trigger queues and then
///   resolves AFTER the automaton hits the battlefield, with the
///   battlefield-gate satisfied at resolution time).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. The cast trigger is
///   attached to the card for inspection but not registered (no live
///   trigger manager). Suitable for dispatcher / shape tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ReplacementBus?)"/>
///   — fully wired. Cast trigger registered with
///   <paramref name="triggers"/>; counter placement routes through
///   <see cref="CountersService.Add"/> with the supplied replacement bus
///   (null → direct add).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Ward consultation</b>: <see cref="WardEffect"/> is a standalone
///   helper, not yet plumbed onto a battlefield-attached triggered
///   ability. Shared deferred slice with Kappa Cannoneer / Murktide
///   Regent / every other Ward card in the catalog.
/// </summary>
[CardName("Patchwork Automaton")]
public static class PatchworkAutomatonFactory
{
    public const string CardName = "Patchwork Automaton";
    public const string PrintedManaCost = "{2}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>CR 702.21 — printed Ward cost: {2}.</summary>
    public const string WardCost = "{2}";

    /// <summary>
    /// CR 702.21 — Patchwork Automaton's printed Ward {2} effect, bound to
    /// the supplied <paramref name="card"/>. Exposed as a builder for
    /// callers (tests, spell-resolution path once Ward wiring lands).
    /// </summary>
    public static WardEffect BuildWardEffect(Creature card) =>
        new(card, ManaCost.Parse(WardCost));

    /// <summary>
    /// Construct Patchwork Automaton with no live wiring. Cast trigger is
    /// attached to the card shape; not registered. Suitable for
    /// dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, replacements: null);

    /// <summary>
    /// Construct Patchwork Automaton with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Reserved for future Ward-trigger wiring. Not
    /// consumed directly today.</param>
    /// <param name="triggers">TriggerManager for the cast trigger. May be
    /// null — the trigger is still attached to the card shape for
    /// inspection.</param>
    /// <param name="replacements">ReplacementBus for routing the +1/+1
    /// counter placement through <see cref="CountersService.Add"/> so
    /// Hardened Scales / Doubling Season replacements can rewrite the
    /// count (CR 614). May be null — the counter is placed directly.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Construct });

        // CR 301.1 / 302.1 — Patchwork Automaton is an Artifact Creature.
        // Stamp the Artifact type on the Creature shell so HasType-based
        // lookups see it (mirrors Steel Overseer / Kappa Cannoneer's
        // multi-type shape). This also makes the automaton's own cast
        // satisfy its own cast-trigger predicate.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Ward {2} (CR 702.21) — marker keyword. WardEffect exists as a
        // standalone helper (BuildWardEffect bounds an instance to the
        // live card) but the battlefield-attached triggered-ability
        // surface is deferred; the marker keeps Patchwork Automaton
        // shape-correct alongside Kappa Cannoneer / the rest of the Ward
        // catalog.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Ward", card, owner));

        // ----------------------------------------------------------------
        // Cast trigger — CR 603.1.
        //   "Whenever you cast an artifact spell, put a +1/+1 counter on
        //    this creature."
        // "You cast" → spell.Controller == automaton.Controller.
        // Artifact gate: spell.Card.HasType(Artifact).
        //
        // Patchwork Automaton's own cast fires the trigger (CR 603.6a):
        // SpellCastEvent fires when the automaton is on the stack, the
        // trigger queues, and resolves on top of the stack AFTER the
        // automaton lands on the battlefield — at resolution time the
        // activeZones gate is satisfied.
        // ----------------------------------------------------------------
        var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            // CR 109.5 — "you cast" matches the source's current
            // controller (with owner as a fallback in case the source
            // hasn't been re-zoned yet).
            var liveController = card.Controller ?? owner;
            if (!ReferenceEquals(e.Spell.Controller, liveController)) return false;
            return e.Spell.Card.HasType(CardType.Artifact);
        });

        var counterEffect = new Effect(
            $"{CardName}: +1/+1 counter (whenever you cast an artifact spell)",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                // CR 122.1c — counter placement; routed through
                // CountersService so Hardened Scales / Doubling Season
                // replacements observe the intent (CR 614).
                CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements);
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }
}
