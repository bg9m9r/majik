using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Endless One (Battle for Zendikar, {X}).
///
/// Creature — Eldrazi 0/0. Oracle text (Scryfall, verified):
///   "Endless One enters with X +1/+1 counters on it."
///
/// ## Implemented (v1)
/// - 0/0 Creature — Eldrazi at {X} (colourless generic — Endless One
///   itself is colourless per CR 105 because the printed cost has no
///   coloured pips).
/// - <b>ETB +1/+1 counters trigger (CR 603.6a / CR 122.1g)</b>: on
///   entering the battlefield, places X <see cref="CounterType.PlusOnePlusOne"/>
///   counters on Endless One. X is read from <see cref="Card.PendingCastX"/>,
///   stamped by <see cref="Majik.Core.Game.SpellCastFlow"/> at cast time
///   right after the caster's <c>ChooseXAsync</c>. The stamp is consumed
///   (cleared) so a later non-cast battlefield entry (blink, copy)
///   doesn't reuse it — such an entry leaves Endless One with zero
///   counters, matching the printed behaviour for an Endless One that
///   didn't come in via a real X cast (the SBA pass per CR 704.5f
///   immediately puts it in the graveyard as a 0/0).
/// - Counter placement routes through <see cref="CountersService.Add"/>
///   when a <see cref="ReplacementBus"/> is supplied so Hardened Scales /
///   Doubling Season replacements rewrite the amount before it commits
///   (CR 614 / CR 121.2). When no bus is supplied the call falls through
///   to a direct add (same posture as Champion of the Parish's no-bus
///   path).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. ETB trigger attached for
///   shape observability; not registered with any
///   <see cref="TriggerManager"/>; counter placement uses the direct
///   <see cref="CountersService.Add"/> fallthrough (no replacement-bus
///   rewrites). Suitable for shape / dispatcher tests.
/// - <see cref="Create(Player, TriggerManager?, ReplacementBus?)"/> —
///   fully wired. Trigger registers with <paramref name="triggers"/>;
///   counter placement routes through <paramref name="replacements"/>.
///
/// ## Notes
/// - <b>Strict 122.1g timing</b>: counters should be placed as Endless
///   One enters (CR 122.1g "with") rather than via an ETB trigger that
///   puts an ability on the stack. The v1 impl uses an ETB-trigger
///   effect for the same reason Chalice of the Void does — no general
///   122.1g replacement-effect surface yet that threads chosen-X
///   through <see cref="ZoneMoveIntent"/> — and the observable end
///   state is identical for the test matrix here. The existing
///   <see cref="EntersWithCountersReplacement"/> handles fixed
///   counter-amount cards (Strangleroot Geist's "enters with a +1/+1
///   counter") but doesn't yet thread <c>ChosenSpellParams.X</c>
///   through the intent — same gap Walking Ballista documents.
/// </summary>
[CardName("Endless One")]
public static class EndlessOneFactory
{
    public const string CardName = "Endless One";
    public const string PrintedManaCost = "{X}";
    public const int Power = 0;
    public const int Toughness = 0;

    /// <summary>
    /// Construct Endless One with no live wiring. The ETB-counters trigger
    /// is attached for shape observability; not registered with any
    /// <see cref="TriggerManager"/>; counter placement uses the direct
    /// <see cref="CountersService.Add"/> fallthrough. Suitable for shape
    /// / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Endless One with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager. When supplied the ETB
    /// counter-placement trigger registers so a self-enter
    /// <see cref="CardMovedEvent"/> automatically queues the trigger
    /// (CR 603.2).</param>
    /// <param name="replacements">Replacement bus. When supplied the
    /// counter placement routes through <see cref="CountersService.Add"/>
    /// so Hardened Scales / Doubling Season can rewrite the count
    /// (CR 614).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Eldrazi });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB +1/+1 counters trigger — CR 603.6a / CR 122.1g.
        //   "Endless One enters with X +1/+1 counters on it."
        // v1 folds 122.1g "as it enters with N counters" into the ETB
        // trigger effect: read PendingCastX (stamped by SpellCastFlow
        // right after ChooseXAsync), apply that many +1/+1 counters via
        // CountersService.Add (so Hardened Scales / Doubling Season
        // replacements re-write the amount), then clear the stamp so
        // re-entries (blink, copy) don't reuse the value. PendingCastX
        // is null for non-cast entries → 0 counters → 0/0 → SBA puts it
        // in the graveyard (CR 704.5f), matching the printed behaviour.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: enters with X +1/+1 counters (CR 122.1g)",
            () =>
            {
                var x = card.PendingCastX ?? 0;
                if (x > 0)
                {
                    // CR 614 — routes through ReplacementBus so Hardened
                    // Scales bumps + Doubling Season doubles apply (and
                    // stack — Hardened-then-Doubling rewrites X +1 then
                    // 2*(X+1) per CR 614.1 ordering).
                    CountersService.Add(card, CounterType.PlusOnePlusOne, x, replacements);
                }
                card.ClearPendingCastX();
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
