using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Players.Agents;

/// <summary>
/// CR 118.9 + Force-of-Will-cycle pitch cost — surfaces
/// <see cref="PitchAlternativeCost"/> candidates for the bot's spell-cast
/// enumeration.
///
/// A pitch spell is identified by the lookup delegate (so the probe stays
/// data-driven: callers can wire the named-card factories' pitch descriptors,
/// or eventually an oracle-text parser). For each card in the caster's hand
/// that returns a descriptor, the probe walks the hand looking for a card of
/// the required color and yields one <see cref="PitchAlternativeCost"/> per
/// (spell, pitch-candidate) pair.
///
/// Probe-level filtering:
///   * Only emits candidates when it's NOT the caster's turn
///     (mirrors <see cref="PitchAlternativeCost.IsLegalInContext(Player)"/>).
///   * Skips the spell card itself as a pitch candidate.
///   * Skips a pitch candidate whose color doesn't include the required color.
///
/// The bot still calls <see cref="IAlternativeCost.CanCastFor(ICard, Player)"/>
/// before bidding, so this probe is the pre-filter, not the source of truth.
/// Composable with other probes via the bot's
/// <see cref="CompositeAlternativeCostProbe"/>.
/// </summary>
public sealed class PitchAltCostProbe : IAlternativeCostProbe
{
    /// <summary>Descriptor of a printed pitch cost on a card. <see cref="LifeCost"/>
    /// is the life rider (0 for Force of Negation, 1 for Force of Will).</summary>
    public readonly record struct PitchDescriptor(ManaColor RequiredColor, int LifeCost);

    private readonly Func<ICard, PitchDescriptor?> _lookup;

    public PitchAltCostProbe(Func<ICard, PitchDescriptor?> lookup)
    {
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
    }

    public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
    {
        // Pitch is illegal on the caster's own turn — early-out (CR 118.9).
        if (ReferenceEquals(ctx.ActivePlayer, caster)) yield break;

        // Only hand-castable spells get pitch candidates.
        if (card.Zone != ZoneType.Hand) yield break;
        if (!ReferenceEquals(card.Owner, caster)) yield break;

        var desc = _lookup(card);
        if (desc is not { } pitch) yield break;

        foreach (var candidate in caster.Zones.Hand.GetCards())
        {
            if (ReferenceEquals(candidate, card)) continue;
            if (!CardColors.GetColors(candidate).Contains(pitch.RequiredColor)) continue;
            yield return new PitchAlternativeCost(pitch.RequiredColor, candidate, pitch.LifeCost);
        }
    }

    /// <summary>
    /// Built-in lookup that recognizes the ship-list of named pitch cards by
    /// name. Wired by callers that don't have a richer per-card metadata
    /// source. Force of Will = blue, +1 life; Force of Negation = blue, 0 life;
    /// Force of Vigor = green, 0 life; Force of Despair = black, 0 life;
    /// Force of Rage = red, 0 life.
    ///
    /// Note: only cards whose printed pitch carries the Force-cycle
    /// not-your-turn timing gate are surfaced here. Snapback / Pyrokinesis /
    /// Soul Spike use the no-timing-gate
    /// <see cref="Majik.Core.Costs.ExileColoredCardAlternativeCost"/> /
    /// <see cref="Majik.Core.Costs.ExileTwoColoredCardsAlternativeCost"/>
    /// primitives and so don't fit this probe's
    /// <see cref="PitchAlternativeCost"/> shape.
    /// </summary>
    public static PitchDescriptor? DefaultLookup(ICard card)
    {
        return card.Name switch
        {
            "Force of Will" => new PitchDescriptor(ManaColor.Blue, 1),
            "Force of Negation" => new PitchDescriptor(ManaColor.Blue, 0),
            "Force of Vigor" => new PitchDescriptor(ManaColor.Green, 0),
            "Force of Despair" => new PitchDescriptor(ManaColor.Black, 0),
            "Force of Rage" => new PitchDescriptor(ManaColor.Red, 0),
            _ => null,
        };
    }
}
