using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// "Tap N untapped Advisors you control" — an activated-ability cost that taps
/// a fixed number of untapped creatures with the
/// <see cref="CardSubtype.Advisor"/> subtype on the controller's battlefield
/// (CR 602.2b / 118.12 — a tap word on a set of permanents is a tap-as-cost).
///
/// Persistent Petitioners is the canonical consumer:
///   "Tap four untapped Advisors you control: Target player mills twelve cards."
///
/// The Advisor twin of <see cref="TapSpiritsYouControlCost"/> (Shacklegeist's
/// "Tap two untapped Spirits you control") and
/// <see cref="TapElvesYouControlCost"/> (Heritage Druid's "Tap three untapped
/// Elves you control"); the only difference is the counted subtype and the
/// fixed count.
///
/// ## Eligibility (deliberately NOT gated on summoning sickness)
/// Like the Spirit/Elf twins, this cost is the printed "Tap … Advisors"
/// wording, NOT a <c>{T}</c> symbol in the ability's activation cost. CR 302.6
/// restricts only abilities whose <i>activation cost</i> contains the tap/untap
/// <i>symbol</i> — i.e. a creature tapping <i>itself</i>. Persistent
/// Petitioners' second ability taps OTHER Advisors (and may include the source
/// itself) via the printed word "Tap", so summoning sickness does not restrict
/// which Advisors may be chosen. Eligible Advisor = on the controller's
/// battlefield, an Advisor, and untapped.
///
/// ## Self-inclusion
/// The source itself is an Advisor you control, so it is an eligible choice
/// (CR 602.2b places no "other" restriction here, contrast
/// <see cref="SacrificeAnotherCreatureCost"/>).
///
/// ## Deferred (v1 gaps)
/// - <see cref="Targets"/> may be set by the agent to pick exactly which
///   Advisors to tap; when null/insufficient the cost falls back to the first
///   <see cref="Count"/> eligible Advisors in battlefield order (deterministic
///   v1, same posture as the rest of the additional-cost family).
/// </summary>
public sealed class TapAdvisorsYouControlCost : ICost
{
    /// <summary>Number of untapped Advisors that must be tapped.</summary>
    public int Count { get; }

    /// <summary>
    /// Optionally set by the agent to indicate exactly which Advisors to tap.
    /// When null or under-populated the cost falls back to the first
    /// <see cref="Count"/> eligible Advisors on the controller's battlefield
    /// (deterministic v1).
    /// </summary>
    public IReadOnlyList<Creature>? Targets { get; set; }

    public TapAdvisorsYouControlCost(int count = 4)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Must tap at least one Advisor.");
        Count = count;
    }

    public string Description => $"tap {Count} untapped Advisors you control";

    private static bool IsEligible(Creature c) =>
        c.HasSubtype(CardSubtype.Advisor) && !c.IsTapped;

    private static IEnumerable<Creature> EligibleAdvisors(Player player) =>
        player.Zones.Battlefield.GetCards().OfType<Creature>().Where(IsEligible);

    /// <inheritdoc/>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return EligibleAdvisors(player).Count() >= Count;
    }

    /// <inheritdoc/>
    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        // Prefer the agent's explicit choice when it is valid + sufficient;
        // otherwise fall back to the first Count eligible Advisors in
        // battlefield order (deterministic v1).
        List<Creature> chosen;
        if (Targets != null
            && Targets.Count >= Count
            && Targets.Take(Count).All(IsEligible)
            && Targets.Take(Count).Distinct().Count() >= Count)
        {
            chosen = Targets.Take(Count).ToList();
        }
        else
        {
            chosen = EligibleAdvisors(player).Take(Count).ToList();
        }

        if (chosen.Count < Count)
            throw new InvalidOperationException(
                $"Cannot pay {Description}: only {chosen.Count} eligible untapped Advisors.");

        foreach (var advisor in chosen)
            advisor.Tap();
    }
}
