using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// "Tap an untapped artifact you control" — activation cost that taps an
/// artifact OTHER than (or including) the ability's source as an additional
/// cost (CR 118.12 — a tap symbol on an object names it as part of the
/// activation cost). Direct artifact-typed sibling of
/// <see cref="TapAnotherUntappedCreatureCost"/> — same deterministic-first
/// fallback when <see cref="Target"/> is null, but the eligibility filter
/// is "is an artifact" rather than "is a creature".
///
/// Urza, Lord High Artificer is the canonical consumer:
///   <c>Tap an untapped artifact you control: Add {U}.</c>
/// Note Urza's mana ability does NOT tap Urza itself — the whole activation
/// cost is the tap-another-artifact. The mana ability is therefore wired
/// with <c>tapsAsCost: false</c> (no implicit {T} on the source) and this
/// cost expresses the only tap. The tapped artifact MAY be Urza itself if
/// Urza is also an artifact, but in the common case it is a separate
/// artifact the controller owns.
///
/// ## Eligibility (CR 302.6 summoning-sickness)
/// An artifact that is ALSO a creature is subject to CR 302.6 when tapped
/// as a cost — a creature its controller has not controlled continuously
/// since their most recent turn began can't be tapped to pay a cost (unless
/// it has haste). A non-creature artifact has no such restriction. The
/// filter therefore gates summoning-sickness only for artifacts that are
/// creatures (mirrors <see cref="TapAnotherUntappedCreatureCost"/>'s
/// honouring of <see cref="Creature.HasSummoningSickness"/>).
///
/// ## Deferred (v1 gaps)
/// - <see cref="Target"/> must be set by the agent before <see cref="Pay"/>
///   is called; otherwise the first eligible untapped artifact is chosen
///   deterministically. Same posture as the rest of the additional-cost
///   family (<see cref="TapAnotherUntappedCreatureCost"/>,
///   <see cref="SacrificeAnotherCreatureCost"/>).
/// </summary>
public sealed class TapAnotherUntappedArtifactCost : ICost
{
    private readonly Permanent? _self;

    /// <summary>
    /// Optionally set by the agent to indicate which artifact to tap.
    /// When null the cost falls back to the first eligible untapped
    /// artifact on the controller's battlefield (deterministic v1).
    /// </summary>
    public Permanent? Target { get; set; }

    /// <summary>
    /// Build the cost. <paramref name="self"/> is the ability's source
    /// permanent (Urza). It is allowed to be tapped to pay this cost when
    /// it is itself an artifact (Urza is not, so in practice a separate
    /// artifact pays), so it is NOT excluded from eligibility — unlike the
    /// creature cost, whose printed wording on Springleaf Drum is "an
    /// untapped creature you control" alongside a separate {T} on the drum.
    /// Pass null when there is no distinguished source to reference in the
    /// description.
    /// </summary>
    public TapAnotherUntappedArtifactCost(Permanent? self = null)
    {
        _self = self;
    }

    public string Description => "tap an untapped artifact you control";

    private static bool IsEligible(Permanent p)
    {
        if (!p.HasType(CardType.Artifact)) return false;
        if (p.IsTapped) return false;
        // CR 302.6 — only creatures are subject to summoning sickness when
        // tapped to pay a cost. A non-creature artifact is always eligible.
        if (p is Creature c && c.HasSummoningSickness) return false;
        return true;
    }

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
        if (player == null) throw new ArgumentNullException(nameof(player));

        var pick = Target != null && IsEligible(Target)
            ? Target
            : player.Zones.Battlefield.GetCards()
                .OfType<Permanent>()
                .FirstOrDefault(IsEligible);

        if (pick == null)
            throw new InvalidOperationException(
                $"Cannot pay '{Description}': no eligible untapped artifact.");

        pick.Tap();
    }
}
