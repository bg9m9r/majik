using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// "Tap N untapped Spirits you control" — an activated-ability cost that taps
/// a fixed number of untapped creatures with the
/// <see cref="CardSubtype.Spirit"/> subtype on the controller's battlefield
/// (CR 602.2b / 118.12 — a tap word on a set of permanents is a tap-as-cost).
///
/// Shacklegeist is the canonical consumer:
///   "Tap two untapped Spirits you control: Tap target creature you don't control."
///
/// The Spirit twin of <see cref="TapElvesYouControlCost"/> (Heritage Druid's
/// "Tap three untapped Elves you control"); the only difference is the
/// counted subtype.
///
/// ## Eligibility (deliberately NOT gated on summoning sickness)
/// Like <see cref="TapElvesYouControlCost"/>, this cost is the printed "Tap …
/// Spirits" wording, NOT a <c>{T}</c> symbol in the ability's activation cost.
/// CR 302.6 restricts only abilities whose <i>activation cost</i> contains the
/// tap/untap <i>symbol</i> — i.e. a creature tapping <i>itself</i>.
/// Shacklegeist's cost taps OTHER Spirits (and may include Shacklegeist
/// itself) via the printed word "Tap", so summoning sickness does not restrict
/// which Spirits may be chosen. Eligible Spirit = on the controller's
/// battlefield, a Spirit, and untapped.
///
/// ## Self-inclusion
/// The source itself is a Spirit you control, so it is an eligible choice
/// (Shacklegeist may tap itself as one of the two — CR 602.2b places no
/// "other" restriction here, contrast <see cref="SacrificeAnotherCreatureCost"/>).
///
/// ## Deferred (v1 gaps)
/// - <see cref="Targets"/> may be set by the agent to pick exactly which
///   Spirits to tap; when null/insufficient the cost falls back to the first
///   <see cref="Count"/> eligible Spirits in battlefield order (deterministic
///   v1, same posture as the rest of the additional-cost family).
/// </summary>
public sealed class TapSpiritsYouControlCost : ICost
{
    /// <summary>Number of untapped Spirits that must be tapped.</summary>
    public int Count { get; }

    /// <summary>
    /// Optionally set by the agent to indicate exactly which Spirits to tap.
    /// When null or under-populated the cost falls back to the first
    /// <see cref="Count"/> eligible Spirits on the controller's battlefield
    /// (deterministic v1).
    /// </summary>
    public IReadOnlyList<Creature>? Targets { get; set; }

    public TapSpiritsYouControlCost(int count = 2)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Must tap at least one Spirit.");
        Count = count;
    }

    public string Description => $"tap {Count} untapped Spirits you control";

    private static bool IsEligible(Creature c) =>
        c.HasSubtype(CardSubtype.Spirit) && !c.IsTapped;

    private static IEnumerable<Creature> EligibleSpirits(Player player) =>
        player.Zones.Battlefield.GetCards().OfType<Creature>().Where(IsEligible);

    /// <inheritdoc/>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return EligibleSpirits(player).Count() >= Count;
    }

    /// <inheritdoc/>
    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        // Prefer the agent's explicit choice when it is valid + sufficient;
        // otherwise fall back to the first Count eligible Spirits in
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
            chosen = EligibleSpirits(player).Take(Count).ToList();
        }

        if (chosen.Count < Count)
            throw new InvalidOperationException(
                $"Cannot pay {Description}: only {chosen.Count} eligible untapped Spirits.");

        foreach (var spirit in chosen)
            spirit.Tap();
    }
}
