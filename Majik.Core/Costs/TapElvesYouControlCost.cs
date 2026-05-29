using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// "Tap N untapped Elves you control" — an activated-ability cost that taps
/// a fixed number of untapped creatures with the
/// <see cref="CardSubtype.Elf"/> subtype on the controller's battlefield
/// (CR 602.2b / 118.12 — a tap symbol on a set of permanents is a
/// tap-as-cost).
///
/// Heritage Druid is the canonical consumer:
///   "Tap three untapped Elves you control: Add {G}{G}{G}."
///
/// ## Eligibility (deliberately NOT gated on summoning sickness)
/// Unlike <see cref="TapAnotherUntappedCreatureCost"/> (Springleaf Drum),
/// this cost is the printed "Tap three untapped Elves" wording, NOT a
/// <c>{T}</c> symbol in the ability's activation cost. CR 302.6 restricts
/// only abilities whose <i>activation cost</i> contains the tap/untap
/// <i>symbol</i> — i.e. a creature tapping <i>itself</i>. Heritage Druid's
/// cost taps OTHER Elves (and may include the Druid) via the printed word
/// "Tap", so summoning sickness does not restrict which Elves may be chosen.
/// Eligible Elf = on the controller's battlefield, an Elf, and untapped.
///
/// ## Self-inclusion
/// The source itself is an Elf you control, so it is an eligible choice
/// (Heritage Druid may tap itself as one of the three — CR 602.2b places no
/// "other" restriction here, contrast <see cref="SacrificeAnotherCreatureCost"/>).
///
/// ## Deferred (v1 gaps)
/// - <see cref="Targets"/> may be set by the agent to pick exactly which
///   Elves to tap; when null/insufficient the cost falls back to the first
///   <see cref="Count"/> eligible Elves in battlefield order (deterministic
///   v1, same posture as the rest of the additional-cost family).
/// </summary>
public sealed class TapElvesYouControlCost : ICost
{
    /// <summary>Number of untapped Elves that must be tapped.</summary>
    public int Count { get; }

    /// <summary>
    /// Optionally set by the agent to indicate exactly which Elves to tap.
    /// When null or under-populated the cost falls back to the first
    /// <see cref="Count"/> eligible Elves on the controller's battlefield
    /// (deterministic v1).
    /// </summary>
    public IReadOnlyList<Creature>? Targets { get; set; }

    public TapElvesYouControlCost(int count = 3)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Must tap at least one Elf.");
        Count = count;
    }

    public string Description => $"tap {Count} untapped Elves you control";

    private static bool IsEligible(Creature c) =>
        c.HasSubtype(CardSubtype.Elf) && !c.IsTapped;

    private static IEnumerable<Creature> EligibleElves(Player player) =>
        player.Zones.Battlefield.GetCards().OfType<Creature>().Where(IsEligible);

    /// <inheritdoc/>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return EligibleElves(player).Count() >= Count;
    }

    /// <inheritdoc/>
    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        // Prefer the agent's explicit choice when it is valid + sufficient;
        // otherwise fall back to the first Count eligible Elves in
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
            chosen = EligibleElves(player).Take(Count).ToList();
        }

        if (chosen.Count < Count)
            throw new InvalidOperationException(
                $"Cannot pay {Description}: only {chosen.Count} eligible untapped Elves.");

        foreach (var elf in chosen)
            elf.Tap();
    }
}
