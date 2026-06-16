using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "Sacrifice a &lt;filtered&gt; permanent" — a generic, predicate-driven
/// activation cost (CR 117 / CR 701.16). The controller sacrifices ONE
/// permanent they control that matches an arbitrary <see cref="Func{T, TResult}"/>
/// filter — e.g. "Sacrifice a token" (Fountainport), "Sacrifice a Desert"
/// (Scavenger Grounds), or any "Sacrifice a/an &lt;subtype/type&gt;: …"
/// activated-ability cost.
///
/// This generalizes the bespoke <see cref="SacrificeAnArtifactCost"/> /
/// <see cref="SacrificeBasicLandCost"/> family: rather than one class per
/// filter, the predicate is supplied at construction via the
/// <see cref="ForToken"/> / <see cref="ForSubtype"/> factory helpers (or a
/// custom predicate). The battlefield → owner's-graveyard move is identical
/// to the sibling sacrifice costs (CR 701.16 — sacrificed to its owner's
/// graveyard).
///
/// <para>Posture matches <see cref="SacrificeAnArtifactCost"/>: the agent
/// may pre-set <see cref="Target"/> to choose which matching permanent dies;
/// when null the cost deterministically picks the first eligible permanent on
/// the controller's battlefield (v1 — full agent-driven prompting is the same
/// deferred MVP the sibling sacrifice-picker costs wait on). After
/// <see cref="Pay"/> succeeds, <see cref="Target"/> reflects the permanent
/// actually sacrificed so downstream effects can reference it.</para>
///
/// <para>The source permanent IS eligible to pay itself when it matches the
/// filter (CR 701.16 — Scavenger Grounds is itself a Desert, so it can
/// sacrifice itself to its own ability when it is the only Desert).</para>
///
/// <para><b>Prompted choice.</b> Implements
/// <see cref="IChoosePermanentToSacrificeCost"/> so the live activation dispatch
/// (<c>SacrificeCostPrompt.ChooseSacrificesAsync</c>) prompts the controller to
/// choose WHICH eligible permanent to sacrifice (CR 700.6) when more than one
/// qualifies — e.g. a Ramunap Ruins controller with several Deserts picks the
/// one to sacrifice rather than the engine silently taking the first. The pick
/// is stamped onto <see cref="Target"/> before <see cref="Pay"/> runs; a null /
/// declined choice falls back to the first eligible permanent (the legacy
/// deterministic posture, used by factory-direct tests / bot convenience
/// wiring).</para>
/// </summary>
public sealed class SacrificeFilteredCost : ICost, IChoosePermanentToSacrificeCost, IRebindableCost
{
    private readonly Func<Permanent, bool> _filter;
    private readonly IEventBus? _eventBus;

    /// <summary>
    /// STAGE 1 (re-sourceable abilities) — the permanent the "another"
    /// clause excludes from the eligible set (CR 701.16 — "Sacrifice
    /// <i>another</i> Vampire or Zombie" excludes the ability's own source).
    /// Captured EXPLICITLY (rather than baked into <see cref="_filter"/>) so
    /// <see cref="RebindTo"/> can swap it onto a new bearer: when Agatha's
    /// Soul Cauldron re-homes Kalitas's pump onto a different creature, the
    /// "another" exclusion must exclude the NEW bearer, not the original
    /// Kalitas. Null ⇒ no exclusion (the source may pay itself — Scavenger
    /// Grounds is itself a Desert).
    /// </summary>
    private readonly Permanent? _excludeSelf;

    /// <summary>
    /// The chosen permanent to sacrifice. May be pre-set by the agent;
    /// when null the cost falls back to the first eligible permanent the
    /// controller controls (deterministic v1). After <see cref="Pay"/>
    /// returns, this reflects the permanent actually sacrificed.
    /// </summary>
    public Permanent? Target { get; set; }

    /// <inheritdoc/>
    public string Description { get; }

    /// <summary>
    /// Construct a filtered "sacrifice a permanent" cost.
    /// </summary>
    /// <param name="filter">Predicate a controlled permanent must satisfy to
    /// be a legal sacrifice (e.g. <c>p =&gt; p.IsToken</c> or
    /// <c>p =&gt; p.HasSubtype(CardSubtype.Desert)</c>).</param>
    /// <param name="description">Human-readable cost text
    /// (e.g. "sacrifice a token", "sacrifice a Desert").</param>
    /// <param name="eventBus">Optional event bus — publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) on payment so
    /// aristocrat payoffs fire. Null preserves the legacy posture.</param>
    /// <param name="excludeSelf">Optional permanent the "another" clause
    /// excludes from the eligible set (CR 701.16). Captured explicitly so
    /// <see cref="RebindTo"/> can re-home it onto a new bearer (Agatha's Soul
    /// Cauldron grant — CR 707.2). Null ⇒ no exclusion (the source may pay
    /// itself when it matches the filter).</param>
    public SacrificeFilteredCost(
        Func<Permanent, bool> filter,
        string description,
        IEventBus? eventBus = null,
        Permanent? excludeSelf = null)
    {
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        Description = description ?? "sacrifice a permanent";
        _eventBus = eventBus;
        _excludeSelf = excludeSelf;
    }

    /// <summary>
    /// "Sacrifice a token" (CR 111.8 / 701.16) — Fountainport's draw ability.
    /// </summary>
    public static SacrificeFilteredCost ForToken(IEventBus? eventBus = null) =>
        new(p => p.IsToken, "sacrifice a token", eventBus);

    /// <summary>
    /// "Sacrifice a &lt;subtype&gt;" (CR 701.16) — Scavenger Grounds
    /// ("Sacrifice a Desert"), Ramunap Ruins, etc. The subtype is matched on
    /// the permanent's printed/effective subtype set via
    /// <see cref="Card.HasSubtype"/>.
    /// </summary>
    public static SacrificeFilteredCost ForSubtype(CardSubtype subtype, IEventBus? eventBus = null) =>
        new(p => p.HasSubtype(subtype), $"sacrifice a {subtype}", eventBus);

    private bool IsEligible(Permanent p) =>
        p.Zone == ZoneType.Battlefield
        && !ReferenceEquals(p, _excludeSelf)
        && _filter(p);

    /// <inheritdoc/>
    /// <remarks>
    /// STAGE 1 (re-sourceable abilities) — re-home the "another" exclusion
    /// (<see cref="_excludeSelf"/>) onto <paramref name="newSource"/> when (and
    /// only when) it is reference-equal to <paramref name="oldSource"/>. Used by
    /// <see cref="Majik.Core.Abilities.ActivatedAbility.RebindTo"/> so a re-sourced
    /// "Sacrifice another &lt;type&gt;" cost excludes the NEW bearer, not the
    /// original permanent (CR 707.2 / 701.16). The <see cref="_filter"/> predicate
    /// (the "Vampire or Zombie" subtype test) carries no source and passes through
    /// unchanged. Pure — the original cost is unmutated.
    /// </remarks>
    public ICost RebindTo(object oldSource, object newSource)
    {
        if (_excludeSelf is null || !ReferenceEquals(_excludeSelf, oldSource))
        {
            return this;
        }

        return new SacrificeFilteredCost(
            _filter,
            Description,
            _eventBus,
            excludeSelf: newSource as Permanent);
    }

    /// <inheritdoc/>
    public IReadOnlyList<Permanent> EligiblePermanents(Player player)
    {
        if (player == null) return Array.Empty<Permanent>();
        return player.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(IsEligible)
            .ToList();
    }

    /// <inheritdoc/>
    public void ChoosePermanent(Permanent? permanent) => Target = permanent;

    /// <inheritdoc/>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return player.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Any(IsEligible);
    }

    /// <inheritdoc/>
    public void Pay(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        var pick = (Target != null && IsEligible(Target) && ReferenceEquals(Target.Controller, player))
            ? Target
            : player.Zones.Battlefield.GetCards()
                .OfType<Permanent>()
                .FirstOrDefault(IsEligible);

        if (pick == null)
            throw new InvalidOperationException(
                $"Cannot pay {Description}: no eligible permanent to sacrifice.");

        SacrificeCostHelper.Sacrifice(player, pick, _eventBus);
        Target = pick;
    }
}
