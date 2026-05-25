using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Eldrazi Mimic (Oath of the Gatewatch, {2}).
///
/// Creature — Eldrazi 2/1. Oracle text (Scryfall, verified):
///   "Whenever another colorless creature with mana value 4 or greater enters
///    under your control, you may have Eldrazi Mimic's base power and
///    toughness become that creature's power and toughness until end of turn."
///
/// ## Implemented (v1)
/// - 2/1 Creature — Eldrazi at {2} (colourless generic — Mimic itself is
///   colourless per CR 105 because the printed cost has no coloured pips).
/// - <b>ETB-other-creature trigger (CR 603.1)</b>: an
///   <see cref="EventTriggerCondition{TEvent}"/> over <see cref="CardMovedEvent"/>
///   to the Battlefield with the predicate stack:
///   <list type="bullet">
///     <item>The entering card is a <see cref="CardType.Creature"/>.</item>
///     <item>The entering card is colourless (CR 105 / CR 202.2 — empty colour
///           set on <see cref="CardColors.GetColors"/>).</item>
///     <item>The entering card's mana value is &gt;= 4 (printed mv via
///           <see cref="ManaCost.TotalValue"/>; Mimic's "with mana value 4 or
///           greater" reads the entering card's mana value, which honours
///           PendingCastX for variable-X spells via the same shape Chalice
///           of the Void's MV comparison uses).</item>
///     <item>The entering card's controller is Mimic's controller
///           (printed "under your control" — CR 109.3 / CR 603.1).</item>
///     <item>The entering card is NOT Mimic itself ("another" per
///           CR 603.1 — the Mimic's own ETB never triggers itself).</item>
///   </list>
///   On resolution, if a <see cref="ContinuousEffectsService"/> is wired and
///   the source still on the battlefield (CR 608.2b illegal-on-resolution
///   gate), the factory registers a
///   <see cref="BecomesPTUntilEndOfTurnEffect"/> (CR 613.7b Layer 7b
///   set-base P/T, EOT-expirable per CR 514.2) carrying the entering
///   creature's current power and toughness at trigger-resolve time
///   (printed wording reads the source's <em>power and toughness</em>, not
///   base — so the snapshot honours buffs already on the source). Mimic's
///   own +1/+1 counters and Layer 7c pumps stack on top of the new base
///   per the layer system.
/// - "You may" — v1 auto-accepts (always copies the P/T when an eligible
///   source enters). Same posture as Wurmcoil Engine's mandatory two-token
///   creation when "may" is replaced by a yes/no prompt the engine doesn't
///   yet surface for trigger resolution.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Trigger attached for shape
///   observability; not registered with any <see cref="TriggerManager"/> and
///   the resolve effect closes over a null <see cref="ContinuousEffectsService"/>
///   (no P/T copy at resolve). Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, ContinuousEffectsService?, TriggerManager?)"/>
///   — fully wired. Trigger registers with <paramref name="triggers"/>;
///   <paramref name="effects"/> hosts the Layer-7b set-base effect on resolve.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompt</b>: auto-accepts (always copies on a legal trigger).
///   Same gap as every other "you may" triggered ability today — the
///   yes/no agent prompt for trigger resolution doesn't surface yet.
/// - <b>Source-leaves-pre-resolve fizzle</b>: the printed text doesn't require
///   the entering creature to still be on the battlefield at resolve time
///   (the trigger reads its P/T then), so a snapshot at trigger-creation
///   would be ideal. v1 reads the live P/T at resolve and gates on the
///   source still being on the battlefield (CR 608.2b illegal-on-resolution
///   guard); if the source has left between trigger queue and resolution
///   the rider no-ops. This is conservative — the printed rules permit the
///   copy because the entering creature's P/T is well-defined at trigger
///   time. A future PR can capture the P/T at trigger-creation in the
///   condition predicate's closure to remove this gap.
/// </summary>
[CardName("Eldrazi Mimic")]
public static class EldraziMimicFactory
{
    public const string CardName = "Eldrazi Mimic";
    public const string PrintedManaCost = "{2}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Eldrazi Mimic with no live wiring. The ETB-other-colourless
    /// trigger is attached for shape observability; the resolve effect closes
    /// over a null <see cref="ContinuousEffectsService"/> so no P/T copy
    /// fires. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, triggers: null);

    /// <summary>
    /// Construct Eldrazi Mimic with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service hosting the Layer-7b
    /// set-base P/T rider. May be null — the trigger still attaches but the
    /// resolve effect no-ops cleanly.</param>
    /// <param name="triggers">Trigger manager. When supplied the
    /// ETB-other-colourless trigger registers so a qualifying
    /// <see cref="CardMovedEvent"/> automatically queues the trigger
    /// (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        TriggerManager? triggers)
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
        // ETB-other-colourless trigger — CR 603.1.
        //   "Whenever another colorless creature with mana value 4 or
        //    greater enters under your control, you may have Eldrazi
        //    Mimic's base power and toughness become that creature's
        //    power and toughness until end of turn."
        //
        // We capture the entering creature in a per-trigger slot at
        // predicate-match time so the resolve effect knows which P/T to
        // copy. Multiple back-to-back enters each create independent
        // trigger instances via the TriggerManager — the slot is per-
        // condition-evaluation, snapped into the effect closure.
        // ----------------------------------------------------------------
        Creature? lastEntered = null;

        var etbCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (!e.Card.HasType(CardType.Creature)) return false;
            if (ReferenceEquals(e.Card, card)) return false; // "another"
            if (!ReferenceEquals(e.Card.Controller, card.Controller)) return false;

            // CR 105 — colourless: no coloured pips. CardColors.GetColors
            // honours TokenColorsOverride for explicit-colourless tokens.
            if (CardColors.GetColors(e.Card).Count != 0) return false;

            // CR 202.3b — mana value of the entering card.
            // ManaCostValue includes the printed numeric + colored pip
            // contributions; PendingCastX (for variable-X spells) is the
            // chosen X, additive to the printed mv. The card may have
            // already cleared PendingCastX by the time we read it in some
            // resolution orderings, but the cost-side TotalValue is the
            // safe lower-bound read.
            var printedMv = e.Card is Card cc
                ? cc.ManaCostValue.TotalValue
                : ManaCost.Parse(e.Card.ManaCost ?? string.Empty).TotalValue;
            var x = (e.Card as Card)?.PendingCastX ?? 0;
            if (printedMv + x < 4) return false;

            // Predicate passed — capture the entering creature for the
            // resolve closure. Down-cast safe because HasType(Creature)
            // gated above.
            lastEntered = e.Card as Creature;
            return lastEntered != null;
        });

        var copyEffect = new Effect(
            $"{CardName}: base P/T becomes that creature's P/T until end of turn",
            () =>
            {
                var src = lastEntered;
                lastEntered = null; // consume — next trigger captures afresh
                if (src == null) return;
                if (effects == null) return; // shape-only path

                // CR 608.2b — illegal-on-resolution: Mimic must still be on
                // the battlefield to receive the rider; if the source has
                // left between trigger queue and resolve the snapshot is
                // moot too.
                if (card.Zone != ZoneType.Battlefield) return;
                if (src.Zone != ZoneType.Battlefield) return;

                // Read the source's CURRENT P/T (printed reads "power and
                // toughness", not base — so continuous-effect riders on
                // the source contribute to the copied numbers).
                var p = src.Power;
                var t = src.Toughness;

                effects.Register(new BecomesPTUntilEndOfTurnEffect(card, p, t));
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { copyEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
