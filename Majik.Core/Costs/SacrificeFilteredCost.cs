using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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
/// </summary>
public sealed class SacrificeFilteredCost : ICost
{
    private readonly Func<Permanent, bool> _filter;

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
    public SacrificeFilteredCost(Func<Permanent, bool> filter, string description)
    {
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        Description = description ?? "sacrifice a permanent";
    }

    /// <summary>
    /// "Sacrifice a token" (CR 111.8 / 701.16) — Fountainport's draw ability.
    /// </summary>
    public static SacrificeFilteredCost ForToken() =>
        new(p => p.IsToken, "sacrifice a token");

    /// <summary>
    /// "Sacrifice a &lt;subtype&gt;" (CR 701.16) — Scavenger Grounds
    /// ("Sacrifice a Desert"), Ramunap Ruins, etc. The subtype is matched on
    /// the permanent's printed/effective subtype set via
    /// <see cref="Card.HasSubtype"/>.
    /// </summary>
    public static SacrificeFilteredCost ForSubtype(CardSubtype subtype) =>
        new(p => p.HasSubtype(subtype), $"sacrifice a {subtype}");

    private bool IsEligible(Permanent p) =>
        p.Zone == ZoneType.Battlefield && _filter(p);

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

        player.Zones.Battlefield.RemoveCard(pick);
        player.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
        Target = pick;
    }
}
