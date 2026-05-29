using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ingot Chewer (Lorwyn, {4}{R}).
///
/// Creature — Elemental 3/3. Oracle text (verified against Scryfall):
///   "When this creature enters, destroy target artifact.
///    Evoke {R} (You may cast this spell for its evoke cost. If you do,
///    it's sacrificed when it enters.)"
///
/// The base shape (name, Creature, Elemental subtype, {4}{R}, 3/3) is
/// materialised from the embedded JSON definition (<c>ingot-chewer.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="TwinSilkSpiderFactory"/>. The JSON
/// <c>AbilityDefinition</c> schema doesn't express keyword markers, evoke
/// alt-cost, or targeted destroy effects, so the Evoke keyword + sacrifice
/// trigger and the ETB destroy trigger are layered on top here.
///
/// ## Implemented (v1)
/// - 3/3 Elemental at {4}{R}.
/// - <b>Evoke {R} (CR 702.74)</b> — keyword marker (<see cref="KeywordAbility"/>)
///   plus the printed "When this creature enters, if its evoke cost was paid,
///   sacrifice it" trigger (CR 702.74b) via <see cref="EvokeFactory.Build"/>.
///   The evoke <em>alternative cost</em> itself is pure mana ({R}) — callers
///   wire it with <see cref="Majik.Core.Costs.EvokeAlternativeCost(Majik.Core.ValueObjects.ManaCost)"/>
///   at cast time (same as the classic Lorwyn evokers, e.g. Mulldrifter; no
///   pitch component, unlike the Modern Horizons incarnation cycle —
///   <see cref="FuryFactory"/> / <see cref="SubtletyFactory"/>).
/// - <b>ETB destroy trigger (CR 603.6a)</b> over a
///   <see cref="EventTriggerCondition{T}"/> filtered to (this card,
///   ToZone = Battlefield), declaring a single 1..1
///   <see cref="TargetRequest"/> for "target artifact". Resolution reads
///   <see cref="TriggeredAbility.ChosenTargets"/>; validates the chosen
///   target is still an artifact on the battlefield (CR 608.2b — illegal
///   target → clean no-op); destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7 — indestructible
///   cancels per CR 702.12; active regeneration shield consumed per
///   CR 701.15). Same destroy posture as <see cref="ReclamationSageFactory"/>.
///   Note Ingot Chewer's destroy is mandatory (no "you may") and restricted
///   to artifacts only.
///
/// ## Deferred (v1 gaps — same posture as <see cref="ReclamationSageFactory"/>)
/// - <b>Real agent-driven target prompt</b>: production callers wire
///   <see cref="TriggeredAbility.SetChosenTargets"/> from an agent prompt
///   before triggers resolve. The factory falls back to the first legal
///   artifact in the gatherer pool deterministically when no agent picked.
/// - <b>Target legality in ActionValidator</b>: the validator does not
///   filter to "artifact" at announcement; the resolution-time guard handles
///   illegal targets (CR 608.2b).
/// </summary>
[CardName("Ingot Chewer")]
public static class IngotChewerFactory
{
    public const string CardName = "Ingot Chewer";
    public const string Slug = "ingot-chewer";

    /// <summary>CR 702.74 — Evoke marker keyword.</summary>
    private const string EvokeKeyword = "Evoke";

    /// <summary>
    /// Construct Ingot Chewer with no live trigger-manager wiring. Produces
    /// the correct card identity + Evoke marker + sacrifice trigger + ETB
    /// destroy trigger shape; the triggers are NOT registered with any
    /// <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Ingot Chewer with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied the ETB destroy
    /// trigger and the Evoke sacrifice trigger are registered for bus-driven
    /// firing (CR 603.2).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elemental subtype, {4}{R}, 3/3). The JSON carries no abilities —
        // Evoke + the ETB destroy trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Evoke {R} — CR 702.74. The NamedCardFactory / direct-test path
        // doesn't run KeywordBinder, so attach the marker here for parity
        // with the data-driven load (mirrors SubtletyFactory / FuryFactory).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility(EvokeKeyword, card, owner));

        // Printed evoke sacrifice trigger (CR 702.74b):
        //   "When this creature enters, if its evoke cost was paid,
        //    sacrifice it."
        var evokeSac = EvokeFactory.Build(card);
        card.AddAbility(evokeSac);
        triggers?.RegisterTriggeredAbility(evokeSac);

        // ----------------------------------------------------------------
        // ETB destroy trigger — CR 603.6a.
        //   "When this creature enters, destroy target artifact."
        // Single 1..1 TargetRequest restricted to artifacts. Live gatherer
        // enumerates the battlefield across every player so the agent's
        // target picker sees an up-to-date legal set at resolution
        // (BotIntent.Removal flips ownership priority).
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;

        var etbEffect = new Effect(
            $"{CardName}: destroy target artifact",
            () => ResolveDestroy(owner, etb));

        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: GatherTargets(owner).Cast<object>().ToList(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact))
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
    private static IReadOnlyList<ICard> GatherTargets(Player owner) =>
        owner.Zones.Battlefield.GetCards()
            .Where(c => c.HasType(CardType.Artifact))
            .ToList();

    /// <summary>
    /// Resolve the ETB destroy. Honours <see cref="TriggeredAbility.ChosenTargets"/>
    /// when set by the agent; otherwise falls back to the first legal artifact
    /// in the controller-scoped gatherer pool (deterministic single-arg
    /// dispatcher posture — mirrors <see cref="ReclamationSageFactory"/>).
    /// Validates the chosen target is still an artifact on the battlefield
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

        // 2) Deterministic fallback — first legal artifact on the
        //    controller's battlefield (no-agent dispatcher posture). The
        //    trigger's gatherer needs a live GameContext we don't have here,
        //    so we fall back to the controller-scoped snapshot.
        picked ??= GatherTargets(owner).OfType<Permanent>().FirstOrDefault();

        if (picked == null) return;

        // CR 608.2b — illegal-on-resolution check: must still be an artifact
        // permanent on the battlefield.
        if (picked.Zone != ZoneType.Battlefield) return;
        if (!picked.HasType(CardType.Artifact)) return;

        // CR 701.7 — destroy. Indestructible (CR 702.12) cancels; active
        // regeneration shield (CR 701.15) is consumed.
        OracleSpellBinder.MoveToGraveyard(picked, ZoneMoveReason.Destroy);
    }
}
