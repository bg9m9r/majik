using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reclamation Sage (Magic 2015 + many reprints,
/// most recently Modern Horizons 3).
///
/// Creature — Elf Shaman {2}{G} 2/1. Oracle text:
///   "When Reclamation Sage enters, you may destroy target artifact or
///    enchantment."
///
/// ## Implemented (v1)
/// - 2/1 Elf Shaman, mana cost {2}{G}.
/// - Single ETB <see cref="TriggeredAbility"/> (CR 603.6a) wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> with a bespoke 1..1
///   <see cref="TargetRequest"/> for "target artifact or enchantment"
///   (Intent: <see cref="BotIntent.Removal"/>). The candidate list is
///   the union of every artifact + enchantment on the battlefield
///   across every player at trigger-creation time — production callers
///   refresh via the agent prompt before resolution (same posture as
///   <see cref="EternalWitnessFactory"/> / <see cref="BoneShardsFactory"/>).
/// - Resolution body reads <see cref="TriggeredAbility.ChosenTargets"/>;
///   validates the chosen target is still an artifact OR enchantment on
///   the battlefield (CR 608.2b — illegal target → clean no-op); destroys
///   via <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="ZoneMoveReason.Destroy"/> (CR 701.7 — indestructible
///   cancels per CR 702.12, active regeneration shield consumed per
///   CR 701.15). Same destroy posture as
///   <see cref="CausticCaterpillarFactory"/>.
/// - "You may" auto-accepted at v1 — same posture as Eternal Witness /
///   Tireless Tracker / Snapcaster Mage's ETB grant.
/// - Single-arg dispatcher path attaches the trigger shape WITHOUT
///   bus-driven wiring (suitable for shape tests). The
///   (owner, triggers) overload registers the ETB with the supplied
///   <see cref="TriggerManager"/> for bus-driven firing.
///
/// ## Deferred (v1 gaps)
/// - <b>Real agent-driven target prompt</b>: production callers wire
///   <see cref="TriggeredAbility.SetChosenTargets"/> from an agent prompt
///   before triggers resolve. The factory falls back to the first legal
///   target deterministically when no agent picked (mirrors
///   <see cref="EternalWitnessFactory"/>'s no-agent posture).
/// - <b>"You may" decline</b>: not modelled — the ability always destroys
///   if a legal target exists. Same gap as Eternal Witness / Tireless
///   Tracker.
/// - <b>Target legality in ActionValidator</b>: the validator does not
///   filter to "artifact or enchantment" at announcement; resolution-time
///   guard handles illegal targets (CR 608.2b). Same posture as
///   <see cref="CausticCaterpillarFactory"/>.
/// </summary>
[CardName("Reclamation Sage")]
public static class ReclamationSageFactory
{
    public const string CardName = "Reclamation Sage";
    public const string PrintedManaCost = "{2}{G}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>Printed oracle text — informational.</summary>
    public const string OracleText =
        "When Reclamation Sage enters, you may destroy target artifact or "
        + "enchantment.";

    /// <summary>
    /// Construct Reclamation Sage with no runtime wiring. Produces the
    /// correct card identity + ETB trigger shape for dispatcher / shape
    /// tests; the trigger is NOT registered with a <see cref="TriggerManager"/>.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Reclamation Sage with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied the ETB ability
    /// is registered for bus-driven firing.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When Reclamation Sage enters, you may destroy target artifact
        //    or enchantment."
        //
        // Bespoke 1..1 TargetRequest mirrors Bone Shards' "target creature
        // or planeswalker" shape but restricted to artifacts + enchantments.
        // Live gatherer enumerates the battlefield across every player so
        // the agent's target picker sees an up-to-date legal set at
        // resolution (BotIntent.Removal flips ownership priority).
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;

        var etbEffect = new Effect(
            $"{CardName}: destroy target artifact or enchantment",
            () => ResolveDestroy(card, owner, etb));

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
                    Description: "target artifact or enchantment",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: GatherTargets(owner).Cast<object>().ToList(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact)
                                 || c.HasType(CardType.Enchantment))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// Snapshot the controller-visible legal-target set at trigger-creation
    /// time. Production callers refresh via
    /// <see cref="TargetRequest.CandidateGatherer"/> at resolution.
    /// </summary>
    private static IReadOnlyList<ICard> GatherTargets(Player owner)
    {
        // Conservatively scan the controller's battlefield only — additional
        // opponents become visible once a game-scoped CandidateGatherer
        // context is supplied. Keeps this overload safe for shape tests
        // where no game context exists.
        return owner.Zones.Battlefield.GetCards()
            .Where(c => c.HasType(CardType.Artifact)
                     || c.HasType(CardType.Enchantment))
            .ToList();
    }

    /// <summary>
    /// Resolve the ETB destroy. Honours <see cref="TriggeredAbility.ChosenTargets"/>
    /// when set by the agent; otherwise falls back to the first legal target
    /// in the gatherer pool (deterministic single-arg dispatcher posture —
    /// mirrors <see cref="EternalWitnessFactory"/>'s no-agent fallback).
    /// Validates the chosen target is still a legal artifact / enchantment
    /// on the battlefield (CR 608.2b) before destroying (CR 701.7).
    /// </summary>
    private static void ResolveDestroy(
        Creature sage,
        Player owner,
        TriggeredAbility? etb)
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

        // 2) Deterministic fallback — first legal artifact/enchantment
        //    on the controller's battlefield (no-agent dispatcher
        //    posture). The trigger's gatherer needs a live GameContext
        //    we don't have here, so we fall back to the controller-
        //    scoped snapshot.
        picked ??= GatherTargets(owner).OfType<Permanent>().FirstOrDefault();

        if (picked == null) return;

        // CR 608.2b — illegal-on-resolution check.
        if (picked.Zone != ZoneType.Battlefield) return;
        if (!(picked.HasType(CardType.Artifact)
              || picked.HasType(CardType.Enchantment))) return;

        // CR 701.7 — destroy. Indestructible (CR 702.12) cancels; active
        // regeneration shield (CR 701.15) is consumed.
        OracleSpellBinder.MoveToGraveyard(picked, ZoneMoveReason.Destroy);
    }
}
