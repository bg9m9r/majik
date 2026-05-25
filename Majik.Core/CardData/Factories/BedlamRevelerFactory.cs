using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bedlam Reveler (Eldritch Moon, {6}{R}{R}).
///
/// Creature — Horror 3/4. Oracle text:
///   "Trample
///    Prowess (Whenever you cast a noncreature spell, this creature gets
///    +1/+1 until end of turn.)
///    This spell costs {1} less to cast for each instant and sorcery card
///    in your graveyard.
///    When this creature enters, if you cast it from your hand, discard
///    your hand, then draw three cards."
///
/// ## Implemented (v1)
///
/// - 3/4 Creature — Horror at {6}{R}{R}; owner / controller wired.
/// - <b>Trample</b> (CR 702.19) wired as a <see cref="KeywordAbility"/>
///   marker; combat helpers (<c>CombatAbilities.HasTrample</c>) read it the
///   same way every other trample-bearing factory in this repo does (Amped
///   Raptor, Hogaak, Primeval Titan, …).
/// - <b>Prowess</b> (CR 702.108) wired via <see cref="ProwessFactory.Build"/>
///   when a <see cref="ContinuousEffectsService"/> is supplied — mirrors
///   <see cref="MonasterySwiftspearFactory"/>'s prowess wire-up. A
///   <see cref="KeywordAbility"/>("Prowess") marker is also attached for
///   shape-only keyword discovery (dispatcher tests, bot keyword scans).
///   Shape-only paths omit the trigger but keep the keyword marker, same
///   posture as Monastery Swiftspear.
/// - <b>Self cost reduction (CR 117.7)</b>: <see cref="CostReductionAbility"/>
///   in <see cref="CostReductionAbility.TotalReducer"/> shape — counts
///   instant + sorcery cards in the caster's graveyard at cost-calc time
///   and reduces the generic mana bucket by that count. Coloured pips
///   ({R}{R}) are untouched per CR 117.7c and the reducer floors at zero
///   inside <see cref="CostReduction.GetEffectiveCost"/>. Mirrors the
///   shape used by <see cref="DemilichFactory"/>'s "{U} less per
///   instant/sorcery" reducer — Bedlam Reveler swaps the {U}-pip
///   reduction for plain {1} generic reduction.
///     - 0 in graveyard → {6}{R}{R} (generic = 6, red pips = 2)
///     - 5 in graveyard → {1}{R}{R} (generic = 1, red pips = 2)
///     - 8 in graveyard → still {R}{R} (floors at coloured pips)
/// - <b>ETB triggered ability (CR 603.6a, CR 603.6e intervening-if)</b>:
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> condition with the
///   intervening-if predicate gated on <see cref="Card.WasCastFromHand"/>
///   — the strict "cast from your hand" sentinel stamped by
///   <see cref="Majik.Core.Game.SpellCastFlow"/> when the source zone
///   was <see cref="ZoneType.Hand"/>. Distinct from <see cref="Card.WasCast"/>
///   which fires on any cast (flashback / suspend / from-exile /
///   from-graveyard included); Bedlam Reveler's printed wording
///   specifically excludes those non-hand cast paths. The ETB effect
///   runs in two phases (CR 121.4 — "then" sequences both halves as a
///   single instruction):
///     1. <b>Discard your hand</b> (CR 701.16) — every card currently in
///        the controller's hand is moved Hand → Graveyard. The Reveler
///        itself is already on the battlefield at ETB resolution time
///        (CR 603.6a — ETB triggers wait until after the spell resolves
///        and the permanent has entered), so it's never in the
///        "your hand" discard pool.
///     2. <b>Draw three cards</b> (CR 121.1) — three top-of-library draws
///        with the empty-library CR 704.5b SBA loss flag set via
///        <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> the same
///        way every other multi-draw factory handles short libraries
///        (Faithless Looting, Brainstorm, Vault Skirge, …).
///   Active zone is <see cref="ZoneType.Battlefield"/> — ETB triggers
///   resolve after the permanent has entered. Trigger source is the
///   card itself, controller is the cast-time caster (passed by the
///   owner argument).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — no live wiring. Trample marker +
///   Prowess marker + cost-reduction ability + ETB trigger are attached
///   for shape inspection. Prowess trigger is NOT wired (no continuous-
///   effects service). The ETB trigger has no <see cref="TriggerManager"/>
///   registration. Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ContinuousEffectsService?, ZoneService?)"/>
///   — fully wired. The ETB trigger is registered when
///   <paramref name="triggers"/> is supplied; the Prowess trigger is
///   wired when <paramref name="effects"/> is supplied. Discard +
///   draw zone moves route through <paramref name="zones"/> when
///   supplied so <see cref="CardMovedEvent"/> publishes for any
///   zone-change subscribers (Bridge from Below's Zombie trigger,
///   Containment Priest, Tormod's Crypt, …). Raw zone manipulation
///   otherwise — same two-mode posture as <see cref="DemilichFactory"/>.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Tracked source zone for non-hand casts</b>: the
///   <see cref="Card.WasCastFromHand"/> sentinel covers Bedlam Reveler's
///   intervening-if as printed. Future cards keyed on "if you cast it
///   from your graveyard" / "if you cast it from exile" can layer
///   their own sentinels onto the same SpellCastFlow capture point
///   (the source zone is snapshot before the Stack → Battlefield move).
/// - <b>Token / blink ETB suppression</b>: the intervening-if is
///   strictly cast-gated; a Bedlam Reveler reanimated via Goryo's
///   Vengeance / blinked via Conjurer's Closet / token-copied via
///   Mirror Gallery sees <c>WasCastFromHand == false</c> and the
///   trigger silently no-ops on the discard + draw. This matches the
///   printed wording exactly.
/// </summary>
[CardName("Bedlam Reveler")]
public static class BedlamRevelerFactory
{
    public const string CardName = "Bedlam Reveler";
    public const string PrintedManaCost = "{6}{R}{R}";
    public const int Power = 3;
    public const int Toughness = 4;
    public const int DrawCount = 3;

    /// <summary>
    /// Construct Bedlam Reveler with no live wiring. Trample + Prowess
    /// keyword markers, cost-reduction ability, and the ETB trigger are
    /// attached for shape observability. Prowess trigger is NOT wired
    /// (no <see cref="ContinuousEffectsService"/>); ETB trigger is NOT
    /// registered with a <see cref="TriggerManager"/>; discard + draw
    /// moves use raw zone manipulation. Suitable for dispatcher /
    /// structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, effects: null, zones: null);

    /// <summary>
    /// Construct Bedlam Reveler with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Reserved for future lifecycle subscribers
    /// (LTB unregister, etc.). Not used directly by this factory today.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers
    /// so a <see cref="Majik.Core.Events.CardMovedEvent"/> Stack →
    /// Battlefield for this card lands the trigger on the stack
    /// automatically (CR 603.6a).</param>
    /// <param name="effects">When supplied, the Prowess trigger
    /// (CR 702.108) is wired via <see cref="ProwessFactory.Build"/>
    /// and registered with <paramref name="triggers"/> when both are
    /// supplied. Null leaves the Prowess trigger unwired (the keyword
    /// marker still attaches).</param>
    /// <param name="zones">When supplied, the discard + draw zone moves
    /// route through <see cref="ZoneService.MoveCard"/> so
    /// <see cref="CardMovedEvent"/> publishes for zone-change
    /// subscribers (Bridge from Below, Containment Priest, Tormod's
    /// Crypt, …). Raw zone manipulation otherwise.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Horror });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.19 — Trample. KeywordAbility marker; combat helpers
        // (CombatAbilities.HasTrample) read it the same way they do for
        // every other trample-bearing factory.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // ----------------------------------------------------------------
        // CR 702.108 — Prowess. KeywordAbility marker for shape-only
        // keyword discovery (dispatcher tests, bot keyword scans). The
        // marker is independent of the actual trigger wiring below —
        // same posture as Monastery Swiftspear's printed Prowess.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Prowess", card, owner));

        // Prowess mechanic — Whenever you cast a noncreature spell, this
        // creature gets +1/+1 until end of turn. Wired via
        // ProwessFactory.Build when a ContinuousEffectsService is
        // supplied; the prowess ability is registered with the trigger
        // manager when both services are provided. When effects == null
        // the prowess trigger is not wired (shape-only path keeps the
        // card lean — same posture as Monastery Swiftspear).
        if (effects != null)
        {
            card.ActiveEffects = effects;
            var prowessTrigger = ProwessFactory.Build(card, effects);
            card.AddAbility(prowessTrigger);
            triggers?.RegisterTriggeredAbility(prowessTrigger);
        }

        // ----------------------------------------------------------------
        // CR 117.7 — "This spell costs {1} less to cast for each instant
        // and sorcery card in your graveyard." Whole-reduction shape
        // (CostReductionAbility(totalReducer)) — the function counts
        // instants/sorceries in the caster's graveyard at cost-calc time.
        // CR 117.7c — cost cannot drive coloured pips below printed; the
        // floor at zero on generic mana is enforced inside
        // CostReduction.GetEffectiveCost, so the two {R} pips remain
        // regardless of graveyard size. Same shape as Demilich's reducer
        // (which keys on {U} pips); Bedlam Reveler reduces plain generic.
        // ----------------------------------------------------------------
        card.AddAbility(new CostReductionAbility(
            totalReducer: caster =>
            {
                if (caster?.Zones?.Graveyard == null) return 0;
                var n = 0;
                foreach (var g in caster.Zones.Graveyard.GetCards())
                {
                    if (g.HasType(CardType.Instant) || g.HasType(CardType.Sorcery)) n++;
                }
                return n;
            },
            description:
                "This spell costs {1} less to cast for each instant and " +
                "sorcery card in your graveyard."));

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.6a, CR 603.6e (intervening-if).
        //   "When this creature enters, if you cast it from your hand,
        //    discard your hand, then draw three cards."
        // The intervening-if gates on Card.WasCastFromHand — stamped by
        // SpellCastFlow when the source zone was Hand. Distinct from
        // Card.WasCast which fires on any cast path. Reanimation / blink
        // / token-copy paths leave WasCastFromHand == false → the ETB
        // resolves but the effect body short-circuits (the intervening-if
        // returns false). Active zone is Battlefield — ETB triggers
        // resolve after the permanent has entered (CR 603.6a).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: discard your hand, then draw {DrawCount} cards (if cast from hand)",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 603.6e — re-check the intervening-if at resolve
                // time. Defensive: TriggeredAbility.CanBePutOnStack
                // already vetted this when the trigger was queued, but
                // the rule re-checks at resolution and a stale stamp
                // (cleared by an unrelated battlefield exit / re-cast
                // race) should short-circuit cleanly here too.
                if (!card.WasCastFromHand) return;

                // CR 701.16 — "discard your hand". Snapshot the hand list
                // before mutation to avoid the collection-modified-during-
                // enumeration trap. Each card moves Hand → Graveyard via
                // ZoneService when supplied (so CardMovedEvent publishes
                // for Bridge from Below / Containment Priest / Tormod's
                // Crypt subscribers), raw zone manipulation otherwise.
                var hand = controller.Zones.Hand.GetCards().ToList();
                foreach (var c in hand)
                {
                    if (zones != null)
                    {
                        zones.MoveCard(c, ZoneType.Hand, ZoneType.Graveyard);
                    }
                    else
                    {
                        controller.Zones.Hand.RemoveCard(c);
                        controller.Zones.Graveyard.AddCard(c);
                        if (c is Card concreteHandCard)
                        {
                            concreteHandCard.SetZone(ZoneType.Graveyard);
                        }
                    }
                }

                // CR 121.1 — "then draw three cards". Three top-of-library
                // draws. Empty library mid-draw flags the SBA loss flag
                // (CR 704.5b) via MarkTriedToDrawFromEmptyLibrary and
                // short-circuits the remaining draws — same handling as
                // Faithless Looting / Wrenn's Resolve / Brainstorm.
                for (var i = 0; i < DrawCount; i++)
                {
                    var top = controller.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null)
                    {
                        controller.MarkTriedToDrawFromEmptyLibrary();
                        break;
                    }
                    if (zones != null)
                    {
                        zones.MoveCard(top, ZoneType.Library, ZoneType.Hand);
                    }
                    else
                    {
                        controller.Zones.Library.RemoveCard(top);
                        controller.Zones.Hand.AddCard(top);
                        if (top is Card concreteTop)
                        {
                            concreteTop.SetZone(ZoneType.Hand);
                        }
                    }
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            // CR 603.6e — intervening-if checks at both queue time
            // (CanBePutOnStack) and resolve time (the inline guard above).
            // Queue-time check uses the live Card.WasCastFromHand stamp.
            interveningIf: () => card.WasCastFromHand,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
