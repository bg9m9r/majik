using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Taurean Mauler (Time Spiral / reprints, {2}{R}).
///
/// Creature — Shapeshifter 2/2. Oracle text (verified against Scryfall):
///   "Changeling (This card is every creature type.)
///    Whenever an opponent casts a spell, you may put a +1/+1 counter on
///    this creature."
///
/// ## Implemented (v1)
///
/// - <b>2/2 Creature — Shapeshifter</b> at {2}{R}. The base shape (name,
///   Creature, Shapeshifter subtype, {2}{R}, 2/2) is materialised from the
///   embedded JSON definition (<c>taurean-mauler.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>; the Changeling subtype
///   stamping + the cast trigger are layered on here.
///
/// - <b>Changeling (CR 702.73)</b> — the card is every creature type in
///   every zone. Same v1 modelling as <see cref="UnsettledMarinerFactory"/>:
///   the printed <see cref="Card.Subtypes"/> set is stamped with the
///   engine's currently-enumerated creature subtypes (sourced from
///   <see cref="MutavaultAnimateEffect.EveryCreatureType"/> so the changeling
///   list stays in lockstep with Mutavault's animate list — when the enum
///   grows, both pick up the new subtype with no per-card edits) plus the
///   printed <see cref="CardSubtype.Shapeshifter"/> base type, and a
///   <see cref="KeywordAbility"/>("Changeling") marker for UI / future
///   Changeling-aware enumerations. CR 702.73a: "Each object with the
///   changeling ability is each creature type. This ability works everywhere,
///   even outside the game." — stamping the printed list models that
///   static-everywhere posture (no Layer 4 registration needed).
///
/// - <b>Opponent-cast growth trigger (CR 603.6a / 109.5 / 603.7)</b>:
///   "Whenever an opponent casts a spell, you may put a +1/+1 counter on this
///    creature." Wired via <see cref="SpellCastEvent"/> filtered to a spell
///    whose controller is an OPPONENT of this creature's controller (CR 109.5
///    — a player other than the controller; same asymmetric gate as
///    <see cref="CounterbalanceFactory"/>). On resolution one
///    <see cref="CounterType.PlusOnePlusOne"/> counter is placed via
///    <see cref="CountersService.Add"/> (so Hardened Scales / Doubling-Season
///    replacements observe the placement, CR 614). The "you may" choice is
///    auto-taken (v1 always-yes — the rational play; same posture as every
///    other optional-rider "may" until an agent prompt lands).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The trigger is attached for
///   dispatcher / structural tests but not registered (no trigger manager).
/// - <see cref="Create(Player, TriggerManager?, ReplacementBus?)"/> — fully
///   wired. The opponent-cast trigger is registered so a matching
///   <see cref="SpellCastEvent"/> surfaces as pending and resolves to a
///   counter; the replacement bus routes CR 614 counter-doubling rewrites.
/// </summary>
[CardName("Taurean Mauler")]
public static class TaureanMaulerFactory
{
    public const string CardName = "Taurean Mauler";
    public const string Slug = "taurean-mauler";

    /// <summary>
    /// Construct Taurean Mauler with no live wiring. Changeling subtype
    /// stamping + the opponent-cast trigger are attached to the card shape; the
    /// trigger is not registered (no trigger manager). Suitable for dispatcher
    /// / structural tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Taurean Mauler with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager — when supplied the opponent-cast
    /// trigger is registered so a matching <see cref="SpellCastEvent"/>
    /// surfaces as pending. May be null — the trigger is still attached to the
    /// card shape.</param>
    /// <param name="replacements">Replacement bus — routes the +1/+1 counter
    /// placement so CR 614 replacements (Hardened Scales / Doubling Season) can
    /// rewrite the count. May be null.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Shapeshifter, {2}{R}, 2/2). The Changeling subtype stamping + the
        // opponent-cast trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.73a — Changeling: this card is every creature type. Stamp the
        // engine's currently-modelled creature subtype set on the printed body
        // so HasSubtype(Goblin), HasSubtype(Elf), etc. all return true
        // everywhere. Sourced from MutavaultAnimateEffect.EveryCreatureType so
        // the changeling list stays in lockstep (same posture as
        // UnsettledMarinerFactory). Shapeshifter (printed) is already present
        // from the JSON; dedupe against it.
        foreach (var st in MutavaultAnimateEffect.EveryCreatureType)
        {
            if (card.HasSubtype(st)) continue; // dedupe (incl. Shapeshifter)
            card.AddSubtype(st);
        }

        // Changeling keyword marker (CR 702.73) — observational; the subtype
        // stamping above drives tribal-lord interactions.
        card.AddAbility(new KeywordAbility("Changeling", card, owner));

        // ----------------------------------------------------------------
        // Opponent-cast growth trigger — CR 603.6a / 109.5 / 603.7.
        //   "Whenever an opponent casts a spell, you may put a +1/+1 counter
        //    on this creature."
        //
        // Fires on SpellCastEvent whose spell's controller is an OPPONENT of
        // this creature's controller (CR 109.5 — a player other than the
        // controller; asymmetric, same gate as Counterbalance). The "you may"
        // is auto-yes in v1 (the rational play). On resolution one +1/+1
        // counter is placed via CountersService.Add so CR 614 replacements
        // observe it.
        // ----------------------------------------------------------------
        var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            var caster = e.Spell.Controller;
            var controller = card.Controller ?? owner;
            return caster != null && !ReferenceEquals(caster, controller);
        });

        var growEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on this creature",
            () => CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements));

        var growTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { growEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(growTrigger);
        triggers?.RegisterTriggeredAbility(growTrigger);

        return card;
    }
}
