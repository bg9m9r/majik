using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "As an additional cost to cast this spell, sacrifice an artifact or
/// creature." — Deadly Dispute (Commander Legends: Battle for Baldur's Gate
/// / reprints, {1}{B}). Disjunctive additional cost (CR 601.2f) where the
/// caster picks ONE permanent — an artifact OR a creature they control — to
/// sacrifice at announcement time.
///
/// ## v1 picker policy
/// Sibling shape to <see cref="SacrificeAnArtifactOrDiscardCardAdditionalCost"/>
/// (Demand Answers — same OR-disjunction, but its second mode is a discard
/// instead of a creature sacrifice) and to <see cref="SacrificeACreatureAdditionalCost"/>
/// / <see cref="SacrificeAnArtifactAdditionalCost"/>. Both modes here are
/// sacrifices, so the picker collapses to a single battlefield scan: the
/// first eligible permanent the caster controls that is an artifact OR a
/// creature is sacrificed (CR 701.16). An artifact creature qualifies under
/// either branch — the combined OR-filter accepts it. <see cref="CanPay"/> is
/// the OR of the two modes — payable so long as the caster controls at least
/// one artifact or creature.
///
/// After payment <see cref="Sacrificed"/> holds the chosen permanent so
/// downstream effects can reference it. Deadly Dispute's resolve doesn't read
/// the sacrificed permanent (it draws two cards and makes a Treasure
/// regardless), but exposing the reference matches the sibling-cost pattern.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven choice</b>: v1 picks the first eligible artifact-or-creature
///   (deterministic). A full agent prompt ("which permanent do you sacrifice?")
///   shares a queue with the sibling sacrifice-picker costs' deferred prompts.
/// - <b>Self-sacrifice loophole</b>: same posture as
///   <see cref="SacrificeAnArtifactAdditionalCost"/> — the picker does NOT
///   exclude any specific permanent; first eligible wins. Deadly Dispute is an
///   Instant, not a permanent, so it can never sacrifice itself.
/// </summary>
public sealed class SacrificeAnArtifactOrCreatureAdditionalCost : IChooseAdditionalCostPayment
{
    private readonly IEventBus? _eventBus;

    /// <summary>
    /// The caster's chosen permanent to sacrifice (CR 601.2f — "the caster
    /// chooses the permanent at announcement"). Stamped by
    /// <see cref="ApplyChoice"/> from the cast pipeline's CR 601.2h chooser
    /// prompt; null falls back to the legacy first-eligible auto-pick.
    /// </summary>
    private Permanent? _chosen;

    /// <param name="eventBus">Optional event bus — publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) on payment so
    /// aristocrat payoffs fire. Null preserves the legacy posture.</param>
    public SacrificeAnArtifactOrCreatureAdditionalCost(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    /// <summary>The permanent sacrificed by <see cref="Pay"/> (an artifact or
    /// a creature). Null before payment.</summary>
    public Permanent? Sacrificed { get; private set; }

    /// <inheritdoc/>
    public string Description => "sacrifice an artifact or creature";

    /// <inheritdoc/>
    /// <remarks>
    /// CR 117.1 — payable if the caster controls at least one permanent that
    /// is an artifact or a creature (CR 601.2f — the disjunction is satisfied
    /// by either type).
    /// </remarks>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        return caster.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Any(IsArtifactOrCreature);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// v1 deterministic pick: the first eligible artifact-or-creature on the
    /// caster's battlefield is sacrificed (CR 601.2f — the caster chooses the
    /// permanent at announcement; v1 simplifies to a fixed first-eligible
    /// pick). CR 701.16 — sacrifice is an owner-routed move to the graveyard,
    /// bypassing Indestructible / regeneration.
    /// </remarks>
    public bool Pay(Player caster)
    {
        if (caster == null) return false;

        // CR 601.2f — honour the caster's CR 601.2h chooser pick when it is
        // still a legal sacrifice; otherwise fall back to the first eligible
        // permanent (the legacy deterministic default, used by tests / bots
        // that don't supply a choice, and as a defensive guard if the chosen
        // permanent left the battlefield between choice and payment).
        var pick = _chosen != null
            && caster.Zones.Battlefield.GetCards().Contains(_chosen)
            && IsArtifactOrCreature(_chosen)
                ? _chosen
                : caster.Zones.Battlefield.GetCards()
                    .OfType<Permanent>()
                    .FirstOrDefault(IsArtifactOrCreature);
        if (pick == null) return false;

        SacrificeCostHelper.Sacrifice(caster, pick, _eventBus);
        Sacrificed = pick;
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// CR 601.2h — prompt the caster to choose which artifact-or-creature to
    /// sacrifice, AFTER target choice (CR 601.2c). Returns null (no prompt)
    /// when zero or one eligible permanent exists — the cost picks the forced
    /// choice itself.
    /// </remarks>
    public ChoiceRequest? BuildChoiceRequest(Player caster)
    {
        if (caster == null) return null;
        var eligible = caster.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(IsArtifactOrCreature)
            .ToList();
        if (eligible.Count <= 1) return null;

        return new ChoiceRequest(
            Kind: ChoiceKind.PickOne,
            Description: $"Choose a permanent to sacrifice ({Description})",
            Min: 1,
            Max: 1,
            Candidates: eligible.Cast<object>().ToList(),
            Intent: BotIntent.None,
            Optional: false);
    }

    /// <inheritdoc/>
    public void ApplyChoice(IReadOnlyList<object> chosen)
    {
        _chosen = chosen?.OfType<Permanent>().FirstOrDefault();
    }

    private static bool IsArtifactOrCreature(Permanent p) =>
        p.HasType(CardType.Artifact) || p.HasType(CardType.Creature);
}
