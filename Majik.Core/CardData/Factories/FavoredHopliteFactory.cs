using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Favored Hoplite (Theros, {W}).
///
/// Creature — Human Soldier 1/2. Oracle text:
///   "Heroic — Whenever you cast a spell that targets Favored Hoplite,
///    prevent all damage that would be dealt to Favored Hoplite this
///    turn and put a +1/+1 counter on it."
///
/// ## Implemented (v1)
///
/// - 1/2 Creature — Human Soldier at {W}, owner/controller wired.
/// - <b>Heroic trigger (CR 702.85 / CR 603.1)</b> — fires on
///   <see cref="SpellCastEvent"/> whose
///   <see cref="Majik.Core.Spells.ISpell.Controller"/> matches Hoplite's
///   controller AND whose <see cref="Majik.Core.Spells.ISpell.Targets"/>
///   list contains a target referencing Hoplite itself (CR 115.6 —
///   a spell "targets" a permanent when that permanent appears in the
///   spell's chosen-target list). On resolution the effect:
///     1. Registers a <see cref="PreventAllDamageToCreatureShield"/>
///        for Hoplite on the supplied <see cref="ReplacementBus"/>; the
///        shield is EOT-expirable (CR 615 / cleanup step), so subsequent
///        Heroic triggers within the same turn stack additional shields
///        but each individually drops at cleanup.
///     2. Places one +1/+1 counter on Hoplite via
///        <see cref="CountersService.Add"/> (CR 122.1c), routing through
///        the replacement bus so Hardened Scales / Doubling Season can
///        scale the count (CR 614).
///
/// ## Heroic semantics (CR 702.85)
///
/// - <b>"Spell you cast"</b>: spell controller must match Hoplite's
///   controller (CR 109.5).
/// - <b>"Targets Favored Hoplite"</b>: at least one element of
///   <see cref="Majik.Core.Spells.ISpell.Targets"/> resolves to Hoplite.
///   The target list is populated by <see cref="SpellCaster"/> /
///   <see cref="Majik.Core.Game.SpellCastFlow"/> at cast time (601.2c),
///   and SpellCastEvent fires after target selection — so the targets
///   list is fully populated when the trigger evaluates.
/// - <b>Self-cast</b>: casting Hoplite itself does NOT trigger Heroic.
///   The trigger is only active while Hoplite is on the battlefield
///   (CR 603.6a — characteristic ETB-style cast trigger;
///   <c>activeZones = {Battlefield}</c>) and Hoplite is on the stack
///   when its own cast event fires. Matches Bygone Bishop / Talrand
///   posture.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. Trigger attached for
///   dispatcher visibility but not registered with a
///   <see cref="TriggerManager"/>; the damage-prevention shield is not
///   placed (no replacement bus). Suitable for shape / dispatch tests.
/// - <see cref="Create(Player, TriggerManager?, ReplacementBus?)"/> —
///   fully wired. When <paramref name="triggers"/> is supplied the
///   Heroic trigger surfaces on a matching SpellCastEvent; when
///   <paramref name="replacements"/> is supplied the prevention shield
///   and the counter placement both route through the bus.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Multiple-target spells</b>: a spell that targets Hoplite twice
///   (e.g. a hypothetical "Hoplite, Hoplite") still triggers Heroic
///   once per CR 702.85 ("Whenever you cast a spell that targets"
///   is a single event per cast). The current predicate stops at the
///   first match — correct posture for "any target is Hoplite".
/// - <b>Shield stacking</b>: the per-turn shield is a fresh instance per
///   trigger; if two Heroic triggers fire in the same turn two shields
///   register, but each only ever cancels intents independently — both
///   drop at cleanup. No measurable difference vs a "register once" idiom.
/// </summary>
[CardName("Favored Hoplite")]
public static class FavoredHopliteFactory
{
    public const string CardName = "Favored Hoplite";
    public const string PrintedManaCost = "{W}";
    public const int Power = 1;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Favored Hoplite with no live wiring. Heroic trigger is
    /// attached to the card for shape observability; no TriggerManager
    /// registration and no shield placement. Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Favored Hoplite with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the Heroic trigger is
    /// registered so a qualifying <see cref="SpellCastEvent"/>
    /// automatically queues the ability. When <paramref name="replacements"/>
    /// is supplied the EOT damage-prevention shield is placed on the bus
    /// and the +1/+1 counter routes through replacement effects.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Heroic trigger — CR 702.85 / CR 603.1.
        //   "Whenever you cast a spell that targets Favored Hoplite,
        //    prevent all damage that would be dealt to Favored Hoplite
        //    this turn and put a +1/+1 counter on it."
        // Predicate: spell.Controller is Hoplite's controller AND at least
        // one chosen target on the spell references Hoplite (CR 115.6).
        // ----------------------------------------------------------------
        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            if (!ReferenceEquals(e.Spell.Controller, card.Controller ?? owner)) return false;
            return SpellTargetsCreature(e.Spell.Targets, card);
        });

        var heroicEffect = new Effect(
            $"{CardName}: heroic — prevent all damage to it this turn and " +
            "put a +1/+1 counter on it",
            () =>
            {
                // CR 615 — register a per-turn damage-prevention shield
                // for Hoplite. Drops at cleanup via IEndOfTurnExpirable.
                replacements?.Register(new PreventAllDamageToCreatureShield(card));

                // CR 122.1c — place a +1/+1 counter. Route through the
                // replacement bus so Hardened Scales / Doubling Season can
                // rewrite the amount (CR 614).
                CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements);
            });

        var heroicTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { heroicEffect },
            // CR 603.6a — Heroic only active while Hoplite is on the
            // battlefield (also explains why casting Hoplite itself does
            // not trigger Heroic: Hoplite is on the stack at the moment
            // its own SpellCastEvent fires).
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(heroicTrigger);
        triggers?.RegisterTriggeredAbility(heroicTrigger);

        return card;
    }

    /// <summary>
    /// CR 115.6 — does any element of <paramref name="targets"/> refer to
    /// <paramref name="creature"/>? Exposed for tests; mirrors the closure
    /// baked into the live Heroic predicate. Walks every target rather
    /// than short-circuiting on TargetType because some
    /// <see cref="Target"/> shapes (e.g. test-only Card targets that
    /// happen to be permanents on the battlefield) classify as
    /// <see cref="TargetType.Card"/> rather than Permanent.
    /// </summary>
    public static bool SpellTargetsCreature(IReadOnlyList<ITarget> targets, Creature creature)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(creature);

        foreach (var t in targets)
        {
            if (t is not Target concrete) continue;
            if (ReferenceEquals(concrete.TargetObject, creature)) return true;
        }
        return false;
    }
}
