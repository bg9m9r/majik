using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "Tap N untapped white creatures you control" — a non-mana additional cost
/// (CR 601.2f / CR 118.12 — the printed word "Tap" applied to a set of
/// permanents is a tap-as-cost). Battle Screech's flashback cost is the
/// canonical consumer:
///   "Flashback—Tap three untapped white creatures you control."
///
/// This is the colour-gated sibling of
/// <see cref="SacrificeAGoblinAdditionalCost"/> /
/// <see cref="TapSpiritsYouControlCost"/> — same fixed-count, deterministic
/// "first eligible in battlefield order" payment policy, but the eligibility
/// filter is "creature you control that is white and untapped" rather than a
/// subtype / sacrifice. White colour is read via
/// <see cref="CardColors.GetColors"/> so both real white creatures (white
/// pip in the mana cost) and white tokens (TokenColorsOverride) qualify
/// (CR 105 / CR 202.2).
///
/// ## Eligibility (deliberately NOT gated on summoning sickness)
/// Like the rest of the tap-as-cost family, this is the printed "Tap …"
/// wording, NOT a <c>{T}</c> symbol in an activation cost. CR 302.6 restricts
/// only abilities whose <i>activation cost</i> contains the tap/untap
/// <i>symbol</i> — i.e. a permanent tapping <i>itself</i>. Battle Screech is a
/// Sorcery (cast from the graveyard via flashback), not a creature, so it
/// never taps itself, and summoning sickness does not restrict which white
/// creatures may be chosen.
///
/// ## Deferred (v1 gaps)
/// - Agent-side "choose which white creatures to tap" prompt isn't surfaced;
///   v1 deterministically taps the first <see cref="Count"/> eligible white
///   creatures in battlefield order (same posture as the rest of the
///   additional-cost / tap-as-cost family).
/// </summary>
public sealed class TapWhiteCreaturesAdditionalCost : IAdditionalCost
{
    /// <summary>Number of untapped white creatures that must be tapped.</summary>
    public int Count { get; }

    public TapWhiteCreaturesAdditionalCost(int count = 3)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Must tap at least one creature.");
        Count = count;
    }

    public string Description => $"tap {Count} untapped white creatures you control";

    private static bool IsEligible(Creature c, Player caster) =>
        ReferenceEquals(c.Controller, caster)
        && c.Zone == ZoneType.Battlefield
        && !c.IsTapped
        && CardColors.GetColors(c).Contains(ManaColor.White);

    private static IEnumerable<Creature> Eligible(Player caster) =>
        caster.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => IsEligible(c, caster));

    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        return Eligible(caster).Count() >= Count;
    }

    public bool Pay(Player caster)
    {
        if (caster == null) return false;

        var chosen = Eligible(caster).Take(Count).ToList();
        if (chosen.Count < Count) return false;

        foreach (var creature in chosen)
            creature.Tap();
        return true;
    }
}
