using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Acidic Slime (Magic 2010 + many reprints).
///
/// Creature — Ooze {3}{G}{G} 2/2. Oracle text:
///   "Deathtouch (Any amount of damage this deals to a creature is enough to
///    destroy it.)
///    When this creature enters, destroy target artifact, enchantment, or
///    land."
///
/// ## Shape source
/// Card identity (name, {3}{G}{G}, 2/2, Creature — Ooze) is loaded from
/// <c>Majik.Core/CardData/Cards/acidic-slime.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The Deathtouch keyword marker and the
/// single ETB destroy trigger are attached in code below: the JSON ability
/// schema does not yet express a "destroy target" effect, so it is hand-rolled
/// here — same posture as the suggested analogue
/// <see cref="ReclamationSageFactory"/> (ETB destroy target artifact /
/// enchantment) extended to also accept a land as a legal target.
///
/// ## Implemented (v1)
/// - 2/2 Ooze (CR 205.3m) at {3}{G}{G}.
/// - <b>Deathtouch (CR 702.2)</b> attached as a <see cref="KeywordAbility"/>
///   marker — same shape as <see cref="MossViperFactory"/>'s Deathtouch.
///   <see cref="Combat.CombatAbilities.HasDeathtouch"/> consumes this for
///   lethal-damage determination.
/// - <b>ETB trigger (CR 603.6a)</b>: "When this creature enters, destroy
///   target artifact, enchantment, or land." A bespoke 1..1
///   <see cref="TargetRequest"/> over every artifact + enchantment + land on
///   the battlefield across every player (Intent:
///   <see cref="BotIntent.Removal"/>). Unlike Reclamation Sage's "you may",
///   this destroy is MANDATORY (CR 603.3c — a mandatory ETB trigger must be
///   put on the stack; on resolution it destroys the chosen target if it is
///   still legal). Resolution honours
///   <see cref="TriggeredAbility.ChosenTargets"/> when an agent set it,
///   otherwise falls back to the first legal candidate on the controller's
///   battlefield (single-arg dispatcher posture — mirrors Reclamation Sage).
/// - Resolution body validates the chosen target is still an artifact OR
///   enchantment OR land on the battlefield (CR 608.2b — illegal target →
///   clean no-op) and destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="ZoneMoveReason.Destroy"/> (CR 701.7 — indestructible cancels
///   per CR 702.12, active regeneration shield consumed per CR 701.15).
///
/// ## Deferred (v1 gaps)
/// - <b>Real agent-driven target prompt</b>: production callers wire
///   <see cref="TriggeredAbility.SetChosenTargets"/> from an agent prompt
///   before triggers resolve via the
///   <see cref="TargetRequest.CandidateGatherer"/>. The factory falls back to
///   the first legal target deterministically when no agent picked — same
///   posture as <see cref="ReclamationSageFactory"/>.
/// - <b>Target legality in ActionValidator</b>: the validator does not filter
///   to "artifact, enchantment, or land" at announcement; the resolution-time
///   guard handles illegal targets (CR 608.2b). Same posture as
///   <see cref="ReclamationSageFactory"/>.
/// </summary>
[CardName("Acidic Slime")]
public static class AcidicSlimeFactory
{
    public const string CardName = "Acidic Slime";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("acidic-slime");

    /// <summary>
    /// Construct Acidic Slime with its Deathtouch marker and ETB destroy
    /// trigger attached to the card shape but NOT registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Acidic Slime with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so the relevant ETB event places it on the stack
    /// automatically (CR 603.3).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.2 — Deathtouch marker. CombatAbilities.HasDeathtouch
        // consumes this for lethal-damage determination.
        card.AddAbility(new KeywordAbility("Deathtouch", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 603.3c (mandatory).
        //   "When this creature enters, destroy target artifact,
        //    enchantment, or land."
        //
        // Bespoke 1..1 TargetRequest over artifacts + enchantments + lands
        // on the battlefield across every player. Live gatherer enumerates
        // the battlefield so the agent's target picker sees an up-to-date
        // legal set at resolution (BotIntent.Removal flips ownership
        // priority).
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;

        var etbEffect = new Effect(
            $"{CardName}: destroy target artifact, enchantment, or land",
            () => ResolveDestroy(owner, etb));

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact, enchantment, or land",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: GatherTargets(owner).Cast<object>().ToList(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(IsLegalTarget)
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// Legal-target predicate — artifact OR enchantment OR land
    /// (CR 109.2 / CR 608.2b).
    /// </summary>
    private static bool IsLegalTarget(ICard c) =>
        c.HasType(CardType.Artifact)
        || c.HasType(CardType.Enchantment)
        || c.HasType(CardType.Land);

    /// <summary>
    /// Snapshot the controller-visible legal-target set at trigger-creation
    /// time. Production callers refresh via
    /// <see cref="TargetRequest.CandidateGatherer"/> at resolution.
    /// </summary>
    private static IReadOnlyList<ICard> GatherTargets(Player owner) =>
        // Conservatively scan the controller's battlefield only — additional
        // opponents become visible once a game-scoped CandidateGatherer
        // context is supplied. Keeps this overload safe for shape tests where
        // no game context exists.
        owner.Zones.Battlefield.GetCards().Where(IsLegalTarget).ToList();

    /// <summary>
    /// Resolve the ETB destroy. Honours
    /// <see cref="TriggeredAbility.ChosenTargets"/> when set by the agent;
    /// otherwise falls back to the first legal target on the controller's
    /// battlefield (deterministic single-arg dispatcher posture — mirrors
    /// <see cref="ReclamationSageFactory"/>). Validates the chosen target is
    /// still a legal artifact / enchantment / land on the battlefield
    /// (CR 608.2b) before destroying (CR 701.7).
    /// </summary>
    private static void ResolveDestroy(Player owner, TriggeredAbility? etb)
    {
        Permanent? picked = null;

        // 1) Honour agent-set target (production path).
        if (etb != null
            && etb.ChosenTargets.Count > 0
            && etb.ChosenTargets[0].Count > 0
            && etb.ChosenTargets[0][0] is Permanent chosen)
        {
            picked = chosen;
        }

        // 2) Deterministic fallback — first legal target on the
        //    controller's battlefield (no-agent dispatcher posture). The
        //    trigger's gatherer needs a live GameContext we don't have
        //    here, so we fall back to the controller-scoped snapshot.
        picked ??= GatherTargets(owner).OfType<Permanent>().FirstOrDefault();

        if (picked == null) return;

        // CR 608.2b — illegal-on-resolution check.
        if (picked.Zone != ZoneType.Battlefield) return;
        if (!IsLegalTarget(picked)) return;

        // CR 701.7 — destroy. Indestructible (CR 702.12) cancels; active
        // regeneration shield (CR 701.15) is consumed.
        OracleSpellBinder.MoveToGraveyard(picked, ZoneMoveReason.Destroy);
    }
}
