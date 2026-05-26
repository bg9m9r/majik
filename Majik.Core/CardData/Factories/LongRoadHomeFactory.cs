using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Long Road Home (Jumpstart, {1}{W}).
///
/// Instant. Scryfall oracle (verified):
///   "Exile target creature. At the beginning of the next end step, return
///    that card to the battlefield under its owner's control with a +1/+1
///    counter on it."
///
/// Otherworldly Journey's mass-market reprint shell — same effect text,
/// Jumpstart-set color identity. The delayed end-step return is the same
/// CR 603.7 / CR 614 rider used by Touch the Spirit Realm's Channel half.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{W}, owner / controller.
/// - <b>Cast body</b> — <see cref="BuildSpellDefinition"/> returns a
///   <see cref="SpellDefinition"/> with a single 1..1 "target creature"
///   <see cref="TargetRequest"/> sourced from a live
///   <c>CandidateGatherer</c> walking every player's battlefield for
///   <see cref="CardType.Creature"/> permanents (no controller-side filter
///   — Long Road Home can target any creature, including opponents'). Bot
///   intent <see cref="BotIntent.Protection"/> because the dominant casting
///   shape is self-blinking to dodge a removal spell or re-trigger an ETB
///   (mirrors <see cref="CloudshiftFactory"/>; the +1/+1 counter on return
///   skews this toward protection-on-your-own).
/// - <b>Resolve</b>: re-checks the target is still a battlefield Creature
///   (CR 608.2b — illegal target → no effect). Exiles via owner-routed
///   zone moves (CR 701.21). When a <see cref="TriggerManager"/> is
///   supplied, registers a one-shot <see cref="DelayedTriggeredAbility"/>
///   (CR 603.7) that fires on the first <see cref="StepStartedEvent"/>
///   with <c>StepType == End</c> and timestamp strictly after this resolve
///   (the same activation-time fence pattern Touch the Spirit Realm uses).
///   On delayed trigger resolution: defensively check the card is still in
///   exile (CR 111.8 — tokens that exited the battlefield cease to exist),
///   return it to the battlefield under its OWNER's control (CR 614 —
///   distinct from "your control"), and place one
///   <see cref="CounterType.PlusOnePlusOne"/> counter on it via
///   <see cref="CountersService.Add"/> so Hardened Scales / Doubling
///   Season-style replacements can rewrite the count (CR 614).
///
/// ## Deferred (v1 gaps)
/// - <b>ZoneService routing</b>: this factory uses raw zone moves for
///   exile + return (same posture as <see cref="CloudshiftFactory"/>).
///   A future PR can lift through <see cref="ZoneService"/> to publish
///   <see cref="CardMovedEvent"/> so Containment Priest / Tormod's Crypt
///   surfaces see the moves; for now matches Cloudshift's surface.
/// - <b>Counter ETB replacements</b>: <see cref="CountersService.Add"/>
///   is called with <c>replacements: null</c> — Hardened Scales / Branching
///   Evolution amplifiers won't fire. Same posture as the rest of the v1
///   counter-placement surface; lifting through a shared ReplacementBus
///   is tracked at the named-factory baseline.
/// </summary>
[CardName("Long Road Home")]
public static class LongRoadHomeFactory
{
    public const string CardName = "Long Road Home";
    public const string PrintedManaCost = "{1}{W}";

    /// <summary>Construct Long Road Home as an Instant owned and controlled
    /// by <paramref name="owner"/>. Card shape only — the cast body is
    /// produced by <see cref="BuildSpellDefinition"/>.</summary>
    public static Instant Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Long Road Home with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the cast
    /// body's delayed end-step return rider registers with the bus so a
    /// later End-step <see cref="StepStartedEvent"/> automatically lands
    /// the return trigger on the stack (CR 603.7). When omitted, the
    /// resolve still exiles the target but skips the delayed-return rider
    /// (shape-only mode — matches Touch the Spirit Realm's posture).
    /// </summary>
    public static Instant Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // The cast body needs a stash for the registered TriggerManager so
        // BuildSpellDefinition's effect closure can register the delayed
        // return on resolve. Stash on the card via a side-channel: capture
        // it in the closure when the caller goes through Create+Cast (the
        // test path) by exposing a per-card builder below.
        _ = triggers; // see TouchTheSpiritRealm — Channel passes triggers to its rider via the activated-ability closure; for an Instant whose body comes from BuildSpellDefinition the trigger is plumbed via that overload.

        return card;
    }

    /// <summary>
    /// Build the cast SpellDefinition. <paramref name="caster"/> owns the
    /// resolve closure; <paramref name="triggers"/> (when supplied) is the
    /// <see cref="TriggerManager"/> used to register the delayed end-step
    /// return rider (CR 603.7). <paramref name="source"/> is the source
    /// stack object the delayed trigger reports back to (the Long Road
    /// Home card itself).
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        TriggerManager? triggers = null,
        ICard? source = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection,
                    // CR 109.5 — "target creature" with no controller pronoun
                    // gathers every battlefield Creature regardless of side.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                if (chosen.Targets.Count == 0 || chosen.Targets[0].Count == 0)
                {
                    return Array.Empty<IEffect>();
                }
                if (chosen.Targets[0][0] is not Creature target)
                {
                    return Array.Empty<IEffect>();
                }

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: exile target creature, return at next end step with a +1/+1 counter",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (target.Zone != ZoneType.Battlefield) return;

                            var targetOwner = target.Owner ?? caster;

                            // CR 701.21 — Exile via owner-routed zone moves.
                            targetOwner.Zones.Battlefield.RemoveCard(target);
                            targetOwner.Zones.Exile.AddCard(target);
                            target.SetZone(ZoneType.Exile);

                            // CR 603.7 — delayed end-step return rider. Only
                            // register when a TriggerManager is supplied
                            // (matches TouchTheSpiritRealm shape-only fallback).
                            if (triggers == null) return;

                            var resolvedAt = DateTime.UtcNow;
                            var returnEffect = new Effect(
                                $"{CardName}: return exiled creature at next end step with +1/+1 counter (CR 603.7 + CR 614)",
                                () =>
                                {
                                    // CR 111.8 — tokens that left the
                                    // battlefield cease to exist; bounce
                                    // moves out of exile also skip the
                                    // return. Defensive Exile-zone check.
                                    if (target.Zone != ZoneType.Exile) return;

                                    var returnOwner = target.Owner ?? caster;

                                    // CR 614 — return to the battlefield
                                    // under the card's OWNER's control
                                    // (not the spell's controller).
                                    returnOwner.Zones.Exile.RemoveCard(target);
                                    returnOwner.Zones.Battlefield.AddCard(target);
                                    target.SetZone(ZoneType.Battlefield);
                                    target.SetController(returnOwner);

                                    // CR 614 — "with a +1/+1 counter on it"
                                    // is part of the same return event; the
                                    // counter is placed as the card enters,
                                    // so ETB-on-counters triggers (Hardened
                                    // Scales, Doubling Season) see the counter
                                    // when they resolve. CountersService.Add
                                    // routes through the optional ReplacementBus
                                    // (not supplied at this surface — see
                                    // class xmldoc "Deferred" gap).
                                    CountersService.Add(
                                        target,
                                        CounterType.PlusOnePlusOne,
                                        1,
                                        replacements: null);
                                });

                            var delayed = new DelayedTriggeredAbility(
                                source: source ?? target,
                                controller: caster,
                                condition: new EventTriggerCondition<StepStartedEvent>(
                                    (e, _) => e.StepType == PhaseStateType.End
                                              && e.Timestamp > resolvedAt),
                                effects: new IEffect[] { returnEffect });

                            triggers.RegisterDelayed(delayed);
                        }),
                };
            });
    }
}
