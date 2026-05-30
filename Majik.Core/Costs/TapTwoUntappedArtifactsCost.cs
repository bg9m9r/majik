using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "Tap N untapped artifacts you control" — an activated-ability cost that
/// taps a fixed number of untapped permanents the controller controls that
/// have card type <see cref="CardType.Artifact"/> (CR 602.2b / 118.12 — a
/// printed "Tap …" word on a set of permanents is a tap-as-cost).
///
/// Whirler Rogue is the canonical consumer:
///   "Tap two untapped artifacts you control: Target creature can't be
///    blocked this turn."
///
/// ## Eligibility (deliberately NOT gated on summoning sickness)
/// Like <see cref="TapElvesYouControlCost"/>, this is the printed "Tap two
/// untapped artifacts" wording, NOT a <c>{T}</c> symbol in the ability's
/// activation cost. CR 302.6 restricts only abilities whose <i>activation
/// cost</i> contains the tap/untap <i>symbol</i> — i.e. a permanent tapping
/// <i>itself</i>. Whirler Rogue's cost taps OTHER artifacts via the printed
/// word "Tap", so summoning sickness does not restrict which artifacts may
/// be chosen (and most artifacts are not creatures anyway). Eligible
/// artifact = on the controller's battlefield, has <see cref="CardType.Artifact"/>,
/// and untapped.
///
/// ## Artifact selection by card type, not C# class
/// Selecting by <see cref="Permanent"/> + <see cref="Permanent.HasType"/>
/// (CardType.Artifact) — NOT the C# <see cref="Artifact"/> class — captures
/// BOTH pure Artifacts AND Artifact Creatures (e.g. a Thopter token is a
/// <see cref="Creature"/> with CardType.Artifact additively flagged). Same
/// posture as <see cref="ImproviseAdditionalCost.AvailableArtifacts"/> /
/// <see cref="SacrificeAnArtifactCost"/>.
///
/// ## Self-inclusion
/// Whirler Rogue itself is NOT an artifact, so it cannot pay its own cost —
/// but this cost places no "other" restriction; any artifact the controller
/// controls (including artifacts the ability's source might be, for other
/// hypothetical consumers) is an eligible choice.
///
/// ## Deferred (v1 gaps)
/// - <see cref="Targets"/> may be set by the agent to pick exactly which
///   artifacts to tap; when null/insufficient the cost falls back to the
///   first <see cref="Count"/> eligible artifacts in battlefield order
///   (deterministic v1, same posture as <see cref="TapElvesYouControlCost"/>
///   and the rest of the additional-cost family).
/// </summary>
public sealed class TapTwoUntappedArtifactsCost : ICost
{
    /// <summary>Number of untapped artifacts that must be tapped.</summary>
    public int Count { get; }

    /// <summary>
    /// Optionally set by the agent to indicate exactly which artifacts to
    /// tap. When null or under-populated the cost falls back to the first
    /// <see cref="Count"/> eligible artifacts on the controller's
    /// battlefield (deterministic v1).
    /// </summary>
    public IReadOnlyList<Permanent>? Targets { get; set; }

    public TapTwoUntappedArtifactsCost(int count = 2)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Must tap at least one artifact.");
        Count = count;
    }

    public string Description => $"tap {Count} untapped artifacts you control";

    private static bool IsEligible(Permanent p) =>
        p.HasType(CardType.Artifact) && !p.IsTapped;

    private static IEnumerable<Permanent> EligibleArtifacts(Player player) =>
        player.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => IsEligible(p) && ReferenceEquals(p.Controller, player));

    /// <inheritdoc/>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return EligibleArtifacts(player).Count() >= Count;
    }

    /// <inheritdoc/>
    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        // Prefer the agent's explicit choice when valid + sufficient;
        // otherwise fall back to the first Count eligible artifacts in
        // battlefield order (deterministic v1).
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
            chosen = EligibleArtifacts(player).Take(Count).ToList();
        }

        if (chosen.Count < Count)
            throw new InvalidOperationException(
                $"Cannot pay {Description}: only {chosen.Count} eligible untapped artifacts.");

        foreach (var artifact in chosen)
            artifact.Tap();
    }
}
