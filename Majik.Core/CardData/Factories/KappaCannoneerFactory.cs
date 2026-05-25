using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kappa Cannoneer (Commander Legends: Battle for
/// Baldur's Gate, {5}{U}).
///
/// Artifact Creature — Turtle Warrior 4/4. Oracle text:
///   "Improvise (Your artifacts can help cast this spell. Each artifact
///    you tap after you're done activating mana abilities pays for {1}.)
///    Ward {4}
///    Whenever this creature or another artifact you control enters,
///    put a +1/+1 counter on this creature. It can't be blocked this
///    turn."
///
/// ## Implemented (v1)
///
/// - 4/4 <b>Artifact Creature</b> — Turtle Warrior at {5}{U}. The base
///   <see cref="Creature"/> constructor only registers
///   <see cref="CardType.Creature"/>; the Artifact type is additively
///   flagged via <c>AddCardType(CardType.Artifact)</c> (mirrors Scion of
///   Draco / Esika's Chariot / Wurmcoil Engine's multi-type shape).
///
/// - <b>Improvise (CR 702.127)</b>: wired as a
///   <see cref="KeywordAbility"/> marker PLUS the working cost-side
///   primitive — <see cref="BuildAdditionalCost"/> builds an
///   <see cref="ImproviseAdditionalCost"/> bound to the caller-selected
///   untapped artifacts. The cast flow's CR 601.2f additional-cost loop
///   taps the chosen artifacts and the post-improvise generic reduction
///   folds into the mana payment (see
///   <see cref="Majik.Core.Game.SpellCastFlow"/>). The
///   <see cref="Majik.Core.Players.Agents.ImproviseAltCostProbe"/>
///   surfaces this on the bot-discovery rail.
///
/// - <b>Ward {4} (CR 702.21)</b>: wired as a
///   <see cref="KeywordAbility"/> marker. The
///   <see cref="WardEffect"/> trigger helper exists as a stand-alone
///   check (callers invoke <c>ResolvesWard</c> from the spell-resolution
///   path) but there is no battlefield-attached Ward trigger primitive
///   yet, so the marker is structural-only — same posture as the
///   Improvise marker. <see cref="BuildWardEffect"/> returns a
///   <see cref="WardEffect"/> instance bound to the live card so the
///   spell-resolve path can opt-in once the wiring lands.
///
/// - <b>ETB / artifact-ETB trigger (CR 603.1 / 603.6a)</b>: wired via
///   <see cref="EventTriggerCondition{T}"/> over
///   <see cref="CardMovedEvent"/> filtered to:
///     1. <c>ToZone == Battlefield</c>
///     2. The entering card has <see cref="CardType.Artifact"/> (covers
///        both <em>this creature</em> entering — Kappa is itself an
///        Artifact Creature, satisfying the "or" clause — and any other
///        Artifact entering under the same controller).
///     3. The entering card's controller equals Kappa's controller
///        ("you control" wording, CR 109.5).
///   On resolution: add a <see cref="CounterType.PlusOnePlusOne"/>
///   counter to Kappa (CR 122.1c) and register an EOT-expiring
///   <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotBeBlocked"/> scoped to Kappa
///   (CR 702.x / CR 509.1c) when a
///   <see cref="ContinuousEffectsService"/> is supplied (the
///   restriction silently no-ops on the shape-only path).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Ward {4} trigger wiring</b>: <see cref="WardEffect"/> is a
///   standalone check helper, not yet plumbed onto a
///   battlefield-attached triggered ability. v1 ships the marker +
///   <see cref="BuildWardEffect"/> builder; the spell-resolution path
///   gains the Ward consultation in a separate PR.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. The ETB trigger is
///   attached so structural / dispatcher tests observe it; the EOT
///   "can't be blocked" rider no-ops without a continuous-effects
///   service. Suitable for shape / dispatcher tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ContinuousEffectsService?)"/>
///   — fully wired. ETB trigger registered when
///   <paramref name="triggers"/> is supplied; "can't be blocked this
///   turn" rider registers against
///   <paramref name="continuousEffects"/> when supplied.
/// </summary>
[CardName("Kappa Cannoneer")]
public static class KappaCannoneerFactory
{
    public const string CardName = "Kappa Cannoneer";
    public const string PrintedManaCost = "{5}{U}";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>CR 702.21 — printed Ward cost: {4}.</summary>
    public const string WardCost = "{4}";

    /// <summary>
    /// CR 702.127 — build the Improvise additional cost for this Kappa
    /// Cannoneer spell with the caller-selected untapped artifacts. The
    /// caller threads the returned cost through
    /// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>'s
    /// <c>additionalCosts</c> parameter; the cast flow taps the chosen
    /// artifacts and folds {1} of generic reduction per tap into the mana
    /// payment (coloured pips preserved per CR 702.127). Tests + bots
    /// pre-select the artifact list, mirroring the deferred agent prompt
    /// pattern used for <see cref="DelveCost"/>.
    /// </summary>
    public static ImproviseAdditionalCost BuildAdditionalCost(
        ICard card, IReadOnlyList<Permanent> tappedArtifacts) =>
        new(card, tappedArtifacts);

    /// <summary>
    /// CR 702.21 — Kappa Cannoneer's printed Ward {4} effect, bound to
    /// the supplied <paramref name="card"/>. v1 exposes this as a
    /// builder so the spell-resolution path can opt-in once the Ward
    /// trigger primitive lands (see class xmldoc for the deferred
    /// wiring gap).
    /// </summary>
    public static WardEffect BuildWardEffect(Creature card) =>
        new(card, ManaCost.Parse(WardCost));

    /// <summary>
    /// Construct Kappa Cannoneer with no live runtime wiring. The ETB
    /// trigger is attached to the card for shape observability; the
    /// "can't be blocked this turn" rider no-ops without a continuous-
    /// effects service. Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, continuousEffects: null, replacements: null);

    /// <summary>
    /// Construct Kappa Cannoneer with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Not consumed directly here; reserved for
    /// future Ward-trigger wiring + LTB cleanup hooks.</param>
    /// <param name="triggers">TriggerManager for the artifact-ETB
    /// trigger. May be null — the trigger is still attached to the
    /// card shape.</param>
    /// <param name="continuousEffects">ContinuousEffectsService for the
    /// EOT "can't be blocked this turn" rider. May be null — the rider
    /// is skipped on the shape-only path.</param>
    /// <param name="replacements">ReplacementBus for routing the +1/+1
    /// counter placement through <see cref="CountersService.Add"/> so
    /// Hardened Scales / Doubling Season replacements can rewrite the
    /// count (CR 614). May be null — the counter is placed directly.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? continuousEffects,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Turtle, CardSubtype.Warrior });

        // CR 301.1 / 302.1 — Kappa Cannoneer is an Artifact Creature. The
        // base Creature constructor only registers CardType.Creature, so
        // additively flag the Artifact type for HasType-based lookups
        // (mirrors Scion of Draco / Esika's Chariot's multi-type shape).
        // This also makes Kappa's own ETB satisfy the "or another
        // artifact" branch of its own trigger predicate.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Improvise (CR 702.127) — marker keyword + working cost-side
        // primitive. The marker keeps the discovery surface uniform with
        // TreasureCruise's Delve / ChordOfCalling's Convoke (probes scan
        // for KeywordAbility "Improvise"); the actual cost-reduction
        // wiring is supplied by BuildAdditionalCost above, which the
        // caster (test, bot, or future agent prompt) threads through the
        // SpellCastFlow additional-cost loop with a pre-selected list of
        // untapped artifacts to tap.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Improvise", card, owner));

        // ----------------------------------------------------------------
        // Ward {4} (CR 702.21) — marker keyword. WardEffect exists as a
        // standalone helper (BuildWardEffect bounds an instance to the
        // live card) but the battlefield-attached triggered-ability
        // surface is deferred — see class xmldoc.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Ward", card, owner));

        // ----------------------------------------------------------------
        // Artifact-ETB trigger — CR 603.1 / CR 603.6a.
        //   "Whenever this creature or another artifact you control
        //    enters, put a +1/+1 counter on this creature. It can't be
        //    blocked this turn."
        //
        // Predicate:
        //   - ToZone is Battlefield.
        //   - Entering card has CardType.Artifact (catches Kappa itself
        //     because Kappa is an Artifact Creature, satisfying the
        //     "this creature or another artifact" clause without needing
        //     a separate self-ETB branch).
        //   - Entering card's controller is Kappa's controller.
        //
        // Effect:
        //   - Add 1 PlusOnePlusOne counter to Kappa (CR 122.1c).
        //   - Register EOT-expiring CombatRestrictionEffect (CannotBe-
        //     Blocked, target = Kappa) when a continuous-effects service
        //     is supplied; no-op on the shape-only path.
        //
        // Active only while Kappa is on the battlefield (activeZones
        // gate).
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            e.ToZone == ZoneType.Battlefield
            && e.Card.HasType(CardType.Artifact)
            && ReferenceEquals(e.Card.Controller, card.Controller ?? owner));

        var counterAndUnblockableEffect = new Effect(
            $"{CardName}: +1/+1 counter + can't be blocked this turn",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;

                // CR 122.1c — counter placement (routed through
                // CountersService so Hardened Scales / Doubling Season
                // replacements observe the intent — CR 614).
                CountersService.Add(card, CounterType.PlusOnePlusOne, 1, replacements);

                // CR 702.x — "can't be blocked this turn" registered as a
                // per-turn combat restriction. Consulted by the combat
                // validator directly (Apply is a no-op for restrictions).
                // EOT cleanup runs via ContinuousEffectsService's
                // expiresAtEndOfTurn pipeline (CR 514.2).
                if (continuousEffects != null)
                {
                    continuousEffects.Register(new CombatRestrictionEffect(
                        CombatRestriction.CannotBeBlocked,
                        target: card,
                        expiresAtEndOfTurn: true));
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { counterAndUnblockableEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
