using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Costs;

/// <summary>
/// "Tap N untapped artifacts and/or creatures you control" — an
/// activated-ability cost that taps a fixed number of untapped permanents the
/// controller controls that are EITHER an artifact OR a creature (CR 602.2b /
/// CR 118.12 — a printed "Tap …" word on a set of permanents is a
/// tap-as-cost). A single permanent that is both an artifact AND a creature
/// (e.g. an artifact creature) counts once toward the total.
///
/// Warden of the Inner Sky is the canonical consumer:
///   "Tap three untapped artifacts and/or creatures you control: Put a +1/+1
///    counter on this creature. Scry 1. Activate only as a sorcery."
///
/// This is the mixed-pool sibling of <see cref="TapTwoUntappedArtifactsCost"/>
/// (artifacts only) and <see cref="TapElvesYouControlCost"/> (Elves only) — it
/// widens the eligible set to "artifact OR creature" rather than introducing a
/// new cost shape.
///
/// ## Eligibility (deliberately NOT gated on summoning sickness)
/// Like its siblings, this is the printed "Tap …" wording, NOT a <c>{T}</c>
/// symbol in the ability's activation cost. CR 302.6 restricts only abilities
/// whose <i>activation cost</i> contains the tap/untap <i>symbol</i> — i.e. a
/// permanent tapping <i>itself</i>. Warden's cost taps OTHER permanents via the
/// printed word "Tap", so summoning sickness does not restrict which creatures
/// may be chosen. Eligible = on the controller's battlefield, controlled by
/// that player, has <see cref="CardType.Artifact"/> or
/// <see cref="CardType.Creature"/>, and untapped.
///
/// ## Self-inclusion
/// Selecting by <see cref="Permanent.HasType"/> rather than the C# class
/// captures pure artifacts, pure creatures, AND artifact creatures. Warden of
/// the Inner Sky is itself a creature you control, so it is an eligible choice
/// (it may tap itself as one of the three — CR 602.2b places no "other"
/// restriction here; tapping the source as part of the cost is legal and does
/// not gate on summoning sickness because the cost uses the printed word, not
/// a {T} symbol).
///
/// ## Deferred (v1 gaps)
/// - <see cref="Targets"/> may be set by the agent to pick exactly which
///   permanents to tap; when null/insufficient the cost falls back to the first
///   <see cref="Count"/> eligible permanents in battlefield order
///   (deterministic v1, same posture as the rest of the tap-as-cost family).
/// </summary>
public sealed class TapUntappedArtifactsOrCreaturesCost : ICost
{
    /// <summary>Number of untapped permanents that must be tapped.</summary>
    public int Count { get; }

    /// <summary>
    /// Optionally set by the agent to indicate exactly which permanents to tap.
    /// When null or under-populated the cost falls back to the first
    /// <see cref="Count"/> eligible permanents on the controller's battlefield
    /// (deterministic v1).
    /// </summary>
    public IReadOnlyList<Permanent>? Targets { get; set; }

    public TapUntappedArtifactsOrCreaturesCost(int count = 3)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Must tap at least one permanent.");
        Count = count;
    }

    public string Description => $"tap {Count} untapped artifacts and/or creatures you control";

    private static bool IsEligible(Permanent p) =>
        (p.HasType(CardType.Artifact) || p.HasType(CardType.Creature)) && !p.IsTapped;

    private static IEnumerable<Permanent> Eligible(Player player) =>
        player.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => IsEligible(p) && ReferenceEquals(p.Controller, player));

    /// <inheritdoc/>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return Eligible(player).Count() >= Count;
    }

    /// <inheritdoc/>
    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        // Prefer the agent's explicit choice when valid + sufficient; otherwise
        // fall back to the first Count eligible permanents in battlefield order
        // (deterministic v1).
        List<Permanent> chosen;
        if (Targets != null
            && Targets.Count >= Count
            && Targets.Take(Count).All(p => p != null
                                            && IsEligible(p)
                                            && ReferenceEquals(p.Controller, player))
            && Targets.Take(Count).Distinct(ReferenceEqualityComparer.Instance).Count() >= Count)
        {
            chosen = Targets.Take(Count).ToList();
        }
        else
        {
            chosen = Eligible(player).Take(Count).ToList();
        }

        if (chosen.Count < Count)
            throw new InvalidOperationException(
                $"Cannot pay {Description}: only {chosen.Count} eligible untapped permanents.");

        foreach (var permanent in chosen)
            permanent.Tap();
    }
}
