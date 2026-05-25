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
/// Named-card factory for Otherworldly Journey (Champions of Kamigawa,
/// {1}{W}).
///
/// Instant — Arcane. Scryfall oracle (verified):
///   "Exile target creature. At the beginning of the next end step, return
///    that card to the battlefield under its owner's control with a +1/+1
///    counter on it."
///
/// Identical body to <see cref="LongRoadHomeFactory"/> — Otherworldly
/// Journey is the original Kamigawa printing with the Arcane subtype
/// (gating splice-onto-Arcane riders, CR 702.46). The two factories share
/// the same exile-then-delayed-return shape; only the printing's subtype
/// + set differ.
///
/// ## Implemented (v1)
/// - Instant shape (mana cost {1}{W}) with the
///   <see cref="CardSubtype.Arcane"/> subtype attached (CR 205.3k —
///   matches <see cref="GoryosVengeanceFactory"/> / Lava Spike printings).
/// - <b>Cast body</b> — <see cref="BuildSpellDefinition"/> returns a
///   <see cref="SpellDefinition"/> with a single 1..1 "target creature"
///   <see cref="TargetRequest"/>. Live <c>CandidateGatherer</c> walks every
///   player's battlefield for <see cref="CardType.Creature"/> permanents
///   (no controller-side filter — Otherworldly Journey can target any
///   creature). Bot intent <see cref="BotIntent.Protection"/> mirrors
///   <see cref="LongRoadHomeFactory"/>.
/// - <b>Resolve</b>: re-checks the target is still a battlefield Creature
///   (CR 608.2b). Exiles via owner-routed zone moves (CR 701.21). When a
///   <see cref="TriggerManager"/> is supplied, registers a one-shot
///   <see cref="DelayedTriggeredAbility"/> (CR 603.7) that fires on the
///   first End-step <see cref="StepStartedEvent"/> after this resolve.
///   On delayed-trigger resolution: defensively check the card is still
///   in exile, return to the battlefield under its OWNER's control
///   (CR 614), and place one <see cref="CounterType.PlusOnePlusOne"/>
///   counter via <see cref="CountersService.Add"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Splice onto Arcane (CR 702.46)</b>: this factory attaches the
///   Arcane subtype so a future <see cref="Majik.Core.Costs.SpliceOntoArcaneCost"/>
///   rider on another card (Glacial Ray, Through the Breach) can splice
///   onto Otherworldly Journey. The splice-resolution primitive itself
///   isn't surfaced at this factory (Otherworldly Journey doesn't have
///   splice itself — it's a splice-target, not a splice-source).
/// - <b>ZoneService routing</b>: this factory uses raw zone moves for
///   exile + return (same posture as <see cref="LongRoadHomeFactory"/>
///   / <see cref="CloudshiftFactory"/>).
/// - <b>Counter ETB replacements</b>: <see cref="CountersService.Add"/>
///   is called with <c>replacements: null</c> — Hardened Scales / Doubling
///   Season amplifiers won't fire. Same posture as the rest of the v1
///   counter-placement surface.
/// </summary>
[CardName("Otherworldly Journey")]
public static class OtherworldlyJourneyFactory
{
    public const string CardName = "Otherworldly Journey";
    public const string PrintedManaCost = "{1}{W}";

    /// <summary>Construct Otherworldly Journey as an Instant — Arcane
    /// owned and controlled by <paramref name="owner"/>. Card shape only —
    /// the cast body is produced by <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Otherworldly Journey with optional <see cref="TriggerManager"/>.
    /// When <paramref name="triggers"/> is supplied, the cast body's
    /// delayed end-step return rider registers with the bus.
    /// </summary>
    public static Instant Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: new[] { CardSubtype.Arcane });
        card.SetOwner(owner);
        card.SetController(owner);

        _ = triggers; // triggers flow through BuildSpellDefinition's overload, same as LongRoadHomeFactory.

        return card;
    }

    /// <summary>
    /// Build the cast SpellDefinition. <paramref name="caster"/> owns the
    /// resolve closure; <paramref name="triggers"/> (when supplied) is
    /// where the delayed end-step return rider registers (CR 603.7).
    /// <paramref name="source"/> is the stack object the delayed trigger
    /// reports back to (the Otherworldly Journey card itself).
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
                    // CR 109.5 — "target creature" with no controller
                    // pronoun gathers every battlefield Creature.
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

                            // CR 603.7 — delayed end-step return rider.
                            if (triggers == null) return;

                            var resolvedAt = DateTime.UtcNow;
                            var returnEffect = new Effect(
                                $"{CardName}: return exiled creature at next end step with +1/+1 counter (CR 603.7 + CR 614)",
                                () =>
                                {
                                    if (target.Zone != ZoneType.Exile) return;

                                    var returnOwner = target.Owner ?? caster;

                                    // CR 614 — return under the OWNER's control.
                                    returnOwner.Zones.Exile.RemoveCard(target);
                                    returnOwner.Zones.Battlefield.AddCard(target);
                                    target.SetZone(ZoneType.Battlefield);
                                    target.SetController(returnOwner);

                                    // CR 614 — +1/+1 counter placed as the
                                    // card re-enters; ETB-on-counters triggers
                                    // (Hardened Scales, Doubling Season) see
                                    // the counter when they resolve.
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
