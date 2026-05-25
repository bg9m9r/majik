using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Touch the Spirit Realm (Kamigawa: Neon Dynasty,
/// {2}{W}).
///
/// Instant. Current Scryfall oracle:
///   "Exile target artifact, creature, or enchantment.
///    Channel — {2}{W}, Discard Touch the Spirit Realm: Exile target
///    creature or enchantment you control. Return it to the battlefield
///    under its owner's control at the beginning of the next end step."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {2}{W}, owner / controller.
/// - <b>Cast body</b> — <see cref="BuildSpellDefinition"/> returns a
///   <see cref="SpellDefinition"/> with a single 1..1 "target artifact,
///   creature, or enchantment" <see cref="TargetRequest"/> sourced from a
///   live <c>CandidateGatherer</c> walking every player's battlefield
///   (Artifact / Creature / Enchantment card-types; CR 305 — Lands are a
///   card type, not a subtype, so the filter correctly rejects Dryad
///   Arbor / Mishra's Factory). Removal intent in the bot ranker.
///   Resolve: re-checks the target is still a battlefield permanent
///   matching the type filter (CR 608.2b) and exiles it via owner-routed
///   zone moves (CR 701.21 — mirrors PathToExile / AnguishedUnmaking /
///   PrismaticEnding). Indestructible (CR 702.12) does NOT prevent exile.
///
/// - <b>Channel — {2}{W}, Discard Touch the Spirit Realm</b> (CR 702.74).
///   Activated-from-hand ability attached to the card (same surface used
///   by <see cref="ChannelLandCycleFactory"/>: <see cref="ManaCostCost"/>
///   + <see cref="DiscardSelfCost"/> — the discard-self cost gates
///   activation to <see cref="ZoneType.Hand"/> per CR 702.74a).
///   <see cref="AttachChannelAbility"/> wires the activated ability with
///   one 1..1 "target creature or enchantment you control" TargetRequest
///   (controller-side gathering only — Protection intent). On resolve:
///   (a) re-check the target is still a battlefield Creature /
///       Enchantment controlled by the channel's controller (CR 608.2b);
///   (b) exile it via owner-routed moves (CR 701.21);
///   (c) when a <see cref="TriggerManager"/> is supplied, register a
///       one-shot <see cref="DelayedTriggeredAbility"/> (CR 603.7) that
///       fires on the first <see cref="StepStartedEvent"/> with
///       <c>StepType == End</c> and timestamp strictly after this resolve
///       (same activation-time fence as Mishra's Bauble / Wrenn's
///       Resolve / Yorion's ETB-exile rider). When the trigger resolves,
///       move the still-exiled card back to the battlefield under its
///       owner's control (CR 614 — "under its owner's control" overrides
///       the controller pronoun on the channel's resolve closure).
///
/// ## Deferred (v1 gaps)
/// - <b>Agent prompt — "target creature or enchantment YOU control"</b>:
///   the Channel TargetRequest's <c>CandidateGatherer</c> filters to
///   controller-side Creature / Enchantment permanents; the agent
///   surface for the actual pick is the same shared queue as PathToExile
///   / Anguished Unmaking — heuristic bot picks via Intent ranking, no
///   explicit "pick one of yours" prompt needed at the v1 surface.
///
/// - <b>Tokens / non-card permanents</b>: tokens that get exiled cease
///   to exist (CR 111.8). The delayed-return trigger defensively checks
///   <c>Zone == Exile</c> at resolve so a token already removed by SBA
///   is skipped — same posture as
///   <see cref="YorionSkyNomadFactory.ResolveEtb"/>.
///
/// - <b>Replacement effects on return</b>: the delayed return uses
///   <see cref="ZoneService.MoveCard"/> when supplied so ETB triggers /
///   replacement effects on the returned card fire correctly; raw-zone
///   fallback skips those events (matching Yorion's two-mode posture).
/// </summary>
[CardName("Touch the Spirit Realm")]
public static class TouchTheSpiritRealmFactory
{
    public const string CardName = "Touch the Spirit Realm";
    public const string PrintedManaCost = "{2}{W}";
    public const string ChannelManaCost = "{2}{W}";

    /// <summary>CardDef DSL — card shape only. The cast resolve body
    /// lives in <see cref="BuildSpellDefinition"/>; the Channel activated
    /// ability is attached by <see cref="Create(Player, TriggerManager?, ZoneService?)"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    /// <summary>
    /// Single-arg dispatcher entry point. Builds the Instant shape and
    /// attaches the Channel ability in shape-only mode (no
    /// <see cref="TriggerManager"/>, no <see cref="ZoneService"/>) — the
    /// Channel's exile body still runs, but the delayed end-step return
    /// is skipped (matches Wrenn's Resolve / Yorion's shape-only fallback).
    /// </summary>
    public static Instant Create(Player owner) => Create(owner, triggers: null, zones: null);

    /// <summary>
    /// Build Touch the Spirit Realm with the Channel activated ability
    /// fully wired. The cast body is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner, TriggerManager? triggers, ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Instant)CardDefRuntime.Build(Define(), owner);
        AttachChannelAbility(card, owner, triggers, zones);
        return card;
    }

    /// <summary>
    /// Build the printed cast SpellDefinition — single 1..1 target
    /// artifact / creature / enchantment, on-resolve exile via
    /// owner-routed zone moves (CR 701.21).
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact, creature, or enchantment",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP — live gather any battlefield permanent
                    // that's Artifact / Creature / Enchantment (CR 305 — Lands
                    // are a card type, so a Dryad Arbor is rejected here even
                    // though it's also a Creature... wait — Dryad Arbor IS a
                    // Creature card type, so it WOULD be eligible. The printed
                    // text reads "target artifact, creature, or enchantment"
                    // and Dryad Arbor satisfies the creature half).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact)
                                    || c.HasType(CardType.Creature)
                                    || c.HasType(CardType.Enchantment))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: exile target artifact/creature/enchantment",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            if (!target.HasType(CardType.Artifact)
                                && !target.HasType(CardType.Creature)
                                && !target.HasType(CardType.Enchantment))
                            {
                                return;
                            }

                            // CR 701.21 — Exile via owner-routed zone moves.
                            // Indestructible (CR 702.12) does not prevent exile.
                            var fromOwner = target.Owner;
                            if (fromOwner != null)
                            {
                                fromOwner.Zones.Battlefield.RemoveCard(target);
                                fromOwner.Zones.Exile.AddCard(target);
                            }
                            target.SetZone(ZoneType.Exile);
                        }),
                };
            });
    }

    /// <summary>
    /// Wire the Channel activated ability onto <paramref name="card"/>.
    /// Same cost stack as <see cref="ChannelLandCycleFactory"/>:
    /// <see cref="ManaCostCost"/>(<see cref="ChannelManaCost"/>) +
    /// <see cref="DiscardSelfCost"/>. The discard-self cost is what gates
    /// activation to the hand zone (CR 702.74a) — the engine surface
    /// doesn't otherwise check the source zone for activated-ability
    /// activations.
    /// </summary>
    private static void AttachChannelAbility(
        Instant card, Player controller, TriggerManager? triggers, ZoneService? zones)
    {
        ActivatedAbility? channel = null;

        var targetRequests = new[]
        {
            new TargetRequest(
                Description: "target creature or enchantment you control",
                MinTargets: 1,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Protection,
                // Controller-scoped gather. CR 109.5 / CR 608.2b — "you
                // control" reads off Permanent.Controller at choose-time.
                CandidateGatherer: ctx => controller.Zones.Battlefield.GetCards()
                    .Where(c => c.HasType(CardType.Creature) || c.HasType(CardType.Enchantment))
                    .Where(c => ReferenceEquals(c.Controller, controller))
                    .Cast<object>()
                    .ToList()),
        };

        var channelEffect = new Effect(
            $"{CardName} (Channel): exile target creature/enchantment you control; return at next end step",
            () =>
            {
                var ability = channel!;
                if (ability.ChosenTargets.Count == 0 || ability.ChosenTargets[0].Count == 0) return;
                if (ability.ChosenTargets[0][0] is not Permanent target) return;

                // CR 608.2b — resolution-time legality re-check.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!ReferenceEquals(target.Controller, controller)) return;
                if (!target.HasType(CardType.Creature) && !target.HasType(CardType.Enchantment)) return;

                // CR 701.21 — Exile. Prefer ZoneService when supplied so
                // LTB events fire (Yorion's two-mode posture).
                if (zones != null)
                {
                    zones.MoveCard(target, ZoneType.Battlefield, ZoneType.Exile);
                }
                else
                {
                    var fromOwner = target.Owner;
                    if (fromOwner != null)
                    {
                        fromOwner.Zones.Battlefield.RemoveCard(target);
                        fromOwner.Zones.Exile.AddCard(target);
                    }
                    target.SetZone(ZoneType.Exile);
                }

                // CR 603.7 — delayed end-step return rider. Only register
                // when a TriggerManager is supplied (matches WrennsResolve
                // / Yorion shape-only fallback).
                if (triggers == null) return;

                var resolvedAt = DateTime.UtcNow;
                var returnEffect = new Effect(
                    $"{CardName} (Channel): return exiled card at next end step (CR 603.7)",
                    () =>
                    {
                        // CR 111.8 — tokens that left the battlefield cease
                        // to exist; defensively skip if the card has already
                        // moved out of exile (SBA pickup, second move, etc.).
                        if (target.Zone != ZoneType.Exile) return;

                        // CR 614 — "under its owner's control" — return goes
                        // to the card's OWNER, not to the channel's
                        // controller (distinct from Yorion's "you control"
                        // resolve, where the controller is also the owner
                        // of the exiled permanents).
                        var returnOwner = target.Owner ?? controller;

                        if (zones != null)
                        {
                            zones.MoveCard(target, ZoneType.Exile, ZoneType.Battlefield, returnOwner);
                        }
                        else
                        {
                            // Raw-zone fallback — find which zone holds the
                            // card today (it could have been routed through
                            // someone else's exile pile via the channel's
                            // controller exiling an opponent-owned token,
                            // but the printed text targets controller-side
                            // permanents only so it must be in returnOwner's
                            // exile pile).
                            returnOwner.Zones.Exile.RemoveCard(target);
                            returnOwner.Zones.Battlefield.AddCard(target);
                            target.SetZone(ZoneType.Battlefield);
                            target.SetController(returnOwner);
                        }
                    });

                var delayed = new DelayedTriggeredAbility(
                    source: card,
                    controller: controller,
                    condition: new EventTriggerCondition<StepStartedEvent>(
                        (e, _) => e.StepType == PhaseStateType.End
                                  && e.Timestamp > resolvedAt),
                    effects: new IEffect[] { returnEffect });

                triggers.RegisterDelayed(delayed);
            });

        channel = new ActivatedAbility(
            source: card,
            controller: controller,
            costs: new ICost[]
            {
                new ManaCostCost(ChannelManaCost),
                new DiscardSelfCost(card),
            },
            effects: new IEffect[] { channelEffect },
            targetRequests: targetRequests);

        card.AddAbility(channel);
    }
}
