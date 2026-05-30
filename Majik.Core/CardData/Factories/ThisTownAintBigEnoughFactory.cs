using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for This Town Ain't Big Enough (Outlaws of Thunder
/// Junction, {4}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "This spell costs {3} less to cast if it targets a permanent you control.
///    Return up to two target nonland permanents to their owners' hands."
///
/// ## Implemented (v1)
/// - Instant shape {4}{U}, blue — built from the embedded JSON definition
///   (<c>this-town-aint-big-enough.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Cost reduction (CR 117.7)</b>: a printed
///   <see cref="CostReductionAbility"/> using the whole-reducer shape, mirror
///   of <see cref="MysticalDisputeFactory"/> ("costs {2} less if it targets a
///   blue spell"). At cost-calc time <see cref="CostReduction.GetEffectiveCost"/>
///   consults the reducer; the closure reads <see cref="Card.PendingCastTargets"/>
///   (stamped by <see cref="SpellCastFlow"/> immediately after target
///   collection and before cost calculation — CR 601.2c then 601.2f). If any
///   picked target is a <see cref="Permanent"/> controlled by the caster, the
///   reducer returns 3 ({4}{U} → {1}{U}). Otherwise zero. Floor-at-zero and
///   the coloured-pip protection (CR 117.7c) are enforced by
///   <see cref="CostReduction"/>.
/// - <b>Bounce (CR 701.10)</b>: <see cref="BuildDefinition"/> declares one
///   0..2 "up to two target nonland permanents" <see cref="TargetRequest"/>
///   (MinTargets=0 — "up to" per CR 115.1b — mirrors
///   <see cref="DisperseFactory"/> extended to two targets and
///   <see cref="ElectrolyzeFactory"/>'s single-request multi-target shape).
///   On resolution each chosen target that is still a nonland permanent on the
///   battlefield is returned to its owner's hand; lands and targets that have
///   left the battlefield are no-ops (CR 608.2b — illegal-at-resolution picks
///   are skipped independently).
///
/// ## Deferred (v1 gaps — shared with the Mystical Dispute family)
/// - <b>Bot affordability sees worst-case cost</b>: HeuristicBotAgent's "can I
///   cast this?" picker calls <see cref="CostReduction.GetEffectiveCost"/>
///   BEFORE targets are stamped, so it sees the printed {4}{U} and may skip
///   casts that would resolve at {1}{U}. The reduction still applies for real
///   (cast-time payment uses the same call AFTER targets are stamped), so the
///   bot simply plays slightly conservatively. Same posture as Mystical
///   Dispute.
/// </summary>
[CardName("This Town Ain't Big Enough")]
public static class ThisTownAintBigEnoughFactory
{
    public const string CardName = "This Town Ain't Big Enough";
    public const string Slug = "this-town-aint-big-enough";
    public const string PrintedManaCost = "{4}{U}";

    /// <summary>CR 117.7 — generic reduction when the spell targets a
    /// permanent the caster controls.</summary>
    public const int OwnPermanentTargetGenericReduction = 3;

    /// <summary>CR 115.1b — "Return up to two target nonland permanents."</summary>
    public const int MaxTargets = 2;

    /// <summary>
    /// Build the card shape from the embedded JSON definition and attach the
    /// target-conditional cost-reduction ability (CR 117.7). The resolve-time
    /// SpellDefinition (the bounce) is built on demand via
    /// <see cref="BuildDefinition"/> — mirrors <see cref="MysticalDisputeFactory"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);

        // CR 117.7 — "This spell costs {3} less to cast if it targets a
        // permanent you control." Whole-reducer shape: the closure inspects
        // the card's pending cast targets (stamped by SpellCastFlow right
        // after target collection — see Card.PendingCastTargets). The reducer
        // receives the caster, so "you control" reads against the caster, not
        // necessarily the printed owner (relevant if cast via a steal effect).
        // Returns 3 when any chosen target is a permanent the caster controls,
        // else 0. Floor-at-zero is enforced by CostReduction.GetEffectiveCost
        // so {4}{U} → {1}{U} (not below; the {U} pip is untouched, CR 117.7c).
        card.AddAbility(new CostReductionAbility(
            totalReducer: caster => TargetsPermanentCasterControls(card, caster)
                ? OwnPermanentTargetGenericReduction
                : 0,
            description: "Costs {3} less to cast if it targets a permanent you control"));

        return card;
    }

    /// <summary>
    /// Predicate consulted at cost-calc time: does the card's pending cast
    /// target set include at least one permanent the caster controls?
    /// Tolerates the no-target case (null / empty) — bot affordability and
    /// shape-only tests both call <see cref="CostReduction.GetEffectiveCost"/>
    /// before targets are picked.
    /// </summary>
    private static bool TargetsPermanentCasterControls(ICard card, Player caster)
    {
        var pending = (card as Card)?.PendingCastTargets;
        if (pending == null) return false;

        foreach (var bucket in pending)
        {
            foreach (var raw in bucket)
            {
                if (raw is Permanent perm && ReferenceEquals(perm.Controller, caster))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Build the "Return up to two target nonland permanents to their owners'
    /// hands" SpellDefinition. Single 0..2 request (CR 115.1b — "up to two"
    /// allows zero). Mirrors <see cref="DisperseFactory.BuildDefinition"/>
    /// extended to two targets.
    /// </summary>
    /// <param name="zoneService">Optional ZoneService for replacement-bus-aware
    /// zone moves. When null, raw zone manipulation is used (shape tests).</param>
    public static SpellDefinition BuildDefinition(ZoneService? zoneService = null) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "up to two target nonland permanents",
                    MinTargets: 0,
                    MaxTargets: MaxTargets,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Permanent>()
                        .Where(p => !p.HasType(CardType.Land))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var rawTargets = chosen.Targets.Count > 0
                    ? chosen.Targets[0]
                    : Array.Empty<object>();

                return new IEffect[]
                {
                    new Effect(
                        "This Town Ain't Big Enough — return up to two target nonland permanents to their owners' hands",
                        () =>
                        {
                            // CR 608.2c — resolve each chosen target
                            // independently; an illegal-at-resolution pick
                            // (left the battlefield, or became a land) is a
                            // no-op without affecting the other target.
                            foreach (var raw in rawTargets)
                            {
                                Resolve(raw, zoneService);
                            }
                        }),
                };
            });

    private static void Resolve(object raw, ZoneService? zoneService)
    {
        // CR 608.2b — target must still be a permanent on the battlefield.
        if (raw is not Permanent target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        // "Nonland permanent" — a land is not a legal target (CR 608.2b no-op).
        if (target.HasType(CardType.Land)) return;

        var targetOwner = target.Owner;
        if (targetOwner == null) return;

        var controller = target.Controller ?? targetOwner;

        // CR 701.10 — return to owner's hand.
        if (zoneService != null)
        {
            zoneService.MoveCard(target, ZoneType.Battlefield, ZoneType.Hand);
        }
        else
        {
            controller.Zones.Battlefield.RemoveCard(target);
            targetOwner.Zones.Hand.AddCard(target);
            target.SetZone(ZoneType.Hand);
            target.SetController(targetOwner);
        }
    }
}
